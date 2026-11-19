using System;
using System.Collections.Generic;
using Poker.Game;

namespace Poker.UX
{
    /// <summary>選單項目的實際動作（實機接 GameController／TablePlacer／HandSurfaceCalibration；測試用假的）。</summary>
    public interface IMenuActions
    {
        void NewGame(); void Quit(); void ReplayTutorial();
        /// <summary>「Move table here」：把桌子叫到面前（同系統重新置中，RecenterPlan），再顯示雙手手印。</summary>
        void MoveTableHere();
        /// <summary>教學正在進行（主頁顯示「Skip tutorial」）。</summary>
        bool TutorialActive { get; }
        void SkipTutorial();
        /// <summary>可以開始「Fool Rocco」情境：只有開發工具場景（TELL_DEV_TOOLS=1）而且牌局允許時（2026-10-15 起不在玩家版選單）。</summary>
        bool FoolRoccoAvailable { get; }
        void StartFoolRocco();
    }

    public enum MenuPage { Main, Settings, VoiceConsent, HowToPlay }

    /// <summary>
    /// 選單項目（2026-10-15 選單瘦身：拿掉 Fool Rocco（只剩開發工具場景）、靈敏度 −／+、Fast motion、Roll peek／Pick up／Chips 示範，
    /// Hand hints 併進 Hints、Text size −／+ 改成一顆循環按鈕、Dominant hand 改成 Left-handed）。
    /// </summary>
    public enum MenuItem
    {
        Resume, HowToPlay, MoveTable, Settings, NewGame, Quit, SkipTutorial, FoolRocco,
        GhostPeek, GhostBet, GhostKnock, GhostFold, GhostAllIn, GhostFlip, GhostTalk, ReplayTutorial, Back,
        VoiceChat, Hints, TextSize, HighContrast, LeftHanded,
        VoiceAccept, VoiceDecline,
    }

    /// <summary>
    /// 手腕選單的狀態機（不含畫面）：開關、分頁、項目動作；開著時掛上 PauseSource.Menu。
    /// </summary>
    public sealed class MenuModel
    {
        static readonly MenuItem[] MainItems = { MenuItem.Resume, MenuItem.HowToPlay, MenuItem.MoveTable, MenuItem.Settings, MenuItem.NewGame, MenuItem.Quit };
        /// <summary>教學進行中：多一個「Skip tutorial」，和 Resume 同一列（教學只能從這裡跳過）。</summary>
        static readonly MenuItem[] MainItemsInTutorial = { MenuItem.Resume, MenuItem.SkipTutorial, MenuItem.HowToPlay, MenuItem.MoveTable, MenuItem.Settings, MenuItem.NewGame, MenuItem.Quit };
        /// <summary>開發工具場景、可以開始 Fool Rocco 時：放在 New game 後面。</summary>
        static readonly MenuItem[] MainItemsWithFoolRocco = { MenuItem.Resume, MenuItem.HowToPlay, MenuItem.MoveTable, MenuItem.Settings, MenuItem.NewGame, MenuItem.FoolRocco, MenuItem.Quit };
        /// <summary>How to play：每個玩家動作一個示範（順序同 GhostCatalog.All），再來是重看教學、返回。</summary>
        static readonly MenuItem[] HowToPlayItems =
        {
            MenuItem.GhostPeek, MenuItem.GhostBet, MenuItem.GhostKnock, MenuItem.GhostFold, MenuItem.GhostAllIn, MenuItem.GhostFlip, MenuItem.GhostTalk,
            MenuItem.ReplayTutorial, MenuItem.Back,
        };
        static readonly MenuItem[] SettingsItems =
        {
            MenuItem.VoiceChat, MenuItem.Hints, MenuItem.TextSize, MenuItem.HighContrast, MenuItem.LeftHanded, MenuItem.Back,
        };
        /// <summary>首次開啟語音對話的同意畫面。</summary>
        static readonly MenuItem[] VoiceConsentItems = { MenuItem.VoiceAccept, MenuItem.VoiceDecline };

        readonly IMenuActions _actions;
        readonly PauseState _pause;

        public UxSettings Settings { get; }
        public bool IsOpen { get; private set; }
        /// <summary>How to play 頁選中、正在示範的手勢（離開這一頁或關選單就清掉）。</summary>
        public GhostGesture? Demo { get; private set; }

