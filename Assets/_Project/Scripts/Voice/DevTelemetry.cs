using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace Poker.Voice
{
    /// <summary>
    /// 開發版遙測：只在 VoiceClient.json 有 "telemetry": true 時開（tools/build-quest.sh 預設開，TELL_RELEASE=1 關；Editor 永遠不開）。
    /// 收白名單 tag 的 Unity log＋錯誤／例外 → 記憶體緩衝（有上限，滿了丟最舊的）→ 每 10 s 一批 gzip NDJSON 送中繼 POST /telemetry；
    /// 暫停／失焦／離開時多送一次。離線就退避重試，絕不擋遊戲。不送音訊、不送玩家講的話、不送硬體識別碼。
    /// 格式見 tools/voice-proxy/src/telemetry.ts。
    /// </summary>
    public sealed class DevTelemetry : MonoBehaviour
    {
        static DevTelemetry _instance;

        TelemetryBuffer _buffer;
        TelemetryUploader _uploader;
        readonly Stopwatch _clock = Stopwatch.StartNew();
        long _seq;

        public static bool ShouldRun(VoiceConfig cfg, bool isEditor) =>
            !isEditor && cfg != null && cfg.Telemetry && !string.IsNullOrEmpty(cfg.Token);

        /// <summary>每次開 App 隨機一個（8 個 hex），和裝置、玩家都無關。</summary>
        public static string NewSessionId() => Guid.NewGuid().ToString("N").Substring(0, 8);

        /// <summary>版本字串 → header 安全的值（中繼只收 [A-Za-z0-9._+-]，最長 40）。</summary>
        public static string SafeBuild(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return "unknown";
            var s = Regex.Replace(v.Trim(), "[^A-Za-z0-9._+-]", "_");
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (_instance != null) return;
            var t = Resources.Load<TextAsset>(VoiceConfig.ClientResource);
            var cfg = VoiceConfig.FromClientJson(t != null ? t.text : null);
            if (!ShouldRun(cfg, Application.isEditor)) return;
            var go = new GameObject("DevTelemetry") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DevTelemetry>();
            _instance.Init(cfg, VoiceDeviceId.LoadOrCreate(Application.persistentDataPath));
        }

        void Init(VoiceConfig cfg, string deviceId)
        {
            var session = NewSessionId();
            _buffer = new TelemetryBuffer(TelemetryBuffer.DefaultMaxLines);
            _uploader = new TelemetryUploader(new UnityTelemetryTransport(), _buffer, cfg.ProxyUrl, cfg.Token, deviceId, session,
                SafeBuild(Application.version), () => _clock.Elapsed.TotalSeconds);
            Application.logMessageReceivedThreaded += OnLog;
            Debug.Log($"[Telemetry] on (session {session})");
        }

        void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            if (_instance == this) _instance = null;
        }

        /// <summary>任何執行緒都可能呼叫：只做過濾、格式化、放進緩衝（有鎖）；絕不丟例外。</summary>
        void OnLog(string condition, string stack, LogType type)
        {
            try
            {
                if (!TelemetryFilter.Accept(condition, type)) return;
                bool problem = type == LogType.Exception || type == LogType.Error || type == LogType.Assert;
                var line = TelemetryFilter.Format(DateTime.UtcNow, Interlocked.Increment(ref _seq), _clock.Elapsed.TotalSeconds, type,
                    TelemetryFilter.Scrub(condition), problem ? TelemetryFilter.Scrub(stack) : null);
                _buffer.Add(line);
            }
            catch (Exception) { /* 遙測壞掉也不能影響遊戲 */ }
        }

        void Update() => _uploader?.Tick();
        void OnApplicationPause(bool paused) { if (paused) _uploader?.Flush(); }
        void OnApplicationFocus(bool focused) { if (!focused) _uploader?.Flush(); }
        void OnApplicationQuit() => _uploader?.Flush();
    }

    /// <summary>白名單 tag、去識別、一行 JSON。</summary>
    public static class TelemetryFilter
    {
        /// <summary>每行上限（中繼收 4096）。</summary>
        public const int MaxLineChars = 4000;

        static readonly string[] Tags =
        {
            "[Playtest]", "[Gesture]", "[GestureEvent]", "[LiveTalk]", "[Read]", "[Tutorial]", "[Table]",
            "[Speech]", "[Assist]", "[Audio]", "[Voice]", "[FoolRocco]", "[UX]",
        };

        static readonly Regex GuidRe = new Regex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled);
        static readonly Regex Mac = new Regex(@"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b", RegexOptions.Compiled);
        // 玩家講的話（目前沒有任何 log 印逐字稿；這是保險）：transcript="…"、heard "…"、said: …
        static readonly Regex QuotedSpeech = new Regex(@"\b(transcript|heard|said|playerText)\b(?:\s*[=:]\s*|\s+)""[^""]*""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex BareSpeech = new Regex(@"\b(transcript|heard|said|playerText)\b\s*[=:]\s*(?!<scrubbed>)[^\r\n]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool Accept(string message, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) return true;
            if (string.IsNullOrEmpty(message) || message[0] != '[') return false;
            foreach (var t in Tags) if (message.StartsWith(t, StringComparison.Ordinal)) return true;
            return false;
        }

        public static string Scrub(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = GuidRe.Replace(s, "<id>");
            s = Mac.Replace(s, "<hw>");
            s = QuotedSpeech.Replace(s, "$1=<scrubbed>");
            s = BareSpeech.Replace(s, "$1=<scrubbed>");
            return s;
        }

        static string Level(LogType t)
        {
            switch (t)
            {
                case LogType.Warning: return "warn";
                case LogType.Error: return "error";
                case LogType.Exception: return "exception";
                case LogType.Assert: return "assert";
                default: return "log";
            }
        }

        /// <summary>{"t":UTC ISO,"s":序號,"up":開 App 後秒數,"l":層級,"m":訊息[,"st":堆疊]}；太長就截短到 MaxLineChars 內。</summary>
        public static string Format(DateTime utc, long seq, double uptime, LogType type, string message, string stack)
        {
            int keepMsg = 1500, keepStack = string.IsNullOrEmpty(stack) ? 0 : 1500;
            for (;;)
            {
                var sb = new StringBuilder(256);
                sb.Append("{\"t\":\"").Append(utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)).Append('"');
                sb.Append(",\"s\":").Append(seq.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"up\":").Append(uptime.ToString("0.000", CultureInfo.InvariantCulture));
                sb.Append(",\"l\":\"").Append(Level(type)).Append('"');
                sb.Append(",\"m\":"); JsonText.Str(sb, Cut(message ?? "", keepMsg));
                if (keepStack > 0) { sb.Append(",\"st\":"); JsonText.Str(sb, Cut(stack, keepStack)); }
                sb.Append('}');
                if (sb.Length <= MaxLineChars || (keepMsg <= 16 && keepStack == 0)) return sb.ToString();
                keepStack /= 2; if (keepStack < 64) keepStack = 0;
                keepMsg = Math.Max(16, keepMsg / 2);
            }
        }

        // NUL 拿掉（中繼整批拒收含 \u0000 的行）
        static string Cut(string s, int n)
        {
            if (s.IndexOf('\0') >= 0) s = s.Replace("\0", "");
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }
    }

    /// <summary>執行緒安全、有上限的行緩衝：滿了丟最舊的，下一批開頭補一行「丟了幾行」。</summary>
    public sealed class TelemetryBuffer
    {
        public const int DefaultMaxLines = 3000;

        readonly LinkedList<string> _lines = new LinkedList<string>();
        readonly int _max;
        int _dropped;

        public TelemetryBuffer(int maxLines) { _max = Math.Max(1, maxLines); }

        public int Count { get { lock (_lines) return _lines.Count; } }
        public int Dropped { get { lock (_lines) return _dropped; } }

        public void Add(string line)
        {
            lock (_lines)
            {
                _lines.AddLast(line);
                Trim();
            }
        }

        void Trim()
        {
            while (_lines.Count > _max) { _lines.RemoveFirst(); _dropped++; }
        }

        /// <summary>從最舊的開始拿，總位元組（含換行）不超過 maxBytes（至少拿一行）。</summary>
        public List<string> TakeBatch(int maxBytes)
        {
            var batch = new List<string>();
            lock (_lines)
            {
                int bytes = 0;
                if (_dropped > 0 && _lines.Count > 0)
                {
                    var meta = TelemetryFilter.Format(DateTime.UtcNow, -1, 0, LogType.Warning, $"[Telemetry] dropped {_dropped} lines (buffer full)", null);
                    batch.Add(meta);
                    bytes += Encoding.UTF8.GetByteCount(meta) + 1;
                    _dropped = 0;
                }
                while (_lines.First != null)
                {
                    var l = _lines.First.Value;
                    int n = Encoding.UTF8.GetByteCount(l) + 1;
                    if (batch.Count > 0 && bytes + n > maxBytes) break;
                    batch.Add(l);
                    bytes += n;
                    _lines.RemoveFirst();
                }
            }
            return batch;
        }

        /// <summary>送不出去：放回最前面（順序不變），超過上限照樣丟最舊的。</summary>
        public void Requeue(List<string> batch)
        {
            if (batch == null) return;
            lock (_lines)
            {
                for (int i = batch.Count - 1; i >= 0; i--) _lines.AddFirst(batch[i]);
                Trim();
            }
        }
    }

    /// <summary>遙測的 HTTP 傳輸。done(status, body)：status 0＝連線失敗；在主執行緒呼叫。</summary>
    public interface ITelemetryTransport
    {
        void Post(string url, IList<KeyValuePair<string, string>> headers, byte[] body, Action<int, string> done);
    }

    /// <summary>批次上傳：每 10 s 一批（落後時最快 5 s 一批）、一次只有一個請求、離線指數退避（最多 5 分鐘）。主執行緒呼叫。</summary>
    public sealed class TelemetryUploader
    {
        public const double IntervalSeconds = 10, MinGapSeconds = 5, FlushGapSeconds = 2, MaxBackoffSeconds = 300;
        public const int MaxBatchBytes = 64 * 1024;

        readonly ITelemetryTransport _transport;
        readonly TelemetryBuffer _buffer;
        readonly string _url, _token, _device, _session, _build;
        readonly Func<double> _clock;

        double _nextAt, _lastSendAt = double.NegativeInfinity;
        int _failures;
        bool _inFlight;

        /// <summary>gzip（壓不了或中繼不收就改純文字）。</summary>
        public bool Gzip = true;
        /// <summary>中繼說這個 session 滿了或通行碼錯：這次開 App 不再送。</summary>
        public bool Stopped { get; private set; }
        public double BackoffSeconds { get; private set; }

        public TelemetryUploader(ITelemetryTransport transport, TelemetryBuffer buffer, string proxyUrl, string token, string device, string session, string build, Func<double> clock)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _url = (proxyUrl ?? "").TrimEnd('/') + "/telemetry";
            _token = token ?? "";
            _device = device ?? "";
            _session = session;
            _build = build;
            _clock = clock;
            _nextAt = clock() + IntervalSeconds;
        }

        public void Tick()
        {
            if (Stopped || _inFlight) return;
            var now = _clock();
            if (now < _nextAt) return;
            Send(now);
        }

        /// <summary>暫停／離開：馬上送（退避中、送出中或 2 s 內剛送過就不送）。</summary>
        public void Flush()
        {
            if (Stopped || _inFlight || _failures > 0) return;
            var now = _clock();
            if (now - _lastSendAt < FlushGapSeconds) return;
            Send(now);
        }

        void Send(double now)
        {
            var batch = _buffer.TakeBatch(MaxBatchBytes);
            if (batch.Count == 0) { _nextAt = now + IntervalSeconds; return; }
            bool more = _buffer.Count > 0;
            var raw = Encoding.UTF8.GetBytes(string.Join("\n", batch) + "\n");
            bool gz = Gzip;
            var body = raw;
            if (gz)
            {
                try { body = GzipBytes(raw); }
                catch (Exception) { Gzip = gz = false; body = raw; }
            }
            var headers = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("x-voice-token", _token),
                new KeyValuePair<string, string>("x-voice-device", _device),
                new KeyValuePair<string, string>("x-tell-session", _session),
                new KeyValuePair<string, string>("x-tell-build", _build),
                new KeyValuePair<string, string>("content-type", "application/x-ndjson"),
            };
            if (gz) headers.Add(new KeyValuePair<string, string>("content-encoding", "gzip"));
            _inFlight = true;
            _lastSendAt = now;
            try { _transport.Post(_url, headers, body, (status, resp) => OnDone(batch, gz, more, status, resp)); }
            catch (Exception e)
            {
                if (gz) Gzip = false; // 例如平台不讓設 content-encoding：之後改純文字
                OnDone(batch, gz, more, 0, e.Message);
            }
        }

        void OnDone(List<string> batch, bool gz, bool more, int status, string body)
        {
            _inFlight = false;
            var now = _clock();
            if (status >= 200 && status < 300)
            {
                _failures = 0;
                BackoffSeconds = 0;
                _nextAt = now + (more ? MinGapSeconds : IntervalSeconds);
                return;
            }
            var code = VoiceProtocol.ErrorCode(body);
            if (status == 401 || status == 403 || (status == 413 && code == "session_full"))
            {
                Stopped = true;
                Debug.LogWarning($"[Telemetry] stopped (http {status}{(code != null ? " " + code : "")})");
                return;
            }
            if (status == 400 || status == 413)
            {
                if (status == 400 && gz) { Gzip = false; _buffer.Requeue(batch); } // 中繼解不開 gzip：改純文字再試一次
                else Debug.LogWarning($"[Telemetry] batch of {batch.Count} lines refused (http {status}{(code != null ? " " + code : "")}); dropped");
                _nextAt = now + IntervalSeconds;
                return;
            }
            // 離線、429、5xx、507：留著重試，指數退避
            _buffer.Requeue(batch);
            _failures++;
            BackoffSeconds = Math.Min(MaxBackoffSeconds, IntervalSeconds * Math.Pow(2, Math.Min(_failures, 10)));
            _nextAt = now + BackoffSeconds;
        }

        static byte[] GzipBytes(byte[] raw)
        {
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionMode.Compress, true)) gz.Write(raw, 0, raw.Length);
                return ms.ToArray();
            }
        }
    }

    /// <summary>實機用：UnityWebRequest POST（完成回呼在主執行緒）。</summary>
    public sealed class UnityTelemetryTransport : ITelemetryTransport
    {
        public void Post(string url, IList<KeyValuePair<string, string>> headers, byte[] body, Action<int, string> done)
        {
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 15,
            };
            foreach (var h in headers) req.SetRequestHeader(h.Key, h.Value);
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                bool net = req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.DataProcessingError;
                int status = net ? 0 : (int)req.responseCode;
                string text = net ? req.error : req.downloadHandler?.text;
                req.Dispose();
                done(status, text);
            };
        }
    }
}
