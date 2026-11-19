using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using Oculus.Interaction.Surfaces;
using Poker.Game;
using UnityEngine;

namespace Poker.UX
{
    /// <summary>
    /// 以 ISDK v207 注視元件判斷玩家在看哪個角色：
    /// IGaze（實機為 EyeGaze）→ GazeConecaster（錐形投射＋0.2 秒共識）→ GazeInteractor 的 hover → 角色索引。
    /// ISDK 沒有「停留 N 秒選取」，所以 1 秒停留仍由 GazeTracker 計算；這裡只提供 IGazeTargetSource。
    /// 全部在執行期 AddComponent＋Inject 建立，不需要 prefab。
    /// </summary>
    public sealed class IsdkGazeRig : MonoBehaviour, IGazeTargetSource
    {
        public const float ConeAngleDegrees = 2f;

        IGaze _gaze;
        GazeInteractor _interactor;
        readonly Dictionary<GazeInteractable, int> _targets = new Dictionary<GazeInteractable, int>();

        /// <summary>已登記的注視編號（不重複）。</summary>
        public IEnumerable<int> TargetIds => new HashSet<int>(_targets.Values);
        public GazeInteractor Interactor => _interactor;
        public IGaze Gaze => _gaze;

        /// <summary>在 parent 下建立注視 interactor。pointer 是互動的參考原點（頭）。</summary>
        public static IsdkGazeRig Build(Transform parent, IGaze gaze, Transform pointer)
        {
            var go = new GameObject("IsdkGaze");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<IsdkGazeRig>();
            rig._gaze = gaze;

            var caster = go.AddComponent<GazeConecaster>();
            caster.InjectAllGazeConecaster(gaze);
            caster.ConeAngle = ConeAngleDegrees;
            var selector = go.AddComponent<VirtualSelector>();   // 不用選取，只用 hover
            rig._interactor = go.AddComponent<GazeInteractor>();
            rig._interactor.InjectAllGazeInteractor(selector, pointer, caster);
            return rig;
        }

        /// <summary>
        /// ISDK 的 EyeGaze 修飾器：emulateWithCamera=true 時把資料來源的相機姿勢當成注視姿勢（EyeGaze.cs:58-66）。
        /// Quest 3／3S 沒有眼動（OVRPlugin.eyeGazeInteractionsSupported=false），要開這個才有頭部注視。
        /// </summary>
        public static EyeGaze AddEmulatingEyeGaze(GameObject go, IDataSource<EyeGazeDataAsset> source, bool emulateWithCamera)
        {
            var eye = go.AddComponent<EyeGaze>();
            eye.InjectAllDataModifier(DataSource<EyeGazeDataAsset>.UpdateModeFlags.AfterPreviousStep, source, source, true);
            eye.EmulateGazeWithCameraPose = emulateWithCamera;
            return eye;
        }

        /// <summary>在 target 上加一個可注視的球（觸發器、Ignore Raycast 層，不影響物理）。</summary>
        public GazeInteractable AddTarget(Transform target, float radius, int index)
        {
            var go = new GameObject("GazeTarget");
            go.layer = 2; // Ignore Raycast：ColliderSurface 用 Collider.Raycast，不受層影響
            go.transform.SetParent(target, false);
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = radius / Mathf.Max(1e-4f, target.lossyScale.x);
            var surface = go.AddComponent<ColliderSurface>();
            surface.InjectAllColliderSurface(col);
            var gi = go.AddComponent<GazeInteractable>();
            gi.InjectAllGazeInteractable(surface);
            _targets[gi] = index;
            return gi;
        }

        public bool TryGetTarget(out int target)
        {
            target = -1;
            if (!this || !isActiveAndEnabled || _gaze == null || _interactor == null) return false;
            if (!_gaze.TryGetWorldPose(out _)) return false;   // 注視不可用 → 呼叫端退回頭部射線
            if (_interactor.HasInteractable && _interactor.Interactable is GazeInteractable gi && _targets.TryGetValue(gi, out int i))
                target = i;
            return true;
        }

        void OnDisable()
        {
            if (ReferenceEquals(GazeTracker.DefaultSource, this)) GazeTracker.DefaultSource = null;
        }
    }

    /// <summary>以 Transform（頭）姿勢當注視的 IGaze，每幀發 WhenUpdated。測試與沒有 OVR 資料來源時使用。</summary>
    public sealed class TransformGaze : MonoBehaviour, IGaze
    {
        public Transform Source;
        public event System.Action WhenUpdated = delegate { };
        public bool IsTrackedDataValid => Source != null;

        public bool TryGetWorldPose(out Pose pose)
        {
            pose = Source != null ? new Pose(Source.position, Source.rotation) : Pose.identity;
            return Source != null;
        }

        void Update() => WhenUpdated();
    }
}
