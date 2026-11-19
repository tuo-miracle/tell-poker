using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>
    /// 執行期顯示設定：72 Hz、Time.fixedDeltaTime 對齊刷新率、固定注視點渲染中等、不開 Application SpaceWarp。
    /// XR session 建立前設定會靜默失敗（MetaXRDisplayRefreshRateOVRPluginShim.cs:33），所以每 0.5 s 重試直到成功。
    /// </summary>
    public sealed class QuestDisplaySettings : MonoBehaviour
    {
        [SerializeField] float targetRefreshRate = 72f;
        [SerializeField] OVRManager.FoveatedRenderingLevel foveation = OVRManager.FoveatedRenderingLevel.Medium;
        [SerializeField] bool dynamicFoveation = false;

        bool _refreshDone;
        bool _foveationDone;
        float _nextTry;
        int _attempts;

        void Awake()
        {
            // 不管有沒有 XR，物理步長都先對齊目標刷新率
            Time.fixedDeltaTime = 1f / targetRefreshRate;
        }

        bool? _boundarySuppressed;

        void Update()
        {
            // 診斷：系統邊界是否真的被隱藏（OVRManager 只在第一次被拒時印警告，之後默默重試）。用 [Table] 前綴，遠端紀錄收得到
            var mgr = OVRManager.instance;
            if (mgr != null && mgr.isBoundaryVisibilitySuppressed != _boundarySuppressed)
            {
                _boundarySuppressed = mgr.isBoundaryVisibilitySuppressed;
                Debug.Log($"[Table] boundary suppressed={_boundarySuppressed} (wanted={mgr.shouldBoundaryVisibilityBeSuppressed})");
            }
            if ((_refreshDone && _foveationDone) || Time.unscaledTime < _nextTry) return;
            _nextTry = Time.unscaledTime + 0.5f;
            if (!XrRuntime.IsActive) return;
            if (++_attempts > 40)
            {
                Debug.LogWarning($"[QuestDisplaySettings] give up: refreshDone={_refreshDone} foveationDone={_foveationDone} refresh={OVRPlugin.systemDisplayFrequency}");
                _refreshDone = _foveationDone = true;
                return;
            }

            if (!_foveationDone && !OVRManager.fixedFoveatedRenderingSupported && _attempts > 4)
            {
                _foveationDone = true;   // 不支援（例如 Link）：不再重試
                Debug.Log("[QuestDisplaySettings] fixed foveated rendering not supported");
            }
            if (!_foveationDone && OVRManager.fixedFoveatedRenderingSupported)
            {
                // 順序：先 level 再 dynamic（MetaXRFoveationFeature.cs:60-61,89,117 共用快取）
                OVRManager.foveatedRenderingLevel = foveation;
                OVRManager.useDynamicFoveatedRendering = dynamicFoveation;
                _foveationDone = OVRManager.foveatedRenderingLevel == foveation;
            }

            if (!_refreshDone)
            {
                var available = OVRPlugin.systemDisplayFrequenciesAvailable;
                if (available != null && available.Length > 0)
                {
                    if (System.Array.IndexOf(available, targetRefreshRate) >= 0)
                        OVRPlugin.systemDisplayFrequency = targetRefreshRate;
                    float actual = OVRPlugin.systemDisplayFrequency;
                    if (actual > 1f)
                    {
                        Time.fixedDeltaTime = 1f / actual;
                        _refreshDone = Mathf.Approximately(actual, targetRefreshRate) || System.Array.IndexOf(available, targetRefreshRate) < 0;
                    }
                }
            }

            // SpaceWarp 預設關閉（OVRManager.cs:1777）；這裡明確保持關閉，避免手部延遲
            if (OVRManager.GetSpaceWarp()) OVRManager.SetSpaceWarp(false);

            if (_refreshDone && _foveationDone)
                Debug.Log($"[QuestDisplaySettings] refresh={OVRPlugin.systemDisplayFrequency} fixedDt={Time.fixedDeltaTime:F4} foveation={OVRManager.foveatedRenderingLevel} spaceWarp={OVRManager.GetSpaceWarp()}");
        }
    }
}
