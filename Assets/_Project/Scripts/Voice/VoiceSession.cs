using System;
using UnityEngine;

namespace Poker.Voice
{
    /// <summary>牌桌那一側（VoiceChatController 接到 GameController 與角色；測試用假的）。</summary>
    public interface IVoiceTable
    {
        /// <summary>玩家正盯著的角色（None＝沒有）。</summary>
        VoiceCharacter Gazed { get; }
        /// <summary>已經盯著 Gazed 多久（秒）。</summary>
        float GazeDwell { get; }
        /// <summary>有角色正在播語音（半雙工：這時不收音）。</summary>
        bool AnyCharacterSpeaking { get; }
        PublicTableState BuildState();
        string DirectivesJson();
        /// <summary>每個座位現在是誰的聲音（寫實角色；VoiceCast.VoicesJson），中繼用它選配音的聲音。</summary>
        string VoicesJson();
        /// <summary>開始收音：角色做出「在聽」的反應。</summary>
        void OnListening(VoiceCharacter who);
        /// <summary>送出等回覆：角色轉頭看玩家蓋過等待（不顯示字）。</summary>
        void OnFiller(VoiceCharacter who);
        /// <summary>角色回話（整段）：有 samples 就只播聲音；samples 為 null（離線備案）時才顯示字。</summary>
        void Speak(VoiceCharacter who, string text, float[] samples, int sampleRate);
        /// <summary>串流回話開始（第一塊音訊到了）：之後 FeedVoice 一塊塊餵 PCM16，EndVoice 表示不會再有。</summary>
        void BeginVoice(VoiceCharacter who, string text, int sampleRate);
        void FeedVoice(byte[] pcm16, int count);
        void EndVoice();
    }

    public enum VoiceState { Off, Idle, Capturing, Waiting }

    /// <summary>
    /// 語音對話的流程（不碰 Unity 場景，可單元測試）：
    /// 同意＋開啟才運作 → 盯著角色時能量 VAD 偵測開口 →
    /// 串流（預設）：一開口就連中繼的 /live、邊講邊送 PCM，講完送 end，回話音訊一塊塊到就一塊塊播；
    /// 串流連不上或在回話前斷掉 → 用整段 WAV 走 /talk；逾時或 /talk 也失敗 → VoiceFallback（只顯示字）。
    /// 半雙工：角色講話或等回覆時丟掉麥克風資料。
    /// 中繼限流（rate_limited／device_daily_cap／daily_cap）：不用 /talk 重送，直接離線備案；之後一段時間（每日上限＝到 UTC 換日）不再連中繼。
    /// </summary>
    public sealed class VoiceSession
    {
        public const int SendRate = 16000;

        readonly IVoiceTable _table;
        readonly VoiceProxyClient _client;
        readonly IVoiceStreamTransport _streams;
        readonly VoiceConfig _cfg;
        readonly int _micRate;

        public bool Consented;
        public bool UserEnabled;
        /// <summary>麥克風權限（Android 執行期權限被拒時為 false）。</summary>
        public bool MicAllowed = true;
        public bool Enabled => Consented && UserEnabled && MicAllowed;

        public VoiceState State { get; private set; } = VoiceState.Off;
        public EnergyVad Vad { get; }
        public int RequestsSent { get; private set; }
        public VoiceCharacter LastAddressee { get; private set; } = VoiceCharacter.None;
        public VoiceCharacter LastSpeaker { get; private set; } = VoiceCharacter.None;
        public VoiceReply LastReply { get; private set; }
        public string LastFallbackReason { get; private set; }
        /// <summary>現在的 UTC 時間（測試可換）：限流的冷卻與每日上限用。</summary>
        public Func<DateTime> UtcNow = () => DateTime.UtcNow;
        /// <summary>中繼叫我們先別送（限流冷卻中或今天到上限）：這時直接用離線備案。</summary>
        public bool RelayBlocked => UtcNow() < _relayBlockedUntil;
        DateTime _relayBlockedUntil = DateTime.MinValue;

        /// <summary>上一次串流回話：VAD 判定講完到第一塊音訊到手（毫秒；不含 VAD 的結尾靜音）。</summary>
        public float LastFirstAudioMs { get; private set; }

        double _now, _sentAt, _speechAt;
        int _requestId, _fallbackN;
        VoiceCharacter _armedFor = VoiceCharacter.None, _addressee = VoiceCharacter.None;
        PublicTableState _sentState;

        // 串流
        IVoiceStreamConnection _stream;
        bool _streamFailed, _voiceBegun;
        LiveMessage _pendingReply;
        VoiceCharacter _streamSpeaker = VoiceCharacter.None;
        byte[] _pcmBuf = new byte[0];
        double _firstAudioAt;

