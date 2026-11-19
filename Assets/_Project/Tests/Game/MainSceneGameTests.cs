using System.Collections;
using System.Linq;
using NUnit.Framework;
using Poker.Game;
using Poker.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Poker.Tests.Game
{
    /// <summary>Main 場景（MainSceneBuilder 產生）裡有接好線的 GameController、MR 燈光，沒有除錯看板與錄製面板（開發工具另外開）。</summary>
    public class MainSceneGameTests
    {
        [UnityTest]
        public IEnumerator MainScene_HasWiredGame_MrLighting_AndNoDebugTools()
        {
            SceneManager.LoadScene("Main");
            for (int i = 0; i < 90; i++) yield return null;

            var game = Object.FindAnyObjectByType<GameController>();
            Assert.IsNotNull(game, "GameController in Main");
            Assert.IsNotNull(game.Table);
            Assert.IsNotNull(game.HandsSource as IHandsFrameSource, "hands source wired");
            Assert.IsNotNull(game.Head);
            Assert.IsTrue(game.IsInitialized);
            Assert.IsNotNull(game.Pip);
            Assert.AreEqual(game.Table.transform, game.Pip.transform.parent, "characters follow the table");
            Assert.AreNotEqual(GameFlow.NotStarted, game.Flow, "a hand is running");
            Assert.AreNotEqual(0UL, game.Config.Seed, "scene seed 0 is replaced by a fresh one at launch");
            Assert.AreNotEqual(20261015UL, game.Config.Seed, "deals are not the same every launch");

            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.type == LightType.Directional && l.enabled).ToList();
            Assert.GreaterOrEqual(lights.Count, 2, "key + fill");
            Assert.IsTrue(lights.All(l => l.shadows == LightShadows.None));
            var amb = RenderSettings.ambientProbe;
            var c = new Vector3[] { Vector3.up };
            var outC = new Color[1];
            amb.Evaluate(c, outC);
            Assert.Greater(outC[0].grayscale, 0.2f, "ambient is not black");

            Assert.AreEqual(4, game.SeatCount, "player + Pip, Rocco, Vex");
            // 2026-10-13 使用者：玩家畫面不該有除錯數值。開發工具（手部除錯看板、錄製面板）只在 TELL_DEV_TOOLS=1 產生的場景裡
            Assert.IsNull(Object.FindAnyObjectByType<HandDebugReadout>(FindObjectsInactive.Include), "no hand debug board in the player build");
            Assert.IsNull(Object.FindAnyObjectByType<RecorderPanel>(FindObjectsInactive.Include), "no recorder panel in the player build");
        }
    }
}
