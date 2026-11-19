using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Poker.Voice
{
    /// <summary>
    /// 每次安裝隨機產生一次的裝置 id（GUID v4），存在 persistentDataPath，只給中繼分開限流用。
    /// 不是硬體識別碼、不含個資；重裝 App（或清除資料）就換一個。log 只印前 4 碼。
    /// </summary>
    public static class VoiceDeviceId
    {
        public const string FileName = "voice-device-id";
        static readonly Regex Format = new Regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");
        static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();

        public static bool IsValid(string id) => !string.IsNullOrEmpty(id) && Format.IsMatch(id);

        /// <summary>讀 dir 裡的 id；沒有或壞掉就產生新的並存檔。存不了檔也照樣回傳（這次執行內不變）。同一個 dir 只在第一次印 log。</summary>
        public static string LoadOrCreate(string dir)
        {
            var key = dir ?? "";
            if (Cache.TryGetValue(key, out var cached)) return cached;
            string id = null;
            var path = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, FileName);
            try { if (path != null && File.Exists(path)) id = File.ReadAllText(path).Trim().ToLowerInvariant(); }
            catch (Exception) { id = null; }
            if (!IsValid(id))
            {
                id = Guid.NewGuid().ToString("D");
                try { if (path != null) { Directory.CreateDirectory(dir); File.WriteAllText(path, id); } }
                catch (Exception e) { Debug.LogWarning("[Voice] device id not saved: " + e.GetType().Name); }
            }
            Cache[key] = id;
            Debug.Log($"[Voice] device id set ({id.Substring(0, 4)}…)");
            return id;
        }

        /// <summary>測試用：模擬 App 重開（下次 LoadOrCreate 重新讀檔）。</summary>
        public static void ResetCacheForTests() => Cache.Clear();
    }
}