        public VoiceSession(IVoiceTable table, VoiceProxyClient client, VoiceConfig cfg, int micRate = SendRate, IVoiceStreamTransport streams = null)
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _cfg = cfg ?? new VoiceConfig();
            _streams = streams;
            _micRate = micRate;
            Vad = new EnergyVad(micRate);
            Vad.SpeechStarted += OnSpeechStarted;
            Vad.SpeechAudio += OnSpeechAudio;
            Vad.SegmentEnded += OnSegment;
        }

        bool UseStreaming => _streams != null && _cfg.Streaming;

        public void Tick(float dt, float[] mic, int count)
        {
            _now += dt;
            if (!Enabled)
            {
                if (State != VoiceState.Off) { Vad.Reset(); CancelStream(); EndVoiceIfBegun(); State = VoiceState.Off; _armedFor = VoiceCharacter.None; _requestId++; }
                return;
            }
            if (State == VoiceState.Off) State = VoiceState.Idle;

            PumpStream();
            if (State == VoiceState.Waiting)
            {
                if (_pendingReply == null && _now - _sentAt > _cfg.TimeoutSeconds) Fallback("timeout " + _cfg.TimeoutSeconds.ToString("0.0") + "s", null);
                else if (_pendingReply != null && _now - _sentAt > _cfg.TimeoutSeconds + 15f) FinishStream(null); // 回話來了但音訊一直沒送完
                return; // 等回覆時不收音
            }
            if (_table.AnyCharacterSpeaking)
            {
                if (Vad.IsSpeaking || State == VoiceState.Capturing) { Vad.Reset(); CancelStream(); State = VoiceState.Idle; }
                return;
            }

            var g = _table.Gazed;
            if (g == VoiceCharacter.None) _armedFor = VoiceCharacter.None;
            else if (g != _armedFor && _table.GazeDwell >= _cfg.ListenDwell) { _armedFor = g; _table.OnListening(g); }
            Vad.CanStart = g != VoiceCharacter.None || !_cfg.RequireGaze;
            if (mic != null && count > 0) Vad.Feed(mic, count);
        }

