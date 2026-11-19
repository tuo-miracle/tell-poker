using UnityEditor;
using UnityEngine;

namespace Poker.Editor.Speech
{
    /// <summary>
    /// 預錄台詞（Resources/Speech/**.mp3，tools/voice-proxy/bake 產生）的匯入設定：單聲道、保留 22.05 kHz、Vorbis、
    /// DecompressOnLoad（SpeechBank 拼接數字句要用 AudioClip.GetData，壓縮在記憶體裡的音檔讀不到樣本）。
    /// </summary>
    sealed class SpeechClipImport : AssetPostprocessor
    {
        const string Folder = "Assets/_Project/Resources/Speech/";

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var imp = (AudioImporter)assetImporter;
            imp.forceToMono = true;
            imp.loadInBackground = false;
            var s = imp.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            s.preloadAudioData = true;
            imp.defaultSampleSettings = s;
            imp.ClearSampleSettingOverride("Android");
        }
    }
}
