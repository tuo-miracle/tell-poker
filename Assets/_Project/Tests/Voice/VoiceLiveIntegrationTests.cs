using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using Poker.Voice;
using UnityEngine;
using UnityEngine.TestTools;

namespace Poker.Tests.Voice
{
    /// <summary>
    /// 手動（Explicit，會花一點 API 額度）：真的 ClientWebSocket 連本機中繼的 /live，送一段 smoke/out/q/ 裡的問題音檔。
    /// 先開中繼：PORT=8788 node tools/voice-proxy/src/node-server.ts；再跑
    /// VOICE_LIVE_URL=http://127.0.0.1:8788 tools/run-unity-tests.sh PlayMode VoiceLiveIntegration
    /// </summary>
    [Explicit, Category("Manual")]
    public class VoiceLiveIntegrationTests
    {
        [UnityTest] public IEnumerator RealWebSocketGetsReplyAndStreamedAudio()
        {
            var url = Environment.GetEnvironmentVariable("VOICE_LIVE_URL");
            if (string.IsNullOrEmpty(url)) Assert.Ignore("VOICE_LIVE_URL not set");
            var dir = Path.Combine(Application.dataPath, "../tools/voice-proxy/smoke/out/q");
            var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.wav") : new string[0];
            if (files.Length == 0) Assert.Ignore("no question WAVs; run node smoke/bench-models.ts once");
            Assert.IsTrue(WavCodec.TryDecode(File.ReadAllBytes(files[0]), out var samples, out int rate, out _));

            var conn = new WebSocketStreamTransport().Open(VoiceProtocol.LiveUrl(url), "tell-dev");
            conn.SendText(VoiceProtocol.BuildLiveStart("dot", States.Flop().ToJson(), "{}", rate));
            var buf = new byte[3200];
            for (int i = 0; i < samples.Length; i += 1600)
            {
                int n = Math.Min(1600, samples.Length - i);
                var part = new float[n];
                Array.Copy(samples, i, part, 0, n);
                conn.SendBinary(buf, WavCodec.ToPcm16(part, n, buf));
            }
            conn.SendText(VoiceProtocol.LiveEnd);
            var sw = Stopwatch.StartNew();
            string reply = null, done = null, closed = null;
            long firstAudio = -1, bytes = 0;
            while (sw.ElapsedMilliseconds < 10000 && closed == null)
            {
                while (conn.TryReceive(out var e))
                {
                    if (e.Kind == VoiceStreamEventKind.Text) { if (e.Text.Contains("\"reply\"")) reply = e.Text; else done = e.Text; }
                    else if (e.Kind == VoiceStreamEventKind.Binary) { if (firstAudio < 0) firstAudio = sw.ElapsedMilliseconds; bytes += e.Data.Length; }
                    else closed = e.Error ?? "ok";
                }
                yield return null;
            }
            UnityEngine.Debug.Log($"[VoiceLive] reply={reply} firstAudio={firstAudio}ms bytes={bytes} done={done} closed={closed}");
            Assert.IsNotNull(reply, "reply");
            Assert.IsTrue(VoiceProtocol.TryParseLive(reply, out var m) && m.Kind == LiveMessageKind.Reply);
            Assert.IsFalse(string.IsNullOrEmpty(m.Reply.Text));
            Assert.Greater(bytes, 10000, "streamed audio");
            Assert.AreEqual("ok", closed);
        }
    }
}