        void OnSpeechStarted()
        {
            State = VoiceState.Capturing;
            _addressee = _table.Gazed;
            if (_addressee != VoiceCharacter.None && _armedFor != _addressee) { _armedFor = _addressee; _table.OnListening(_addressee); }
            _sentState = _table.BuildState();
            _speechAt = _now;
            CancelStream();
            _streamFailed = false;
            if (!UseStreaming || RelayBlocked) return;
            try
            {
                _stream = _streams.Open(VoiceProtocol.LiveUrl(_cfg.ProxyUrl, _cfg.DeviceId), _cfg.Token);
                _stream.SendText(VoiceProtocol.BuildLiveStart(VoiceCast.Id(_addressee), _sentState.ToJson(), _table.DirectivesJson(), _micRate, _table.VoicesJson()));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Voice] stream open failed: " + e.Message);
                _stream = null;
                _streamFailed = true;
            }
        }

        void OnSpeechAudio(float[] samples, int count)
        {
            if (_stream == null || _streamFailed) return;
            if (_pcmBuf.Length < count * 2) _pcmBuf = new byte[count * 2];
            int n = WavCodec.ToPcm16(samples, count, _pcmBuf);
            _stream.SendBinary(_pcmBuf, n);
        }

        void OnSegment(float[] samples)
        {
            if (!Enabled || _table.AnyCharacterSpeaking) { CancelStream(); State = VoiceState.Idle; return; }
            PumpStream(); // 還在 Capturing：連線若已經斷了只會標記 _streamFailed
            LastAddressee = _addressee;
            RequestsSent++; // 也是「玩家講了幾句」（語音教學在數），被限流時照算
            if (RelayBlocked) { CancelStream(); Fallback("relay limit " + _blockCode, null); return; } // 不送、不重試
            int id = ++_requestId;
            _sentAt = _now;
            State = VoiceState.Waiting;
            _pendingReply = null;
            _voiceBegun = false;
            _segment = samples;
            _table.OnFiller(_addressee != VoiceCharacter.None ? _addressee : VoiceCharacter.Dot);
            if (_stream != null && !_streamFailed)
            {
                _stream.SendText(VoiceProtocol.LiveEnd);
                Debug.Log($"[Voice] end addressee={VoiceCast.Id(_addressee)} audio={samples.Length / (float)_micRate:0.00}s speech={(_now - _speechAt):0.00}s (stream)");
            }
            else SendTalk(id);
        }

        float[] _segment;

        void SendTalk(int id)
        {
            CancelStream();
            var samples = _segment ?? new float[0];
            var pcm = _micRate == SendRate ? samples : WavCodec.Resample(samples, samples.Length, _micRate, SendRate);
            var wav = WavCodec.EncodePcm16(pcm, 0, pcm.Length, SendRate);
            Debug.Log($"[Voice] send /talk addressee={VoiceCast.Id(_addressee)} audio={pcm.Length / (float)SendRate:0.00}s");
            _client.Talk(wav, VoiceCast.Id(_addressee), (_sentState ?? _table.BuildState()).ToJson(), _table.DirectivesJson(), (reply, err) => OnReply(id, reply, err), _table.VoicesJson());
        }

        // ================= 串流 =================

        void PumpStream()
        {
            if (_stream == null) return;
            while (_stream != null && _stream.TryReceive(out var e))
            {
                switch (e.Kind)
                {
                    case VoiceStreamEventKind.Text: OnStreamText(e.Text); break;
                    case VoiceStreamEventKind.Binary: OnStreamAudio(e.Data); break;
                    case VoiceStreamEventKind.Closed: OnStreamClosed(e.Error); break;
                }
            }
        }

        void OnStreamText(string json)
        {
            if (!VoiceProtocol.TryParseLive(json, out var m)) return;
            switch (m.Kind)
            {
                case LiveMessageKind.Reply:
                    if (State != VoiceState.Waiting) return;
                    _pendingReply = m;
                    LastReply = m.Reply;
                    _streamSpeaker = VoiceCast.Resolve(m.Reply.Speaker, m.Reply.Transcript, _addressee);
                    LastSpeaker = _streamSpeaker;
                    Debug.Log($"[Voice] reply speaker={VoiceCast.Id(_streamSpeaker)} intent={m.Reply.Intent} path={m.Path} afterEnd={(_now - _sentAt) * 1000:0}ms");
                    break;
                case LiveMessageKind.Done:
                    FinishStream(m);
                    break;
                case LiveMessageKind.Error:
                    if (m.Code != null && BlockRelay(m.Code)) { StreamLimited(m.Code); break; }
                    StreamFailed("proxy: " + m.Error);
                    break;
            }
        }

        void OnStreamAudio(byte[] data)
        {
            if (State != VoiceState.Waiting || _pendingReply == null || data == null || data.Length == 0) return;
            if (_pendingReply.Reply.Intent == "ignore") return;
            if (!_voiceBegun)
            {
                _voiceBegun = true;
                _firstAudioAt = _now;
                LastFirstAudioMs = (float)((_now - _sentAt) * 1000.0);
                _table.BeginVoice(_streamSpeaker, _pendingReply.Reply.Text, _pendingReply.AudioRate > 0 ? _pendingReply.AudioRate : 22050);
            }
            _table.FeedVoice(data, data.Length);
        }

        void OnStreamClosed(string error)
        {
            if (State == VoiceState.Waiting && _pendingReply != null) { FinishStream(null); return; }
            StreamFailed(error ?? "closed");
        }

        void StreamFailed(string reason)
        {
            var s = _stream;
            _stream = null;
            s?.Close();
            if (State == VoiceState.Capturing) { _streamFailed = true; Debug.LogWarning($"[Voice] stream lost while capturing ({reason}); will use /talk"); return; }
            if (State != VoiceState.Waiting) return;
            if (_pendingReply != null) { FinishStream(null); return; }
            Debug.LogWarning($"[Voice] stream failed before reply ({reason}); retry with /talk");
            _sentAt = _now; // /talk 重新計時
            SendTalk(_requestId);
        }

        string _blockCode = "";

        /// <summary>限流代碼 → 停用中繼一段時間；不是限流代碼回 false。</summary>
        bool BlockRelay(string code)
        {
            var now = UtcNow();
            DateTime until;
            if (code == "device_daily_cap" || code == "daily_cap") until = now.Date.AddDays(1); // UTC 換日（中繼的每日上限也用 UTC 日）
            else if (code == "rate_limited") until = now.AddSeconds(Math.Max(1f, _cfg.RateLimitBackoffSeconds));
            else return false;
            if (until > _relayBlockedUntil) _relayBlockedUntil = until;
            _blockCode = code;
            Debug.LogWarning($"[Voice] relay limit {code}: offline replies until {_relayBlockedUntil:yyyy-MM-dd HH:mm:ss} UTC");
            return true;
        }

        /// <summary>串流被限流：不用 /talk 重送（同一個限流器），直接離線備案。</summary>
        void StreamLimited(string code)
        {
            var s = _stream;
            _stream = null;
            s?.Close();
            if (State == VoiceState.Capturing) { _streamFailed = true; return; } // 講完時 OnSegment 看到 RelayBlocked 就走備案
            if (State == VoiceState.Waiting && _pendingReply == null) Fallback("relay limit " + code, null);
        }

        void FinishStream(LiveMessage done)
        {
            var s = _stream;
            _stream = null;
            s?.Close();
            if (State != VoiceState.Waiting) return;
            var reply = _pendingReply;
            _pendingReply = null;
            State = VoiceState.Idle;
            if (reply == null) { Fallback("stream ended without reply", null); return; }
            var t = done?.Timings;
            string proxy = t == null ? "" : $" proxy setup={t.setup:0} gemini={t.gemini:0} facts={t.facts:0} ttsFirst={t.ttsFirst:0} firstAudio={t.firstAudio:0} tts={t.tts:0} total={t.total:0} audioIn={t.audioIn:0.00}s";
            Debug.Log($"[Voice] done speaker={VoiceCast.Id(_streamSpeaker)} path={reply.Path} ms firstAudio={(_voiceBegun ? LastFirstAudioMs : -1):0} (after VAD end; +{Vad.EndSilenceSeconds:0.0}s silence) done={(_now - _sentAt) * 1000:0}{proxy}" +
                      (done?.Error != null ? " proxyError=" + done.Error : ""));
            if (reply.Reply.Intent == "ignore") return;
            if (_voiceBegun) { _voiceBegun = false; _table.EndVoice(); return; }
            // 沒拿到任何音訊（配音失敗）：只好顯示字
            var text = string.IsNullOrWhiteSpace(reply.Reply.Text) ? VoiceFallback.Reply(reply.Reply.Transcript, _sentState, _streamSpeaker, _fallbackN++) : reply.Reply.Text;
            _table.Speak(_streamSpeaker, text, null, 0);
        }

        void CancelStream()
        {
            if (_stream == null) return;
            var s = _stream;
            _stream = null;
            _pendingReply = null;
            s.SendText(VoiceProtocol.LiveCancel);
            s.Close();
        }

        void EndVoiceIfBegun()
        {
            if (!_voiceBegun) return;
            _voiceBegun = false;
            _table.EndVoice();
        }

        // ================= 整段（/talk） =================

        void OnReply(int id, VoiceReply reply, string error)
        {
            double rtt = (_now - _sentAt) * 1000.0;
            if (id != _requestId || State != VoiceState.Waiting)
            {
                Debug.Log($"[Voice] late reply ignored ({(error ?? "ok")}) after {rtt:0} ms");
                return;
            }
            if (error != null)
            {
                var code = VoiceProtocol.LimitCode(error);
                if (code != null) BlockRelay(code);
                Fallback(error, null);
                return;
            }
            LastReply = reply;
            var speaker = VoiceCast.Resolve(reply.Speaker, reply.Transcript, _addressee);
            var t = reply.Timings;
            Debug.Log($"[Voice] reply (talk) speaker={VoiceCast.Id(speaker)} intent={reply.Intent} emotion={reply.Emotion} ms gemini={t.gemini:0} facts={t.facts:0} tts={t.tts:0} proxy={t.total:0} roundtrip={rtt:0} firstAudio={rtt:0}" +
                      (reply.Error != null ? " proxyError=" + reply.Error : ""));
            State = VoiceState.Idle;
            if (reply.Intent == "ignore") return;
            string text = string.IsNullOrWhiteSpace(reply.Text) ? VoiceFallback.Reply(reply.Transcript, _sentState, speaker, _fallbackN++) : reply.Text;
            float[] samples = null;
            int rate = 0;
            if (reply.Audio != null && WavCodec.TryDecode(reply.Audio, out var s, out rate, out int ch) && ch == 1) samples = s;
            LastSpeaker = speaker;
            _table.Speak(speaker, text, samples, rate);
        }

        void Fallback(string reason, string transcript)
        {
            LastFallbackReason = reason;
            _requestId++; // 之後才到的回覆不播
            CancelStream();
            EndVoiceIfBegun();
            State = VoiceState.Idle;
            var speaker = _addressee != VoiceCharacter.None ? _addressee : VoiceCharacter.Dot;
            var text = VoiceFallback.Reply(transcript, _sentState, speaker, _fallbackN++);
            Debug.LogWarning($"[Voice] fallback ({reason}) speaker={VoiceCast.Id(speaker)} text-only");
            LastSpeaker = speaker;
            _table.Speak(speaker, text, null, 0);
        }
    }
}
