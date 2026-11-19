using System;
using System.Collections.Generic;
using System.Text;
using Poker.AI;
using UnityEngine;

namespace Poker.Characters
{
    /// <summary>manifest.json 的一行（tools/voice-proxy/bake 寫入；欄位名要和腳本一致）。</summary>
    [Serializable]
    public sealed class SpeechManifestLine
    {
        public string id, voice, voiceId, model, text, file, hash;
        public int bytes;
    }

    [Serializable]
    public sealed class SpeechManifest
    {
        public string model, format;
        public SpeechManifestLine[] lines;
    }

    /// <summary>一句話的聲音：預錄音檔，或用片段拼出來的新 AudioClip（OwnsClip：播完要釋放）。</summary>
    public sealed class SpeechAudio
    {
        public string Id;
        public AudioClip Clip;
        public bool OwnsClip;
    }

    /// <summary>
    /// 預錄台詞庫：Resources/Speech/manifest.json ＋ 每句一個 mp3（22.05 kHz 單聲道，Android 上是 Vorbis）。
    /// 用「聲音＋原文」找音檔；找不到原文而且句子有數字時，拆成片段（SpeechText.Pieces）逐一找、去掉頭尾靜音後接起來。
    /// </summary>
    public static class SpeechBank
    {
        public const string ManifestResource = "Speech/manifest";
        /// <summary>片段之間的停頓（秒）。</summary>
        public const float PieceGap = 0.06f;
        const float SilenceThreshold = 0.015f;
        const float KeepPad = 0.03f;

        static Dictionary<string, SpeechManifestLine> _exact, _keyed;
        static readonly HashSet<string> _voices = new HashSet<string>();
        static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();

        public static int Count { get { Ensure(); return _exact.Count; } }

        public static void Reload()
        {
            _exact = null;
            _keyed = null;
            _voices.Clear();
            _clips.Clear();
            Ensure();
        }

        static void Ensure()
        {
            if (_exact != null) return;
            _exact = new Dictionary<string, SpeechManifestLine>();
            _keyed = new Dictionary<string, SpeechManifestLine>();
            var asset = Resources.Load<TextAsset>(ManifestResource);
            if (asset == null) { Debug.LogWarning("[Speech] no manifest at Resources/" + ManifestResource + " (run tools/voice-proxy bake)"); return; }
            var m = JsonUtility.FromJson<SpeechManifest>(asset.text);
            if (m?.lines == null) return;
            foreach (var l in m.lines)
            {
                if (l == null || string.IsNullOrEmpty(l.voice) || l.text == null) continue;
                _exact[l.voice + "\n" + l.text] = l;
                _voices.Add(l.voice);
                var k = l.voice + "\n" + SpeechText.Key(l.text);
                if (!_keyed.ContainsKey(k)) _keyed[k] = l;
            }
        }

        /// <summary>這個聲音錄過任何一句嗎（新座位／新角色在 bake 之前是 false）。</summary>
        public static bool HasVoice(string voice)
        {
            Ensure();
            return !string.IsNullOrEmpty(voice) && _voices.Contains(voice);
        }

        public static bool TryGetEntry(string voice, string text, out SpeechManifestLine e)
        {
            Ensure();
            return _exact.TryGetValue(voice + "\n" + text, out e);
        }

        static bool TryPiece(string voice, string piece, out SpeechManifestLine e)
        {
            if (_exact.TryGetValue(voice + "\n" + piece, out e)) return true;
            return _keyed.TryGetValue(voice + "\n" + SpeechText.Key(piece), out e);
        }

        /// <summary>只看清單（不載入音檔）：這個聲音說得出這句嗎？說不出時 why 寫缺了哪一段。</summary>
        public static bool CanSay(string voice, string text, out string why)
        {
            Ensure();
            why = null;
            if (string.IsNullOrEmpty(text)) { why = "empty"; return false; }
            if (_exact.ContainsKey(voice + "\n" + text)) return true;
            var pieces = SpeechText.Pieces(text);
            if (pieces.Count == 0) { why = "no words"; return false; }
            if (pieces.Count == 1 && !SpeechText.HasNumber(text)) { why = "no baked line"; return false; }
            foreach (var p in pieces)
                if (!TryPiece(voice, p, out _)) { why = "missing piece \"" + p + "\""; return false; }
            return true;
        }

        static AudioClip Load(SpeechManifestLine e)
        {
            if (_clips.TryGetValue(e.file, out var c) && c != null) return c;
            c = Resources.Load<AudioClip>(e.file);
            if (c != null) _clips[e.file] = c;
            else Debug.LogWarning("[Speech] missing clip Resources/" + e.file + " for \"" + e.text + "\"");
            return c;
        }

        /// <summary>找這句話的聲音；完全找不到回傳 null。</summary>
        public static SpeechAudio Resolve(string voice, string text)
        {
            Ensure();
            if (string.IsNullOrEmpty(voice) || string.IsNullOrEmpty(text)) return null;
            if (_exact.TryGetValue(voice + "\n" + text, out var e))
            {
                var c = Load(e);
                return c != null ? new SpeechAudio { Id = e.id, Clip = c } : null;
            }
            if (!CanSay(voice, text, out _)) return null;
            var parts = new List<AudioClip>();
            var ids = new StringBuilder();
            foreach (var p in SpeechText.Pieces(text))
            {
                TryPiece(voice, p, out var pe);
                var c = Load(pe);
                if (c == null) return null;
                parts.Add(c);
                if (ids.Length > 0) ids.Append('+');
                ids.Append(pe.id);
            }
            var joined = Join(parts, "speech_" + voice + "_joined");
            return joined != null ? new SpeechAudio { Id = ids.ToString(), Clip = joined, OwnsClip = true } : null;
        }

        /// <summary>把片段去頭尾靜音後接起來（取樣率用第一段的；片段都是 22.05 kHz 單聲道）。</summary>
        static AudioClip Join(List<AudioClip> parts, string name)
        {
            int rate = parts[0].frequency;
            int gap = Mathf.RoundToInt(PieceGap * rate), pad = Mathf.RoundToInt(KeepPad * rate);
            var all = new List<float>();
            for (int i = 0; i < parts.Count; i++)
            {
                var c = parts[i];
                if (c.channels != 1 || c.frequency != rate || c.samples <= 0) { Debug.LogWarning("[Speech] cannot join clip " + c.name); return null; }
                if (c.loadState != AudioDataLoadState.Loaded) c.LoadAudioData();
                var s = new float[c.samples];
                if (!c.GetData(s, 0)) { Debug.LogWarning("[Speech] GetData failed for " + c.name + " (load type must be DecompressOnLoad)"); return null; }
                int a = 0, b = s.Length - 1;
                while (a < s.Length && Mathf.Abs(s[a]) < SilenceThreshold) a++;
                while (b > a && Mathf.Abs(s[b]) < SilenceThreshold) b--;
                a = Mathf.Max(0, a - pad);
                b = Mathf.Min(s.Length - 1, b + pad);
                if (i > 0) for (int k = 0; k < gap; k++) all.Add(0f);
                for (int k = a; k <= b; k++) all.Add(s[k]);
            }
            if (all.Count == 0) return null;
            var clip = AudioClip.Create(name, all.Count, 1, rate, false);
            clip.SetData(all.ToArray(), 0);
            return clip;
        }
    }
}
