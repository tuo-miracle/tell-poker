using System;
using System.Collections.Generic;
using System.Text;
using Poker.AI;
using UnityEngine;

namespace Poker.Voice
{
    /// <summary>
    /// 中繼服務設定。Token 是 App 用的通行碼（不是 API 金鑰；金鑰只在中繼服務上），不寫進原始碼：
    /// 建置時由 tools/build-quest.sh 從 .secrets/voice-client.token 產生 Resources/VoiceClient.json（.gitignore）。
    /// </summary>
    [Serializable]
    public sealed class VoiceConfig
    {
        public const string PublicProxyUrl = "https://vr-poker-api.elebymax.com";
        public const string ClientResource = "VoiceClient";

        /// <summary>預設連公開的中繼（Cloudflare Tunnel）；本機開發可在 VoiceClient.json 改成 http://127.0.0.1:8787（tools/voice-dev.sh）。</summary>
        public string ProxyUrl = PublicProxyUrl;
        public string Token = "";
        /// <summary>這次安裝的隨機 id（VoiceDeviceId）：/talk 放 x-voice-device header、/live 放 ?device=，中繼用來分開限流。空字串＝不送（中繼改用 token 計）。</summary>
        public string DeviceId = "";

        /// <summary>開發版遙測（DevTelemetry）：VoiceClient.json 的 "telemetry": true（tools/build-quest.sh 預設開，TELL_RELEASE=1 關）。</summary>
        public bool Telemetry;

        [Serializable] sealed class ClientFile { public string url; public string token; public bool telemetry; }

        /// <summary>VoiceClient.json：{"url": "...", "token": "...", "telemetry": true}；壞掉或空的欄位保留預設。</summary>
        public static VoiceConfig FromClientJson(string json)
        {
            var c = new VoiceConfig();
            if (string.IsNullOrWhiteSpace(json)) return c;
            ClientFile f;
            try { f = JsonUtility.FromJson<ClientFile>(json); } catch { return c; }
            if (f == null) return c;
            if (!string.IsNullOrWhiteSpace(f.url)) c.ProxyUrl = f.url.Trim().TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(f.token)) c.Token = f.token.Trim();
            c.Telemetry = f.telemetry;
            return c;
        }

        /// <summary>讀 Resources/VoiceClient（建置時產生）；沒有就用預設（沒有通行碼：中繼會拒絕，改走離線備案）。裝置 id 存在 persistentDataPath。</summary>
        public static VoiceConfig Load() => Load(Application.persistentDataPath);

