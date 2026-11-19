using System;
using System.IO;
using UnityEngine;

namespace Poker.UX
{
    /// <summary>設定的 JSON 存讀。讀不到或壞掉時回預設值，並把壞檔改名成 .corrupt 留著查。</summary>
    public static class SettingsStore
    {
        public const string FileName = "ux-settings.json";
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        public static UxSettings Load(string path)
        {
            if (!File.Exists(path)) return new UxSettings();
            try
            {
                var text = File.ReadAllText(path);
                var s = string.IsNullOrWhiteSpace(text) ? null : JsonUtility.FromJson<UxSettings>(text);
                if (s == null || !text.TrimStart().StartsWith("{")) throw new FormatException("not a JSON object");
                return s.Sanitize().Migrate();
            }
            catch (IOException e)
            {
                // 暫時讀不到（例如正在寫）不算損毀：用預設值，不動檔案
                Debug.LogWarning($"[SettingsStore] 讀不到設定檔，暫用預設值：{e.Message}");
                return new UxSettings();
            }
            catch (UnauthorizedAccessException e)
            {
                Debug.LogWarning($"[SettingsStore] 沒有權限讀設定檔，暫用預設值：{e.Message}");
                return new UxSettings();
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException)
            {
                Debug.LogWarning($"[SettingsStore] 設定檔損毀，改用預設值：{e.Message}");
                SetAside(path);
                return new UxSettings();
            }
        }

        public static void Save(UxSettings s, string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(s, true));
            // 先寫暫存再整檔替換，寫到一半被中斷不會留下半個檔或沒有檔
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        static void SetAside(string path)
        {
            try
            {
                var bad = path + ".corrupt";
                if (File.Exists(bad)) File.Delete(bad);
                File.Move(path, bad);
            }
            catch (Exception e) { Debug.LogWarning($"[SettingsStore] 無法移開壞檔：{e.Message}"); }
        }
    }
}
