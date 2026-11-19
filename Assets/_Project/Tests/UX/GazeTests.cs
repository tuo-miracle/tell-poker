using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Oculus.Interaction.Input;
using Poker.Game;
using Poker.UX;
using UnityEngine;
using UnityEngine.TestTools;

namespace Poker.Tests.UX
{
    /// <summary>Editor 退回模式：沒有注視來源時，用呼叫端傳入的頭部射線命中。</summary>
    public class GazeTrackerFallbackTests
    {
        const float Dt = 1f / 72f;

        sealed class Log
        {
            public readonly List<int> Started = new List<int>(), Ended = new List<int>();
            public Log(GazeTracker g) { g.GazeStarted += Started.Add; g.GazeEnded += Ended.Add; }
        }

        static void Feed(GazeTracker g, int hit, float seconds)
        {
            for (float t = 0; t < seconds - 1e-4f; t += Dt) g.Update(Dt, hit);
        }

        [SetUp] public void SetUp() => GazeTracker.DefaultSource = null;

        [Test] public void FiresAfterAboutOneSecond()
        {
            var g = new GazeTracker(); var log = new Log(g);
            Feed(g, 0, 0.95f);
            Assert.IsEmpty(log.Started, "未滿 1 秒不觸發");
            Feed(g, 0, 0.1f);
            CollectionAssert.AreEqual(new[] { 0 }, log.Started);
            Assert.AreEqual(0, g.Current);
        }

        [Test] public void FiresOnlyOnceWhileStaring()
        {
            var g = new GazeTracker(); var log = new Log(g);
            Feed(g, 1, 5f);
            CollectionAssert.AreEqual(new[] { 1 }, log.Started);
        }

        [Test] public void LookingAwayBeforeDwellCancels()
        {
            var g = new GazeTracker(); var log = new Log(g);
            Feed(g, 0, 0.7f);
            Feed(g, -1, 0.2f);
            Feed(g, 0, 0.7f);
            Assert.IsEmpty(log.Started, "移開後計時重來");
        }

        [Test] public void LookingAwayEndsGaze()
        {
            var g = new GazeTracker(); var log = new Log(g);
            Feed(g, 0, 1.2f);
            Feed(g, -1, 0.5f);
            CollectionAssert.AreEqual(new[] { 0 }, log.Ended);
            Assert.AreEqual(-1, g.Current);
        }

        [Test] public void DistinguishesCharacters()
        {
            var g = new GazeTracker(); var log = new Log(g);
            Feed(g, 0, 1.2f);
            Feed(g, 1, 1.5f);
            CollectionAssert.AreEqual(new[] { 0, 1 }, log.Started);
            CollectionAssert.AreEqual(new[] { 0 }, log.Ended);
            Assert.AreEqual(1, g.Current);
        }

        sealed class FixedSource : IGazeTargetSource
        {
            public bool Available = true; public int Target;
            public bool TryGetTarget(out int target) { target = Target; return Available; }
        }

        [Test] public void ActiveSourceOverridesHeadRay()
        {
            GazeTracker.DefaultSource = new FixedSource { Target = 1 };
            try
            {
                var g = new GazeTracker(); var log = new Log(g);
                Feed(g, 0, 1.2f);   // 頭部射線說 0，注視來源說 1
                CollectionAssert.AreEqual(new[] { 1 }, log.Started);
            }
            finally { GazeTracker.DefaultSource = null; }
        }

        [Test] public void UnavailableSourceFallsBackToHeadRay()
        {
            var src = new FixedSource { Available = false, Target = 1 };
            var g = new GazeTracker { Source = src }; var log = new Log(g);
            Feed(g, 0, 1.2f);
            CollectionAssert.AreEqual(new[] { 0 }, log.Started);
        }
    }

    /// <summary>ISDK 注視元件（GazeInteractor／GazeConecaster／GazeInteractable）在 Editor 內以 Transform 驅動跑一遍。</summary>
    public class IsdkGazeTests
    {
        readonly List<GameObject> _made = new List<GameObject>();
        GameObject Make(string n) { var go = new GameObject(n); _made.Add(go); return go; }

        [TearDown] public void TearDown()
        {
            foreach (var go in _made) if (go) Object.Destroy(go);
            _made.Clear();
            GazeTracker.DefaultSource = null;
        }

        IsdkGazeRig BuildRig(out Transform head, out Transform pip, out Transform dealer)
        {
            head = Make("Head").transform;
            head.position = new Vector3(0f, 1.2f, 0f);
            pip = Make("Pip").transform;
            pip.position = new Vector3(-0.4f, 1.2f, 1.0f);
            dealer = Make("Dealer").transform;
            dealer.position = new Vector3(0.4f, 1.2f, 1.0f);
            var gaze = Make("Gaze").AddComponent<TransformGaze>();
            gaze.Source = head;
            var rig = IsdkGazeRig.Build(Make("GazeRig").transform, gaze, head);
            rig.AddTarget(pip, 0.16f, 0);
            rig.AddTarget(dealer, 0.16f, 1);
            return rig;
        }

