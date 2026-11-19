using System;
using System.Collections.Generic;
using Poker.Voice;

namespace Poker.Tests.Voice
{
    /// <summary>合成音訊與假的 HTTP、假的牌桌（測試共用）。</summary>
    public static class Synth
    {
        public const int Rate = 16000;

        public static float[] Tone(double seconds, float amp = 0.2f, int rate = Rate, float hz = 220f)
        {
            var a = new float[(int)Math.Round(seconds * rate)];
            for (int i = 0; i < a.Length; i++) a[i] = amp * (float)Math.Sin(2 * Math.PI * hz * i / rate);
            return a;
        }

        public static float[] Silence(double seconds, int rate = Rate, float noise = 0.001f)
        {
            var a = new float[(int)Math.Round(seconds * rate)];
            var r = new Random(7);
            for (int i = 0; i < a.Length; i++) a[i] = noise * (float)(r.NextDouble() * 2 - 1);
            return a;
        }

        public static float[] Concat(params float[][] parts)
        {
            var list = new List<float>();
            foreach (var p in parts) list.AddRange(p);
            return list.ToArray();
        }

        /// <summary>依 10 ms 一塊餵給 VAD。</summary>
        public static void FeedChunks(EnergyVad vad, float[] audio, int chunk = 160)
        {
            var buf = new float[chunk];
            for (int i = 0; i < audio.Length; i += chunk)
            {
                int n = Math.Min(chunk, audio.Length - i);
                Array.Copy(audio, i, buf, 0, n);
                vad.Feed(buf, n);
            }
        }
    }

    public sealed class FakeTransport : IVoiceTransport
    {
        public readonly List<(string url, string token, string device, string json)> Requests = new List<(string, string, string, string)>();
        public Action<VoiceHttpResult> Pending;
        /// <summary>有設定就同步回應；否則留在 Pending 等測試呼叫 Respond。</summary>
        public Func<string, VoiceHttpResult> Auto;

        public void Post(string url, string token, string device, string json, int timeoutSeconds, Action<VoiceHttpResult> done)
        {
            Requests.Add((url, token, device, json));
            if (Auto != null) done(Auto(json));
            else Pending = done;
        }

        public void Respond(int status, string body)
        {
            var p = Pending;
            Pending = null;
            p?.Invoke(new VoiceHttpResult { Status = status, Body = body, Ok = status >= 200 && status < 300 });
        }

        public void Fail(string error)
        {
            var p = Pending;
            Pending = null;
            p?.Invoke(new VoiceHttpResult { Status = 0, Error = error });
        }
    }

    /// <summary>假的 /live WebSocket：記下送出的東西；測試用 Server* 塞進伺服器端的訊息。</summary>
    public sealed class FakeStreamConnection : IVoiceStreamConnection
    {
        public string Url, Token;
        public readonly List<string> Texts = new List<string>();
        public readonly List<byte[]> Audio = new List<byte[]>();
        public bool Closed;
        readonly Queue<VoiceStreamEvent> _in = new Queue<VoiceStreamEvent>();

        public int AudioBytes { get { int n = 0; foreach (var a in Audio) n += a.Length; return n; } }
        public void SendText(string json) { if (!Closed) Texts.Add(json); }
        public void SendBinary(byte[] data, int count) { if (Closed) return; var b = new byte[count]; Array.Copy(data, b, count); Audio.Add(b); }
        public void Close() => Closed = true;
        public bool TryReceive(out VoiceStreamEvent e)
        {
            if (_in.Count > 0) { e = _in.Dequeue(); return true; }
            e = default;
            return false;
        }

        public void ServerText(string json) => _in.Enqueue(new VoiceStreamEvent { Kind = VoiceStreamEventKind.Text, Text = json });
        public void ServerAudio(byte[] pcm) => _in.Enqueue(new VoiceStreamEvent { Kind = VoiceStreamEventKind.Binary, Data = pcm });
        public void ServerClose(string error = null) => _in.Enqueue(new VoiceStreamEvent { Kind = VoiceStreamEventKind.Closed, Error = error });
    }

    public sealed class FakeStreamTransport : IVoiceStreamTransport
    {
        public readonly List<FakeStreamConnection> Opened = new List<FakeStreamConnection>();
        /// <summary>true：連線一開就斷（模擬中繼沒開 /live）。</summary>
        public bool FailImmediately;
        public FakeStreamConnection Last => Opened.Count > 0 ? Opened[Opened.Count - 1] : null;

        public IVoiceStreamConnection Open(string url, string token)
        {
            var c = new FakeStreamConnection { Url = url, Token = token };
            if (FailImmediately) c.ServerClose("connect refused");
            Opened.Add(c);
            return c;
        }
    }

    public static class LiveReplies
    {
        public static string Reply(string speaker, string text, string intent = "on_topic", string transcript = "hey", int rate = 22050) =>
            "{\"type\":\"reply\",\"speaker\":\"" + speaker + "\",\"transcript\":\"" + transcript + "\",\"text\":\"" + text + "\",\"intent\":\"" + intent +
            "\",\"emotion\":\"smug\",\"fact\":null,\"repaired\":false,\"audioRate\":" + rate + ",\"path\":\"live\"}";

        public const string Done = "{\"type\":\"done\",\"timings\":{\"setup\":120,\"audioIn\":1.2,\"gemini\":540,\"facts\":0,\"ttsFirst\":150,\"firstAudio\":700,\"tts\":250,\"total\":800}}";

        /// <summary>PCM16 mono 的正弦波位元組。</summary>
        public static byte[] Pcm(double seconds, int rate = 22050)
        {
            var f = Synth.Tone(seconds, 0.3f, rate);
            var b = new byte[f.Length * 2];
            for (int i = 0; i < f.Length; i++) { short v = (short)(f[i] * 32767); b[2 * i] = (byte)v; b[2 * i + 1] = (byte)(v >> 8); }
            return b;
        }
    }

    public static class Replies
    {
        public static string Json(string speaker, string text, string intent = "on_topic", string transcript = "hey", byte[] wav = null) =>
            "{\"speaker\":\"" + speaker + "\",\"transcript\":\"" + transcript + "\",\"text\":\"" + text + "\",\"intent\":\"" + intent +
            "\",\"emotion\":\"smug\",\"audio\":" + (wav == null ? "null" : "\"" + Convert.ToBase64String(wav) + "\"") +
            ",\"timings\":{\"gemini\":2700.5,\"facts\":1,\"tts\":210,\"total\":2912}}";

        public static byte[] Beep(double seconds = 0.5, int rate = 22050) =>
            WavCodec.EncodePcm16(Synth.Tone(seconds, 0.3f, rate), 0, (int)Math.Round(seconds * rate), rate);
    }

    public static class States
    {
        public static PublicTableState Flop() => new PublicTableState
        {
            Phase = "betting", Hand = 4, Street = "flop", Board = new List<string> { "Ah", "7d", "2c" },
            SmallBlind = 5, BigBlind = 10, Pot = 60, ToAct = "you", Button = "rocco", YouToCall = 20,
            Seats = new List<PublicSeat>
            {
                new PublicSeat { Name = "you", Stack = 950, Bet = 0, Status = "active" },
                new PublicSeat { Name = "pip", Stack = 980, Bet = 0, Status = "folded" },
                new PublicSeat { Name = "rocco", Stack = 870, Bet = 20, Status = "active" },
                new PublicSeat { Name = "vex", Stack = 1000, Bet = 0, Status = "active" },
            },
            Actions = new List<PublicAction> { new PublicAction { Street = "flop", Who = "rocco", Action = "bet", Amount = 20 } },
        };
    }
}
