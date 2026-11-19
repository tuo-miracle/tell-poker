using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>
    /// Mac Editor 沒有 XR 時：把頭部放到坐姿高度並略為低頭，讓虛擬桌與回放的手入鏡；
    /// 手部來源改用 .hands 回放（RecordingHandsSource）。有 XR 時什麼都不做。
    /// 必須比 TablePlacer.Start 早執行（這裡用 Awake）。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class EditorSimulation : MonoBehaviour
    {
        [SerializeField] Transform head;
        [SerializeField] float seatedEyeHeight = 1.2f;
        [SerializeField] float lookDownDegrees = 35f;
        [SerializeField] HandsSourceSelector selector;

        public static bool ShouldSimulate => Application.isEditor && !XrRuntime.IsActive && !UnityEngine.XR.XRSettings.isDeviceActive;

        void Awake()
        {
            if (!ShouldSimulate) return;
            if (head != null)
            {
                head.position = new Vector3(0f, seatedEyeHeight, 0f);
                head.rotation = Quaternion.Euler(lookDownDegrees, 0f, 0f);
            }
            if (selector != null) selector.UsePlayback();
        }
    }
}
