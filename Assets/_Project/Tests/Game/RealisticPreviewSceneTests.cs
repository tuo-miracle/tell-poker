using System.Collections;
using NUnit.Framework;
using Poker.Characters;
using Poker.Game;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Poker.Tests.Game
{
    /// <summary>寫實角色預覽（TELL_REALISTIC_PREVIEW=1 才放進場景）預設關：正式場景沒有預覽元件，Rocco 還是卡通角色。</summary>
    public class RealisticPreviewSceneTests
    {
        [UnityTest]
        public IEnumerator MainScene_DefaultHasNoRealisticPreview_AndRoccoIsCartoon()
        {
            SceneManager.LoadScene("Main");
            for (int i = 0; i < 60; i++) yield return null;
            Assert.IsNull(Object.FindAnyObjectByType<RealisticPreview>(FindObjectsInactive.Include), "no preview in the shipped scene");
            Assert.AreEqual(0, Object.FindObjectsByType<RealisticBody>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
            var game = Object.FindAnyObjectByType<GameController>();
            var rocco = game.Character(2);
            Assert.AreEqual(CharacterKind.Loudmouth, rocco.Kind);
            Assert.IsTrue(rocco.Rig.HeadRenderer.enabled && !rocco.Rig.HeadRenderer.forceRenderingOff, "cartoon head visible");
        }
    }
}
