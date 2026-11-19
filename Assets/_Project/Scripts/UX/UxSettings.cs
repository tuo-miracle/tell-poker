using System;

namespace Poker.UX
{
    public enum Handedness { Auto, Left, Right }

    /// <summary>無障礙與操作設定（存成 JSON，見 SettingsStore）。</summary>
    [Serializable]
    public sealed class UxSettings
    {
        public const float MinSensitivity = 0.5f, MaxSensitivity = 1.5f;
        public const float MinSubtitleScale = 0.75f, MaxSubtitleScale = 2f;

        /// <summary>慣用手：決定手腕按鈕在哪一手（非慣用手）。選單只有「Left-handed」（On＝Left、Off＝Auto）；舊存檔的 Right 照用、顯示 Off。</summary>
        public Handedness Handedness = Handedness.Auto;
        /// <summary>手勢靈敏度倍率：&gt; 1 門檻變低（較容易觸發）。2026-10-15 起不在玩家選單，讀檔一律回到 1（Migrate）；只留給程式（開發）調。</summary>
        public float GestureSensitivity = 1f;
        public bool HighContrast;
        /// <summary>文字大小：每個角色頭上的字幕泡泡（每一句話都有）、籌碼金額標籤、示範手字幕（選單顯示「Text size」；欄位名保留以相容舊存檔）。</summary>
        public float SubtitleScale = 1f;
        /// <summary>手部追蹤 Fast Motion Mode（約 60 Hz）；實機 A/B 用，預設關。2026-10-15 起不在玩家選單，照存檔值套用。</summary>
        public bool FastMotion;
        /// <summary>Settings 裡的 Voice chat 開關（預設關）。</summary>
        public bool VoiceChat;
        /// <summary>玩家在同意畫面按過同意（語音會送到 Google Gemini 與 ElevenLabs）。沒同意之前語音一律關閉。</summary>
        public bool VoiceConsent;
        /// <summary>牌局中的手勢提示（輪到你發呆 5 秒就示範合法動作）。預設開；舊存檔沒有這個欄位也是開。選單的「Hints」同時管這個與 HandHints（SetHints）。</summary>
        public bool Hints = true;
        /// <summary>看牌時顯示牌力提示（Strong／Decent／Weak／Drawing，2026-10-14）。預設開；舊存檔沒有這個欄位也是開。</summary>
        public bool HandHints = true;
        /// <summary>掌心朝上叫出的選單按鈕已經用過一次（之後按鈕上不再附「Touch with your other hand」提示；2026-10-14）。</summary>
        public bool PalmMenuHintSeen;

        /// <summary>語音對話實際開啟：同意過而且開關打開。</summary>
        public bool VoiceActive => VoiceConsent && VoiceChat;

        /// <summary>選單「Left-handed: On」。</summary>
        public bool LeftHanded => Handedness == Handedness.Left;

        /// <summary>選單「Hints」：手勢提示與牌力提示一起開關（兩個欄位都留著，舊版讀得懂存檔）。</summary>
        public void SetHints(bool on) { Hints = on; HandHints = on; }

        /// <summary>選單「Text size」的循環。</summary>
        public static readonly float[] TextSizes = { 1f, 1.25f, 1.5f, 0.85f };

        /// <summary>Text size 的下一個值：1.0 → 1.25 → 1.5 → 0.85 → 1.0；不在循環裡的舊值（例如以前 −／+ 調出來的 1.75）回到 1.0。</summary>
        public static float NextTextSize(float current)
        {
            for (int i = 0; i < TextSizes.Length; i++)
                if (Math.Abs(current - TextSizes[i]) < 0.01f) return TextSizes[(i + 1) % TextSizes.Length];
            return 1f;
        }

        /// <summary>
        /// 讀檔後的遷移（2026-10-15 選單瘦身；每次讀檔都做，重複做結果一樣）：
        /// 提示合併成一個開關 → 兩個都開才算開，只要關了其中一個就兩個都關（尊重玩家關掉的那個）；
        /// 靈敏度不在玩家選單了 → 回到 1（舊的開發值不會悄悄留著又沒地方改）；Fast motion、Text size、慣用手照存檔。
        /// </summary>
        public UxSettings Migrate()
        {
            SetHints(Hints && HandHints);
            GestureSensitivity = 1f;
            return this;
        }

        /// <summary>把讀進來的值拉回合法範圍（壞值退回預設）。</summary>
        public UxSettings Sanitize()
        {
            if (!Enum.IsDefined(typeof(Handedness), Handedness)) Handedness = Handedness.Auto;
            GestureSensitivity = Clamp(GestureSensitivity, MinSensitivity, MaxSensitivity, 1f);
            SubtitleScale = Clamp(SubtitleScale, MinSubtitleScale, MaxSubtitleScale, 1f);
            if (!VoiceConsent) VoiceChat = false;
            return this;
        }

        public UxSettings Clone() => (UxSettings)MemberwiseClone();

        static float Clamp(float v, float min, float max, float fallback)
            => float.IsNaN(v) || float.IsInfinity(v) ? fallback : Math.Min(max, Math.Max(min, v));
    }
}
