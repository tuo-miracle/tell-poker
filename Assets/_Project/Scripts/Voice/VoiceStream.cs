using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Poker.Voice
{
    public enum VoiceStreamEventKind { Text, Binary, Closed }

    public struct VoiceStreamEvent
    {
        public VoiceStreamEventKind Kind;
        public string Text;
        public byte[] Data;
        /// <summary>Closed 時：null＝正常關閉，否則是錯誤原因。</summary>
        public string Error;
    }

    /// <summary>一條 /live 連線。送出不會擋住呼叫端（連上之前先排隊）；收到的訊息由主執行緒用 TryReceive 取出。</summary>
    public interface IVoiceStreamConnection
    {
        void SendText(string json);
        void SendBinary(byte[] data, int count);
        void Close();
        bool TryReceive(out VoiceStreamEvent e);
    }

    /// <summary>開 /live 連線（實機 ClientWebSocket；測試用假的）。</summary>
    public interface IVoiceStreamTransport
    {
        IVoiceStreamConnection Open(string url, string token);
    }

    /// <summary>實機用：System.Net.WebSockets.ClientWebSocket，token 放 x-voice-token header。</summary>
    public sealed class WebSocketStreamTransport : IVoiceStreamTransport
    {
        public int ConnectTimeoutMs = 3000;
        public IVoiceStreamConnection Open(string url, string token) => new WebSocketConnection(url, token, ConnectTimeoutMs);
    }

    sealed class WebSocketConnection : IVoiceStreamConnection
    {
        readonly ClientWebSocket _ws = new ClientWebSocket();
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        readonly ConcurrentQueue<VoiceStreamEvent> _in = new ConcurrentQueue<VoiceStreamEvent>();
        readonly ConcurrentQueue<(bool text, byte[] data)> _out = new ConcurrentQueue<(bool, byte[])>();
        readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        volatile bool _closing;
        int _closedReported;

        public WebSocketConnection(string url, string token, int connectTimeoutMs)
        {
            _ws.Options.SetRequestHeader("x-voice-token", token ?? "");
            Task.Run(() => Run(new Uri(url), connectTimeoutMs));
        }

        public void SendText(string json) { if (!_closing) { _out.Enqueue((true, Encoding.UTF8.GetBytes(json))); _signal.Release(); } }

        public void SendBinary(byte[] data, int count)
        {
            if (_closing) return;
            var b = new byte[count];
            Buffer.BlockCopy(data, 0, b, 0, count);
            _out.Enqueue((false, b));
            _signal.Release();
        }

        public void Close() { if (_closing) return; _closing = true; _signal.Release(); }

        public bool TryReceive(out VoiceStreamEvent e) => _in.TryDequeue(out e);

        void Closed(string error)
        {
            if (Interlocked.Exchange(ref _closedReported, 1) == 0) _in.Enqueue(new VoiceStreamEvent { Kind = VoiceStreamEventKind.Closed, Error = error });
        }

        async Task Run(Uri uri, int connectTimeoutMs)
        {
            try
            {
                using (var connect = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                {
                    connect.CancelAfter(connectTimeoutMs);
                    await _ws.ConnectAsync(uri, connect.Token).ConfigureAwait(false);
                }
                var receive = Task.Run(Receive);
                while (true)
                {
                    await _signal.WaitAsync(_cts.Token).ConfigureAwait(false);
                    while (_out.TryDequeue(out var m))
                        await _ws.SendAsync(new ArraySegment<byte>(m.data), m.text ? WebSocketMessageType.Text : WebSocketMessageType.Binary, true, _cts.Token).ConfigureAwait(false);
                    if (_closing) break;
                }
                try { await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).ConfigureAwait(false); } catch (Exception) { }
                _cts.CancelAfter(1000);
                await receive.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Closed(_closing ? null : ex.GetBaseException().Message);
            }
            finally
            {
                Closed(null);
                _cts.Cancel();
                _ws.Dispose();
            }
        }

        async Task Receive()
        {
            var buf = new byte[64 * 1024];
            var msg = new System.IO.MemoryStream();
            try
            {
                while (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseSent)
                {
                    var r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), _cts.Token).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    msg.Write(buf, 0, r.Count);
                    if (!r.EndOfMessage) continue;
                    var data = msg.ToArray();
                    msg.SetLength(0);
                    if (r.MessageType == WebSocketMessageType.Text) _in.Enqueue(new VoiceStreamEvent { Kind = VoiceStreamEventKind.Text, Text = Encoding.UTF8.GetString(data) });
                    else _in.Enqueue(new VoiceStreamEvent { Kind = VoiceStreamEventKind.Binary, Data = data });
                }
                Closed(null);
            }
            catch (Exception ex)
            {
                Closed(_closing ? null : ex.GetBaseException().Message);
            }
            finally
            {
                // 對方關了：讓送出迴圈也結束
                _closing = true;
                _signal.Release();
            }
        }
    }
}
