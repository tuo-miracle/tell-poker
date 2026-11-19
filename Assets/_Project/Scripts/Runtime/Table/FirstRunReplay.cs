namespace Poker.Runtime
{
    /// <summary>
    /// 重拍用的開發屬性（只在寫實預覽版讀）：adb shell setprop debug.tell.firstrun 1 → 下次啟動當成第一次玩
    /// （教學從頭、Dot 自我介紹、不還原牌桌錨點、出雙手校準手印）。音量、Voice chat 同意等設定和存檔都不動。
    /// 一個值只生效一次（記在 PlayerPrefs）：要再來一次就換個值（2、3…），設 0 清掉。
    /// </summary>
    public static class FirstRunReplay
    {
        public const string Prop = "debug.tell.firstrun";

        /// <summary>這次啟動要不要當成第一次玩；consumed＝上次用掉的值，next＝之後要記住的值。</summary>
        public static bool Decide(string prop, string consumed, out string next)
        {
            var v = prop == null ? "" : prop.Trim();
            if (v.Length == 0 || v == "0") { next = ""; return false; }
            next = v;
            return v != (consumed ?? "");
        }

        const string ConsumedKey = "Dev.FirstRunReplay.consumed";
        static bool? _active;

        /// <summary>
        /// 這次啟動是不是重拍（一個 process 只判定一次，牌桌和牌局都問這裡）。只有 Android 實機、而且場景是寫實預覽版
        /// （isPreview，由呼叫端判斷）才讀屬性；其他情況一律 false。
        /// </summary>
        public static bool Active(bool isPreview)
        {
            if (_active.HasValue) return _active.Value;
            _active = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!isPreview) return false;
            string prop;
            using (var sp = new UnityEngine.AndroidJavaClass("android.os.SystemProperties")) prop = sp.CallStatic<string>("get", Prop, "");
            var consumed = UnityEngine.PlayerPrefs.GetString(ConsumedKey, "");
            _active = Decide(prop, consumed, out var next);
            if (next != consumed) { UnityEngine.PlayerPrefs.SetString(ConsumedKey, next); UnityEngine.PlayerPrefs.Save(); }
            UnityEngine.Debug.Log($"[FirstRun] {Prop}='{prop}' consumed='{consumed}' -> {(_active.Value ? "replay as first run" : "normal")}");
#endif
            return _active.Value;
        }
    }
}
