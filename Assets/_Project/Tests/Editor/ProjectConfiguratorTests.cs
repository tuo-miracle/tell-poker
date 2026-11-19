using NUnit.Framework;
using Poker.Editor.Build;
using UnityEditor;

namespace Poker.Tests.Editor
{
    /// <summary>
    /// 交件清單 #1（2026-10-14）：中繼已改 https（WSS），release 建置不允許明文 HTTP。
    /// 本機中繼（tools/voice-dev.sh，http://127.0.0.1:8787）要測時，建置前設環境變數 TELL_ALLOW_HTTP=1（只給開發建置）。
    /// </summary>
    public class ProjectConfiguratorTests
    {
        [Test]
        public void CleartextHttp_IsNotAllowed_UnlessTheDevOverrideIsSet()
        {
            Assert.AreEqual(InsecureHttpOption.NotAllowed, ProjectConfigurator.HttpOptionFor(null));
            Assert.AreEqual(InsecureHttpOption.NotAllowed, ProjectConfigurator.HttpOptionFor(""));
            Assert.AreEqual(InsecureHttpOption.NotAllowed, ProjectConfigurator.HttpOptionFor("0"));
            Assert.AreEqual(InsecureHttpOption.NotAllowed, ProjectConfigurator.HttpOptionFor("yes please"));
            Assert.AreEqual(InsecureHttpOption.AlwaysAllowed, ProjectConfigurator.HttpOptionFor("1"));
            Assert.AreEqual(InsecureHttpOption.AlwaysAllowed, ProjectConfigurator.HttpOptionFor(" true "));
            Assert.AreEqual("TELL_ALLOW_HTTP", ProjectConfigurator.AllowHttpEnv);
        }

        [Test]
        public void BoundaryCanBeHidden_ForASeatedMixedRealityGame()
        {
            // 2026-10-15 實機：坐著往前傾看牌，頭超出靜止邊界 → 系統把整個遊戲淡化。MR（passthrough 開著）可以要求隱藏邊界
            Assert.IsTrue(ProjectConfigurator.HidesBoundary);
        }

        [Test]
        public void CommittedProjectSettings_AreTheReleaseValue()
        {
            // ProjectSettings.asset 也要是 release 的值（configurator 每次建置會再套一次，但檔案本身不能留著舊的 AlwaysAllowed）
            Assert.AreEqual(InsecureHttpOption.NotAllowed, PlayerSettings.insecureHttpOption);
        }
    }
}