        /// <summary>How to play 頁的項目 → 示範的手勢（Check＝敲桌、Show cards＝攤牌翻牌）；不是示範項目 → null。</summary>
        public static GhostGesture? GhostOf(MenuItem item)
        {
            switch (item)
            {
                case MenuItem.GhostPeek: return GhostGesture.Peek;
                case MenuItem.GhostBet: return GhostGesture.Bet;
                case MenuItem.GhostKnock: return GhostGesture.Knock;
                case MenuItem.GhostFold: return GhostGesture.Fold;
                case MenuItem.GhostAllIn: return GhostGesture.AllIn;
                case MenuItem.GhostFlip: return GhostGesture.Flip;
                case MenuItem.GhostTalk: return GhostGesture.Talk;
                default: return null;
            }
        }
        public MenuPage Page { get; private set; }

        /// <summary>開關或換頁時觸發（畫面重建用）。</summary>
        public event Action Changed;
        /// <summary>設定被改動（每按一次就觸發，用來存檔與套用）。</summary>
        public event Action<UxSettings> SettingsChanged;

        public MenuModel(IMenuActions actions, PauseState pause, UxSettings settings)
        {
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
            _pause = pause ?? throw new ArgumentNullException(nameof(pause));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public IReadOnlyList<MenuItem> Items(MenuPage page)
        {
            switch (page)
            {
                case MenuPage.Main: return _actions.TutorialActive ? MainItemsInTutorial : _actions.FoolRoccoAvailable ? MainItemsWithFoolRocco : MainItems;
                case MenuPage.VoiceConsent: return VoiceConsentItems;
                case MenuPage.HowToPlay: return HowToPlayItems;
                default: return SettingsItems;
            }
        }

        public void Open()
        {
            IsOpen = true;
            Page = MenuPage.Main;
            Demo = null;
            _pause.Set(PauseSource.Menu, true);
            Changed?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Page = MenuPage.Main;
            Demo = null;
            _pause.Set(PauseSource.Menu, false);
            Changed?.Invoke();
        }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        public void Press(MenuItem item)
        {
            if (!IsOpen || Array.IndexOf((MenuItem[])Items(Page), item) < 0) return;
            var ghost = GhostOf(item);
            if (ghost.HasValue) { Demo = ghost; Changed?.Invoke(); return; }
            switch (item)
            {
                case MenuItem.Resume: Close(); break;
                case MenuItem.NewGame: Close(); _actions.NewGame(); break;
                case MenuItem.MoveTable: Close(); _actions.MoveTableHere(); break;
                case MenuItem.Quit: Close(); _actions.Quit(); break;
                case MenuItem.Settings: Page = MenuPage.Settings; Changed?.Invoke(); break;
                case MenuItem.HowToPlay: Page = MenuPage.HowToPlay; Demo = null; Changed?.Invoke(); break;
                case MenuItem.Back: Page = MenuPage.Main; Demo = null; Changed?.Invoke(); break;
                case MenuItem.ReplayTutorial: Close(); _actions.ReplayTutorial(); break;
                case MenuItem.SkipTutorial: Close(); _actions.SkipTutorial(); break;
                case MenuItem.FoolRocco: Close(); _actions.StartFoolRocco(); break;
                // 一個開關同時管手勢提示與牌力提示
                case MenuItem.Hints: Settings.SetHints(!Settings.Hints); Edited(); break;
                case MenuItem.TextSize: Settings.SubtitleScale = UxSettings.NextTextSize(Settings.SubtitleScale); Edited(); break;
                case MenuItem.HighContrast: Settings.HighContrast = !Settings.HighContrast; Edited(); break;
                // Off＝自動（系統慣用手），On＝左手
                case MenuItem.LeftHanded: Settings.Handedness = Settings.LeftHanded ? Handedness.Auto : Handedness.Left; Edited(); break;
                case MenuItem.VoiceChat:
                    // 第一次打開先顯示同意畫面；同意過才直接切換
                    if (!Settings.VoiceChat && !Settings.VoiceConsent) { Page = MenuPage.VoiceConsent; Changed?.Invoke(); break; }
                    Settings.VoiceChat = !Settings.VoiceChat; Edited(); break;
                case MenuItem.VoiceAccept:
                    Settings.VoiceConsent = true; Settings.VoiceChat = true; Page = MenuPage.Settings; Edited(); break;
                case MenuItem.VoiceDecline:
                    Page = MenuPage.Settings; Changed?.Invoke(); break;
            }
        }

        void Edited()
        {
            Settings.Sanitize();
            SettingsChanged?.Invoke(Settings);
            Changed?.Invoke();
        }
    }
}
