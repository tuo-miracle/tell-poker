using System;
using UnityEngine;

namespace Poker.UX
{
    /// <summary>手部追蹤模式（Fast Motion Mode 開關）；實機用 OvrHandTrackingMode，測試用假的。</summary>
    public interface IHandTrackingMode
    {
        /// <summary>最後一次成功套用的狀態。</summary>
        bool IsFastMotion { get; }
        void SetFastMotion(bool on);
        /// <summary>每幀呼叫：之前因為還沒有 XR 而跳過的請求，在 XR 就緒後補送。</summary>
        void Tick();
    }

    /// <summary>
    /// 用 OVRPlugin.RequestFastMotionMode(bool) 切換 FMM（OVRPlugin.cs:13045；OVRP 1.114 以前回 Failure_NotYetImplemented）。
    /// 沒有 XR（Editor、batchmode 測試）時不呼叫 OVRPlugin，只記 log。
    /// 不經 OVRManager.fastMotionModeHandPosesEnabled：那條路只在值改變時送一次且拿不到結果（OVRManager.cs:3033-3037）。
    /// </summary>
    public sealed class OvrHandTrackingMode : IHandTrackingMode
    {
        readonly Func<bool> _xrActive;
        readonly Func<bool, int> _request;
        bool? _pending;

        /// <param name="request">測試用：取代 OVRPlugin.RequestFastMotionMode，回傳 OVRPlugin.Result 的整數值。</param>
        public OvrHandTrackingMode(Func<bool> xrActive = null, Func<bool, int> request = null)
        {
            _xrActive = xrActive ?? UxController.XrActive;
            _request = request ?? (on => (int)OVRPlugin.RequestFastMotionMode(on));
        }

        public bool IsFastMotion { get; private set; }

        public void Tick()
        {
            if (_pending.HasValue && _xrActive()) SetFastMotion(_pending.Value);
        }

        public void SetFastMotion(bool on)
        {
            string result;
            _pending = null;
            if (!_xrActive()) { result = "skipped(no XR)"; _pending = on; }   // 實機上 UxController.Setup 比 XR 初始化早，就緒後由 Tick 補送
            else
            {
                try
                {
                    int r = _request(on);
                    result = ((OVRPlugin.Result)r).ToString();
                    if (r >= 0) IsFastMotion = on;   // Success*（0、1、2…）都是非負值，失敗是負值（OVRPlugin.cs:338）
                }
                catch (Exception e) { result = "exception(" + e.GetType().Name + ": " + e.Message + ")"; }
            }
            Debug.Log($"[UX] FastMotion requested={(on ? "true" : "false")} result={result}");
        }
    }
}
