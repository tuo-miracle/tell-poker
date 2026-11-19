using System.Collections;
using NUnit.Framework;
using Poker.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Poker.Tests.Runtime
{
    /// <summary>Mac Editor 沒有 XR：Main 場景要能跑、不丟例外，並退回虛擬桌與回放手部。</summary>
    public class MainSceneTests
    {
        [UnityTest]
        public IEnumerator MainScene_RunsWithoutXr_UsesVirtualTableInFront()
        {
            SceneManager.LoadScene("Main");
            for (int i = 0; i < 60; i++) yield return null;   // 未處理的例外或 LogError 會讓測試失敗

            var table = Object.FindAnyObjectByType<TableFrame>();
            Assert.That(table, Is.Not.Null, "場景要有 TableFrame");
            Assert.That(table.IsPlaced, Is.True);
            Assert.That(table.Source, Is.EqualTo(TablePlacementSource.Virtual));

            var cam = Camera.main;
            Assert.That(cam, Is.Not.Null, "要有 MainCamera（OVRCameraRig 的 CenterEyeAnchor）");
            var toTable = table.TableToWorld(new Vector3(0, 0, TableDimensions.Depth * 0.5f)) - cam.transform.position;
            Assert.That(Vector3.Dot(Vector3.ProjectOnPlane(toTable, Vector3.up), Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up)), Is.GreaterThan(0.3f));
            Assert.That(cam.transform.position.y - table.transform.position.y, Is.EqualTo(TablePlacement.FallbackBelowEye).Within(0.01f));

            var selector = Object.FindAnyObjectByType<HandsSourceSelector>();
            Assert.That(selector, Is.Not.Null);
            Assert.That(selector.IsPlayback, Is.True, "沒有 XR 時改用 .hands 回放");
            Assert.That(Object.FindAnyObjectByType<RecorderPanel>(FindObjectsInactive.Include), Is.Null, "錄製面板是開發工具，玩家場景不放");
            Assert.That(Object.FindAnyObjectByType<OVRManager>(), Is.Not.Null);
            Assert.That(Object.FindAnyObjectByType<OVRManager>().shouldBoundaryVisibilityBeSuppressed, Is.True, "MR 坐姿：往前傾不要被邊界淡化（2026-10-15 實機）");
            Assert.That(Object.FindAnyObjectByType<OVRPassthroughLayer>(), Is.Not.Null);
        }
    }
}
