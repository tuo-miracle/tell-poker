namespace Poker.Runtime
{
    /// <summary>目前是否真的有 XR 在跑（頭盔或 Link）。Mac Editor 沒有 XR 時為 false，所有 Meta native 呼叫都要先看它。</summary>
    public static class XrRuntime
    {
        /// <summary>
        /// OVRManager.instance 只有在 OVRPlugin.initialized 時才會被設定（OVRManager.cs:2512-2521）；
        /// isHmdPresent 在沒有 display subsystem 時回 false、不丟例外（OVRCommon.cs:854-872）。
        /// </summary>
        public static bool IsActive => OVRManager.instance != null && OVRManager.isHmdPresent;
    }
}
