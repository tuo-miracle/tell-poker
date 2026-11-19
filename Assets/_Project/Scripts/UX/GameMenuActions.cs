using Poker.Game;
using Poker.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Poker.UX
{
    /// <summary>選單動作接到遊戲。</summary>
    public sealed class GameMenuActions : IMenuActions
    {
        readonly GameController _game;
        public GameMenuActions(GameController game) { _game = game; }

        /// <summary>
        /// 重新開一局：GameController 目前沒有公開的重開 API（StartNextHand 是 private），先用重新載入場景代替。
        /// 代價是牌桌要重新放置；已回報需求 GameController.RestartMatch()。
        /// </summary>
        public void NewGame()
        {
            Debug.Log("[UX] new game: reloading scene");
            var scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex >= 0 ? scene.buildIndex : 0);
        }

        /// <summary>
        /// 「Move table here」（舊名 Recalibrate table）：牌桌上有 TablePlacer 就照系統重新置中的做法（RecenterPlan）把桌子放到面前、
        /// 面前有真實桌面就對齊，再顯示雙手手印；沒有 TablePlacer（測試場景）就只顯示手印。
        /// </summary>
        public void MoveTableHere()
        {
            // 只用這張桌子上的 TablePlacer（Main 場景放在同一個物件）；不全場景找，免得搬到別張桌子
            if (_game != null && _game.Table != null && _game.Table.TryGetComponent(out TablePlacer placer)) { placer.BringToFront("menu"); return; }
            HandSurfaceCalibration cal = null;
            if (_game != null && _game.Table != null) _game.Table.TryGetComponent(out cal);
            if (cal == null) cal = Object.FindAnyObjectByType<HandSurfaceCalibration>();
            if (cal == null) { Debug.LogWarning("[UX] move table: no TablePlacer or HandSurfaceCalibration in scene"); return; }
            cal.Recalibrate();
        }

        /// <summary>下一手重新教一次（GameController.ReplayTutorial）。</summary>
        public void ReplayTutorial()
        {
            if (_game != null && _game.IsInitialized) _game.ReplayTutorial();
        }

        public bool TutorialActive => _game != null && _game.IsInitialized && _game.Tutorial != null && _game.Tutorial.IsActive;

        /// <summary>跳過整個教學（GameController.SkipTutorial）。</summary>
        public void SkipTutorial()
        {
            if (_game != null && _game.IsInitialized) _game.SkipTutorial();
        }

        /// <summary>
        /// Fool Rocco（拍宣傳片用的原型情境）不在玩家版選單（2026-10-15）：只有開發工具場景（TELL_DEV_TOOLS=1 建的，場景裡有 RecorderPanel）才出現。
        /// </summary>
        public bool FoolRoccoAvailable => DevToolsScene && _game != null && _game.CanStartFoolRocco;

        bool? _devTools;
        bool DevToolsScene => _devTools ??= Object.FindAnyObjectByType<RecorderPanel>(FindObjectsInactive.Include) != null;

        /// <summary>開始 Fool Rocco 情境（GameController.StartFoolRocco；下一手起）。</summary>
        public void StartFoolRocco()
        {
            if (_game != null && _game.IsInitialized) _game.StartFoolRocco();
        }

        public void Quit()
        {
            Debug.Log("[UX] quit");
            Application.Quit();
        }
    }
}