        public static VoiceConfig Load(string deviceIdDir)
        {
            var t = Resources.Load<TextAsset>(ClientResource);
            var c = FromClientJson(t != null ? t.text : null);
            Debug.Log($"[Voice] proxy {c.ProxyUrl} token {(string.IsNullOrEmpty(c.Token) ? "missing" : "set")}");
            c.DeviceId = VoiceDeviceId.LoadOrCreate(deviceIdDir);
            return c;
        }
        /// <summary>講完後等回話的上限，超過就改用離線備案。串流（/live）講完到 reply 約 0.5–0.8 s；中繼端 Live 失敗會自己改走整段（多 2–3 s），所以抓 5 s。</summary>
        public float TimeoutSeconds = 5f;
        /// <summary>邊講邊送到中繼的 /live（WebSocket）；false 或連不上時用整段的 /talk。</summary>
        public bool Streaming = true;
        /// <summary>串流回話先緩衝多少秒才開始播（避免一開始就斷斷續續）。</summary>
        public float PlaybackPrebufferSeconds = 0.08f;
        /// <summary>盯著角色多久就開始「在聽」（角色做出反應）。</summary>
        public float ListenDwell = 0.8f;
        /// <summary>沒盯著任何角色時不收音（避免把房間裡的對話送出去）。</summary>
        public bool RequireGaze = true;
        /// <summary>中繼回 rate_limited 後多久不連（秒）：期間直接用離線備案，不重送。每日上限（device_daily_cap／daily_cap）則停到 UTC 換日。</summary>
        public float RateLimitBackoffSeconds = 10f;
    }

    [Serializable]
    public sealed class VoiceTimings
    {
        public float gemini, facts, tts, total;
    }

    /// <summary>/live 的 done 訊息裡中繼端各階段毫秒（從玩家講完＝收到 end 算起；setup 從連線算起）。</summary>
    [Serializable]
    public sealed class LiveTimings
    {
        public float setup, audioIn, gemini, facts, ttsFirst, firstAudio, tts, total;
    }

    public enum LiveMessageKind { Reply, Done, Error }

    public sealed class LiveMessage
    {
        public LiveMessageKind Kind;
        public VoiceReply Reply;
        public int AudioRate;
        public string Path, Error;
        /// <summary>error 的代碼（rate_limited／device_daily_cap／daily_cap／bad_device；沒有＝null）。</summary>
        public string Code;
        public LiveTimings Timings;
    }

    public sealed class VoiceReply
    {
        public string Speaker, Transcript, Text, Intent, Emotion, Error;
        public byte[] Audio;
        public VoiceTimings Timings = new VoiceTimings();
    }

    /// <summary>/talk 的請求與回應格式（與 tools/voice-proxy/src/handler.ts 一致）。</summary>
    public static class VoiceProtocol
    {
        [Serializable]
        sealed class ReplyDto
        {
            public string speaker, transcript, text, intent, emotion, audio, error;
            public VoiceTimings timings;
        }

        [Serializable]
        sealed class LiveDto
        {
            public string type, speaker, transcript, text, intent, emotion, path, error, code;
            public int audioRate;
            public LiveTimings timings;
        }

        /// <summary>http(s)://host → ws(s)://host/live；有裝置 id 就加 ?device=（token 仍在 header）。</summary>
        public static string LiveUrl(string proxyUrl, string deviceId = null)
        {
            var u = (proxyUrl ?? "").TrimEnd('/');
            if (u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) u = "wss://" + u.Substring(8);
            else if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) u = "ws://" + u.Substring(7);
            u += "/live";
            return string.IsNullOrEmpty(deviceId) ? u : u + "?device=" + Uri.EscapeDataString(deviceId);
        }

        static readonly string[] LimitCodes = { "device_daily_cap", "daily_cap", "rate_limited" };

        /// <summary>錯誤字串裡的限流代碼（中繼要我們別再送）；不是限流回 null。device_daily_cap 先比，因為它包含 daily_cap。</summary>
        public static string LimitCode(string error)
        {
            if (string.IsNullOrEmpty(error)) return null;
            foreach (var c in LimitCodes) if (error.IndexOf(c, StringComparison.Ordinal) >= 0) return c;
            return null;
        }

        [Serializable] sealed class ErrorDto { public string error, code; }

        /// <summary>HTTP 錯誤回應裡的 code（沒有＝null）。</summary>
        public static string ErrorCode(string body)
        {
            if (string.IsNullOrWhiteSpace(body) || !body.TrimStart().StartsWith("{")) return null;
            try { var d = JsonUtility.FromJson<ErrorDto>(body); return string.IsNullOrEmpty(d?.code) ? null : d.code; }
            catch (Exception) { return null; }
        }

        /// <summary>/live 的第一個訊息（之後是二進位 PCM16 mono，取樣率 rate）。voicesJson：VoiceCast.VoicesJson（空的不送）。</summary>
        public static string BuildLiveStart(string addressee, string stateJson, string directivesJson, int rate, string voicesJson = null)
        {
            var sb = new StringBuilder(1024);
            sb.Append("{\"type\":\"start\",\"addressee\":");
            JsonText.Str(sb, addressee);
            sb.Append(",\"state\":").Append(string.IsNullOrEmpty(stateJson) ? "{}" : stateJson);
            sb.Append(",\"directives\":").Append(string.IsNullOrEmpty(directivesJson) ? "{}" : directivesJson);
            sb.Append(",\"rate\":").Append(rate);
            AppendVoices(sb, voicesJson);
            sb.Append('}');
            return sb.ToString();
        }

        public const string LiveEnd = "{\"type\":\"end\"}";
        public const string LiveCancel = "{\"type\":\"cancel\"}";

        public static bool TryParseLive(string json, out LiveMessage msg)
        {
            msg = null;
            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{")) return false;
            LiveDto d;
            try { d = JsonUtility.FromJson<LiveDto>(json); }
            catch (Exception) { return false; }
            if (d == null) return false;
            switch (d.type)
            {
                case "reply":
                    msg = new LiveMessage
                    {
                        Kind = LiveMessageKind.Reply, AudioRate = d.audioRate, Path = d.path ?? "",
                        Reply = new VoiceReply
                        {
                            Speaker = d.speaker ?? "", Transcript = d.transcript ?? "", Text = d.text ?? "",
                            Intent = string.IsNullOrEmpty(d.intent) ? "on_topic" : d.intent, Emotion = d.emotion ?? "",
                        },
                    };
                    return true;
                case "done":
                    msg = new LiveMessage { Kind = LiveMessageKind.Done, Timings = d.timings ?? new LiveTimings(), Error = string.IsNullOrEmpty(d.error) ? null : d.error };
                    return true;
                case "error":
                    msg = new LiveMessage { Kind = LiveMessageKind.Error, Error = string.IsNullOrEmpty(d.error) ? "error" : d.error, Code = string.IsNullOrEmpty(d.code) ? null : d.code };
                    return true;
                default:
                    return false;
            }
        }

        static void AppendVoices(StringBuilder sb, string voicesJson)
        {
            if (!string.IsNullOrEmpty(voicesJson) && voicesJson != "{}") sb.Append(",\"voices\":").Append(voicesJson);
        }

        public static string BuildRequest(byte[] wav, string addressee, string stateJson, string directivesJson, string voicesJson = null)
        {
            var sb = new StringBuilder(wav.Length * 4 / 3 + 1024);
            sb.Append("{\"audio\":\"").Append(Convert.ToBase64String(wav)).Append("\",\"addressee\":");
            JsonText.Str(sb, addressee);
            sb.Append(",\"state\":").Append(string.IsNullOrEmpty(stateJson) ? "{}" : stateJson);
            sb.Append(",\"directives\":").Append(string.IsNullOrEmpty(directivesJson) ? "{}" : directivesJson);
            AppendVoices(sb, voicesJson);
            sb.Append('}');
            return sb.ToString();
        }

        public static bool TryParseReply(string json, out VoiceReply reply)
        {
            reply = null;
            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{")) return false;
            ReplyDto d;
            try { d = JsonUtility.FromJson<ReplyDto>(json); }
            catch (Exception) { return false; }
            if (d == null) return false;
            byte[] audio = null;
            if (!string.IsNullOrEmpty(d.audio))
            {
                try { audio = Convert.FromBase64String(d.audio); }
                catch (FormatException) { audio = null; }
            }
            reply = new VoiceReply
            {
                Speaker = d.speaker ?? "", Transcript = d.transcript ?? "", Text = d.text ?? "", Intent = string.IsNullOrEmpty(d.intent) ? "on_topic" : d.intent,
                Emotion = d.emotion ?? "", Error = string.IsNullOrEmpty(d.error) ? null : d.error, Audio = audio, Timings = d.timings ?? new VoiceTimings(),
            };
            return true;
        }
    }

    /// <summary>每個 AI 角色「目前在演什麼」（來自 Poker.AI 的 PerformanceCue：情緒、破綻、強度）。這是演出指令，不是真實的牌力。</summary>
    public static class VoiceDirectives
    {
        public static string ToJson(IEnumerable<(VoiceCharacter who, PerformanceCue cue)> cues)
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var (who, cue) in cues)
            {
                if (cue == null || who == VoiceCharacter.None) continue;
                if (!first) sb.Append(',');
                first = false;
                JsonText.Str(sb, VoiceCast.Id(who));
                sb.Append(":{\"mood\":"); JsonText.Str(sb, cue.Mood.ToString());
                sb.Append(",\"tell\":"); JsonText.Str(sb, cue.Tell.ToString());
                sb.Append(",\"intensity\":").Append(JsonText.Num(cue.Intensity)).Append('}');
            }
            return sb.Append('}').ToString();
        }
    }
}
