using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>
    /// 實機的牌桌錨點（Meta XR Core 207 的 OVRSpatialAnchor，存在頭盔本機）。只在 XR 執行中使用；Editor／測試用假的。
    /// - 存：在牌桌姿勢建一個 OVRSpatialAnchor（OVRSpatialAnchor.cs:153 WhenCreatedAsync、:206 WhenLocalizedAsync）→ SaveAnchorAsync（:607）。
    /// - 找：LoadUnboundAnchorsAsync（:1136）→ 每個依序 LocalizeAsync（:1034，帶逾時）→ TryGetPose（:991）→ BindTo（:1068）到新的元件，之後跟著追蹤更新。
    /// - 刪：EraseAnchorsAsync(null, uuids)（:634）。
    /// 權限：USE_ANCHOR_API 是安裝時權限，由 OVRProjectConfig.anchorSupport 寫進 manifest（ProjectConfigurator.ApplyOvrProjectConfig）；
    /// 沒有的話結果是 FailurePermissionInsufficient（OVRAnchor.cs:138、:325）。
    /// </summary>
    public sealed class OvrTableAnchorStore : ITableAnchorStore
    {
        /// <summary>每個錨點定位最多等幾秒（換了房間的錨點不會成功）。</summary>
        const double LocalizeTimeout = 3.0;

        OVRSpatialAnchor _live;

        /// <summary>目前這張牌桌的錨點（已定位）在世界中的姿勢：系統 recenter 之後用它把牌桌放回原位。</summary>
        public bool TryGetLivePose(out Pose pose)
        {
            pose = default;
            if (_live == null || !_live.Localized || !_live.IsTracked) return false;
            pose = new Pose(_live.transform.position, _live.transform.rotation);
            return true;
        }

        public async Task<AnchorLoadResult> LoadAsync(IReadOnlyList<Guid> uuids)
        {
            var unbound = new List<OVRSpatialAnchor.UnboundAnchor>();
            var load = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(uuids, unbound);
            if (!load.Success)
            {
                return load.Status == OVRAnchor.FetchResult.FailurePermissionInsufficient
                    ? AnchorLoadResult.PermissionDenied(load.Status.ToString())
                    : AnchorLoadResult.Failed(load.Status.ToString());
            }
            Debug.Log($"[Table] anchor load: {unbound.Count}/{uuids.Count} known on this headset");
            foreach (var uuid in uuids)   // 依最近使用的順序
            {
                int i = unbound.FindIndex(a => a.Uuid == uuid);
                if (i < 0) continue;
                var a = unbound[i];
                bool localized = a.Localized || await a.LocalizeAsync(LocalizeTimeout);
                if (!localized) { Debug.Log($"[Table] anchor {uuid} not localized (other room?)"); continue; }
                if (!a.TryGetPose(out var pose)) continue;
                Bind(a);
                return AnchorLoadResult.Found(uuid, pose);
            }
            return AnchorLoadResult.NotFound();
        }

        public async Task<AnchorSaveResult> SaveAsync(Pose pose)
        {
            var go = new GameObject("TableAnchor");
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            var anchor = go.AddComponent<OVRSpatialAnchor>();
            if (!await anchor.WhenLocalizedAsync())
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                return AnchorSaveResult.Failed("create/localize failed");
            }
            var save = await anchor.SaveAnchorAsync();
            if (!save.Success)
            {
                UnityEngine.Object.Destroy(go);
                return save.Status == OVRAnchor.SaveResult.FailurePermissionInsufficient
                    ? AnchorSaveResult.PermissionDenied(save.Status.ToString())
                    : AnchorSaveResult.Failed(save.Status.ToString());
            }
            SetLive(anchor);
            return AnchorSaveResult.Saved(anchor.Uuid);
        }

        public void Erase(Guid uuid)
        {
            if (_live != null && _live.Uuid == uuid) SetLive(null);
            EraseAsync(uuid);
        }

        static async void EraseAsync(Guid uuid)
        {
            try
            {
                var r = await OVRSpatialAnchor.EraseAnchorsAsync(null, new[] { uuid });
                Debug.Log($"[Table] anchor erased {uuid}: {r.Status}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Table] anchor erase {uuid} threw: {e.Message}");
            }
        }

        void Bind(OVRSpatialAnchor.UnboundAnchor a)
        {
            // BindTo 要在同一幀新建的元件上做（OVRSpatialAnchor.cs:1058-1062），Start 才不會再建一個新錨點
            var go = new GameObject("TableAnchor");
            var anchor = go.AddComponent<OVRSpatialAnchor>();
            a.BindTo(anchor);
            SetLive(anchor);
        }

        void SetLive(OVRSpatialAnchor anchor)
        {
            if (_live != null && _live != anchor) UnityEngine.Object.Destroy(_live.gameObject);
            _live = anchor;
        }
    }

    /// <summary>錨點清單存在 PlayerPrefs（只有 UUID 與來源，不含任何房間資料）。</summary>
    public sealed class PlayerPrefsAnchorMemory : IAnchorMemory
    {
        public const string Key = "TableAnchors.v1";
        public string Load() => PlayerPrefs.GetString(Key, "");

        public void Save(string value)
        {
            PlayerPrefs.SetString(Key, value);
            PlayerPrefs.Save();
        }
    }
}