        /// <summary>依時間等待（GazeConecaster 的共識窗是 0.2 秒，batchmode 幀很短，不能用幀數）。</summary>
        static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        static void Look(Transform head, Transform at) => head.rotation = Quaternion.LookRotation(at.position - head.position);

        [UnityTest] public IEnumerator InteractorHoversTheCharacterLookedAt()
        {
            var rig = BuildRig(out var head, out var pip, out var dealer);
            Look(head, dealer);
            yield return Wait(0.5f);
            Assert.IsTrue(rig.TryGetTarget(out int t));
            Assert.AreEqual(1, t);
            Look(head, pip);
            yield return Wait(0.5f);
            Assert.IsTrue(rig.TryGetTarget(out t));
            Assert.AreEqual(0, t);
            head.rotation = Quaternion.LookRotation(Vector3.up + Vector3.back);
            yield return Wait(0.5f);
            Assert.IsTrue(rig.TryGetTarget(out t));
            Assert.AreEqual(-1, t, "看別處時沒有目標");
        }

        [UnityTest] public IEnumerator TrackerDrivenByIsdkFiresOnceAfterDwell()
        {
            var rig = BuildRig(out var head, out _, out var dealer);
            GazeTracker.DefaultSource = rig;
            var g = new GazeTracker();
            var started = new List<int>();
            g.GazeStarted += started.Add;
            Look(head, dealer);
            float t = 0f;
            while (t < 0.8f) { yield return null; g.Update(Time.deltaTime, 0); t += Time.deltaTime; }
            Assert.IsEmpty(started, "0.8 秒內不觸發（傳入的頭部射線 0 被 ISDK 結果取代）");
            while (t < 2f) { yield return null; g.Update(Time.deltaTime, 0); t += Time.deltaTime; }
            CollectionAssert.AreEqual(new[] { 1 }, started);
        }

        /// <summary>Quest 3／3S 沒有眼動：資料來源 IsTracked=false，EyeGaze 開 EmulateGazeWithCameraPose 後改用相機（頭部）姿勢。</summary>
        [UnityTest] public IEnumerator EyeGazeEmulationUsesCameraPoseWhenEyesUntracked()
        {
            var head = Make("Head").transform;
            head.position = new Vector3(0f, 1.2f, 0f);
            var dealer = Make("Dealer").transform;
            dealer.position = new Vector3(0.4f, 1.2f, 1.0f);
            head.rotation = Quaternion.LookRotation(dealer.position - head.position);

            var go = Make("EyeGaze");
            var source = go.AddComponent<UntrackedEyeSource>();
            source.Camera = head;
            source.InjectAllDataSource(DataSource<EyeGazeDataAsset>.UpdateModeFlags.UnityUpdate, null);
            var eye = IsdkGazeRig.AddEmulatingEyeGaze(go, source, emulateWithCamera: true);
            var rig = IsdkGazeRig.Build(Make("GazeRig").transform, eye, head);
            rig.AddTarget(dealer, 0.16f, 1);
            yield return Wait(0.5f);
            Assert.IsTrue(eye.TryGetWorldPose(out var pose));
            Assert.Less(Vector3.Distance(pose.position, head.position), 1e-4f);
            Assert.IsTrue(rig.TryGetTarget(out int t));
            Assert.AreEqual(1, t);
        }

        [UnityTest] public IEnumerator UntrackedEyesWithoutEmulationAreUnavailable()
        {
            var head = Make("Head").transform;
            var go = Make("EyeGaze");
            var source = go.AddComponent<UntrackedEyeSource>();
            source.Camera = head;
            source.InjectAllDataSource(DataSource<EyeGazeDataAsset>.UpdateModeFlags.UnityUpdate, null);
            var eye = IsdkGazeRig.AddEmulatingEyeGaze(go, source, emulateWithCamera: false);
            var rig = IsdkGazeRig.Build(Make("GazeRig").transform, eye, head);
            for (int i = 0; i < 10; i++) yield return null;
            Assert.IsFalse(eye.TryGetWorldPose(out _));
            Assert.IsFalse(rig.TryGetTarget(out _), "注視不可用 → GazeTracker 退回頭部射線");
        }
    }

    /// <summary>模擬 Quest 3 的 FromOVREyeGazeDataSource：沒有眼動（IsTracked=false），只帶相機姿勢（同 FromOVREyeGazeDataSource.UpdateData）。</summary>
    public sealed class UntrackedEyeSource : DataSource<EyeGazeDataAsset>
    {
        public Transform Camera;
        readonly EyeGazeDataAsset _data = new EyeGazeDataAsset();
        protected override EyeGazeDataAsset DataAsset => _data;

        protected override void UpdateData()
        {
            _data.IsTracked = false;
            _data.WorldPose = Pose.identity;
            _data.CameraWorldPose = new Pose(Camera.position, Camera.rotation);
        }
    }
}
