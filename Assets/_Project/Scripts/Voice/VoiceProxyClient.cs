using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;

namespace Poker.Voice
{
    public sealed class VoiceHttpResult
    {
        public bool Ok;
        public int Status;
        public string Body;
        /// <summary>連線層錯誤（連不上、逾時）；有 HTTP 狀態碼時為 null。</summary>
        public string Error;
    }

    /// <summary>HTTP 傳輸（實機 UnityWebRequest；測試用假的）。done 在主執行緒呼叫。</summary>
    public interface IVoiceTransport
    {
        void Post(string url, string token, string device, string json, int timeoutSeconds, Action<VoiceHttpResult> done);
    }

    public static class VoiceHttp
    {
        /// <summary>/talk 的 header：x-voice-token（通行碼）、x-voice-device（安裝 id，有才送）。</summary>
        public static IEnumerable<(string, string)> Headers(string token, string device)
        {
            yield return ("x-voice-token", token ?? "");
            if (!string.IsNullOrEmpty(device)) yield return ("x-voice-device", device);
        }
    }

    /// <summary>送一段語音到中繼服務的 /talk，解析回話。錯誤以字串回報（呼叫端改用備案）。</summary>
    public sealed class VoiceProxyClient
    {
        readonly IVoiceTransport _transport;
        readonly VoiceConfig _cfg;

        public VoiceProxyClient(IVoiceTransport transport, VoiceConfig cfg)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _cfg = cfg ?? new VoiceConfig();
        }

        public string TalkUrl => (_cfg.ProxyUrl ?? "").TrimEnd('/') + "/talk";

        public void Talk(byte[] wav, string addressee, string stateJson, string directivesJson, Action<VoiceReply, string> done, string voicesJson = null)
        {
            var json = VoiceProtocol.BuildRequest(wav, addressee, stateJson, directivesJson, voicesJson);
            int timeout = Math.Max(1, (int)Math.Ceiling(_cfg.TimeoutSeconds) + 1); // 傳輸層的保險；真正的逾時由 VoiceSession 計時
            _transport.Post(TalkUrl, _cfg.Token, _cfg.DeviceId, json, timeout, r =>
            {
                if (r == null) { done(null, "no response"); return; }
                if (!r.Ok)
                {
                    var code = VoiceProtocol.ErrorCode(r.Body);
                    done(null, r.Status > 0 ? $"http {r.Status}{(code != null ? " " + code : "")}: {Trim(r.Body)}" : "network: " + (r.Error ?? "unknown"));
                    return;
                }
                if (!VoiceProtocol.TryParseReply(r.Body, out var reply)) { done(null, "parse: reply is not JSON"); return; }
                done(reply, null);
            });
        }

        static string Trim(string s) => string.IsNullOrEmpty(s) ? "" : s.Length > 160 ? s.Substring(0, 160) : s;
    }

    /// <summary>實機用：UnityWebRequest POST JSON（完成回呼在主執行緒）。</summary>
    public sealed class UnityWebRequestTransport : IVoiceTransport
    {
        public void Post(string url, string token, string device, string json, int timeoutSeconds, Action<VoiceHttpResult> done)
        {
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = timeoutSeconds,
            };
            req.SetRequestHeader("Content-Type", "application/json");
            foreach (var (k, v) in VoiceHttp.Headers(token, device)) req.SetRequestHeader(k, v);
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                var r = new VoiceHttpResult { Status = (int)req.responseCode, Body = req.downloadHandler?.text };
                r.Ok = req.result == UnityWebRequest.Result.Success && r.Status >= 200 && r.Status < 300;
                if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.DataProcessingError) { r.Error = req.error; r.Status = 0; }
                req.Dispose();
                done(r);
            };
        }
    }
}
