using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Poker.Runtime
{
    public enum AnchorStatus { Success, NotFound, PermissionDenied, Failed }

    public readonly struct AnchorLoadResult
    {
        public readonly AnchorStatus Status; public readonly Guid Uuid; public readonly Pose Pose; public readonly string Detail;
        AnchorLoadResult(AnchorStatus s, Guid u, Pose p, string d) { Status = s; Uuid = u; Pose = p; Detail = d; }
        public static AnchorLoadResult Found(Guid uuid, Pose pose) => new AnchorLoadResult(AnchorStatus.Success, uuid, pose, null);
        public static AnchorLoadResult NotFound() => new AnchorLoadResult(AnchorStatus.NotFound, Guid.Empty, Pose.identity, null);
        public static AnchorLoadResult PermissionDenied(string d) => new AnchorLoadResult(AnchorStatus.PermissionDenied, Guid.Empty, Pose.identity, d);
        public static AnchorLoadResult Failed(string d) => new AnchorLoadResult(AnchorStatus.Failed, Guid.Empty, Pose.identity, d);
    }

    public readonly struct AnchorSaveResult
    {
        public readonly AnchorStatus Status; public readonly Guid Uuid; public readonly string Detail;
        AnchorSaveResult(AnchorStatus s, Guid u, string d) { Status = s; Uuid = u; Detail = d; }
        public static AnchorSaveResult Saved(Guid uuid) => new AnchorSaveResult(AnchorStatus.Success, uuid, null);
        public static AnchorSaveResult PermissionDenied(string d) => new AnchorSaveResult(AnchorStatus.PermissionDenied, Guid.Empty, d);
        public static AnchorSaveResult Failed(string d) => new AnchorSaveResult(AnchorStatus.Failed, Guid.Empty, d);
    }

    public interface ITableAnchorStore
    {
        Task<AnchorLoadResult> LoadAsync(IReadOnlyList<Guid> uuids);
        Task<AnchorSaveResult> SaveAsync(Pose pose);
        void Erase(Guid uuid);
    }

    public interface IAnchorMemory { string Load(); void Save(string value); }

    public enum TableAnchorState { Idle, Restoring, Restored, Placement }

    /// <summary>
    /// 牌桌錨點的流程（純邏輯；實機的儲存是 OvrTableAnchorStore，測試用假的）：
    /// 開啟時有存過的錨點就先找（Restoring，這段時間 TablePlacer 不自動放桌）→ 找到就把桌子放回原位（Restored）；
    /// 找不到、逾時、權限不足或出錯就走原本的放置流程（Placement）。
    /// 之後每次放到真實表面（MRUK、射線、雙手校準）且停穩 SaveDelay 秒，就存成新錨點（取代這一桌原本的錨點）。
    /// 最多記 MaxRooms 個房間（最近用的在前），換房間回來也找得到。所有結果都在 Tick（主執行緒）處理。
    /// </summary>
    public sealed class TableAnchorFlow
    {
        /// <summary>放好之後要停穩多久才存（MRUK 載入後常常連續更新好幾次）。</summary>
        public const double SaveDelay = 2.5;
        /// <summary>找錨點最多等這麼久，之後就走放置流程（晚到的結果丟掉）。</summary>
        public const double RestoreTimeout = 8.0;
        public const int MaxRooms = 3;
        /// <summary>和目前錨點差這麼少就不重存（重新校準在同一個位置）。</summary>
        const float SameSpotMeters = 0.02f, SameSpotDegrees = 2f;

        readonly ITableAnchorStore _store;
        readonly IAnchorMemory _memory;
        readonly Action<string> _log;
        List<(Guid uuid, TablePlacementSource source)> _entries = new List<(Guid, TablePlacementSource)>();
        Task<AnchorLoadResult> _load;
        double _restoreDeadline;
        Task<AnchorSaveResult> _save;
        Pose _savingPose;
        TablePlacementSource _savingSource;
        bool _pending;
        Pose _pendingPose;
        TablePlacementSource _pendingSource;
        double _pendingSince;
        bool _saveDisabled;
        Pose _activePose;

        /// <summary>找到錨點：把牌桌放到這個世界姿勢（第二個參數是當初放置的來源）。</summary>
        public event Action<Pose, TablePlacementSource> Restore;

        public TableAnchorFlow(ITableAnchorStore store, IAnchorMemory memory, Action<string> log)
        {
            _store = store;
            _memory = memory;
            _log = log ?? (_ => { });
        }

        public TableAnchorState State { get; private set; } = TableAnchorState.Idle;
        /// <summary>正在找錨點：先不要用 MRUK／射線自動放桌（找到之後桌子會跳一次）。</summary>
        public bool HoldsPlacement => State == TableAnchorState.Restoring;
        /// <summary>目前這張牌桌對應的錨點（沒有就是 Guid.Empty）。</summary>
        public Guid ActiveUuid { get; private set; }
        /// <summary>目前的錨點就是現在這張桌子（沒有等著存的新位置）：recenter 後可以用錨點把桌子放回原位。</summary>
        public bool InSync => ActiveUuid != Guid.Empty && !_pending && _save == null;

        public void Begin(double now, bool ignoreSaved = false)
        {
            if (State != TableAnchorState.Idle) return;
            _entries = Decode(_memory.Load());
            if (ignoreSaved && _entries.Count > 0)
            {
                // 重拍用（FirstRunReplay）：這次不找，舊錨點留著（新放的桌子另外記一筆）
                State = TableAnchorState.Placement;
                _log("[Table] first-run replay: " + _entries.Count + " saved anchor(s) ignored -> placement");
                return;
            }
            if (_entries.Count == 0)
            {
                State = TableAnchorState.Placement;
                _log("[Table] no saved anchor -> placement");
                return;
            }
            var uuids = _entries.ConvertAll(e => e.uuid);
            State = TableAnchorState.Restoring;
            _restoreDeadline = now + RestoreTimeout;
            _log("[Table] looking for " + uuids.Count + " saved anchor(s)");
            try { _load = _store.LoadAsync(uuids); }
            catch (Exception e) { FallBack("load threw " + e.Message); }
        }

        /// <summary>目前的錨點在世界中換了位置（系統 recenter）：記下新的世界姿勢，桌子跟過去不算新桌子。</summary>
        public void NoteAnchorMoved(Pose pose)
        {
            if (ActiveUuid != Guid.Empty) _activePose = pose;
        }

        /// <summary>
        /// 目前的錨點不再代表這張桌子，但不刪（可能是另一個位置／房間的桌子，回去還找得到）：
        /// 放回的桌子不合理（TableRestoreCheck）、或換位置校準搬到很遠的地方。之後存的新錨點另外記一筆，不會取代它。
        /// </summary>
        public void DetachActive(string why)
        {
            if (State == TableAnchorState.Restored) State = TableAnchorState.Placement;
            if (ActiveUuid == Guid.Empty) return;
            _log("[Table] keeping anchor " + ActiveUuid + " for later (" + why + ")");
            ActiveUuid = Guid.Empty;
            _activePose = default;
        }

        public void OnTablePlaced(Pose pose, TablePlacementSource source, double now)
        {
            if (source != TablePlacementSource.MrukTable && source != TablePlacementSource.EnvironmentRaycast && source != TablePlacementSource.HandCalibrated) return;
            if (State == TableAnchorState.Restoring)
            {
                if (source != TablePlacementSource.HandCalibrated) return;   // 自動放置在找錨點時本來就不該發生
                _load = null;
                State = TableAnchorState.Placement;
                _log("[Table] calibrated while restoring -> placement");
            }
            // 和目前的錨點同一個位置（同一處重新校準、recenter 後跟著錨點）：不是新桌子，不用存
            if (ActiveUuid != Guid.Empty && Same(pose, _activePose)) { _pending = false; return; }
            _pending = true;
            _pendingPose = pose;
            _pendingSource = source;
            _pendingSince = now;
        }

        public void Tick(double now)
        {
            if (State == TableAnchorState.Restoring) TickRestore(now);
            TickSave(now);
        }

        void TickRestore(double now)
        {
            if (_load != null && _load.IsCompleted)
            {
                var task = _load;
                _load = null;
                if (task.IsFaulted || task.IsCanceled) { FallBack("load failed (" + (task.Exception?.GetBaseException().Message ?? "canceled") + ")"); return; }
                var r = task.Result;
                switch (r.Status)
                {
                    case AnchorStatus.Success:
                        int i = _entries.FindIndex(e => e.uuid == r.Uuid);
                        var from = i >= 0 ? _entries[i].source : TablePlacementSource.None;
                        if (i > 0) { var e = _entries[i]; _entries.RemoveAt(i); _entries.Insert(0, e); Persist(); }
                        State = TableAnchorState.Restored;
                        ActiveUuid = r.Uuid;
                        _activePose = r.Pose;
                        _log("[Table] restored from anchor " + r.Uuid + " (" + from + ")");
                        Restore?.Invoke(r.Pose, from);
                        return;
                    case AnchorStatus.NotFound:
                        FallBack(null);
                        return;
                    case AnchorStatus.PermissionDenied:
                        _saveDisabled = true;
                        FallBack("permission denied: " + r.Detail);
                        return;
                    default:
                        FallBack("load failed (" + r.Detail + ")");
                        return;
                }
            }
            if (now > _restoreDeadline)
            {
                _load = null;
                FallBack("timeout " + RestoreTimeout.ToString("F0") + " s");
            }
        }

        void FallBack(string why)
        {
            _load = null;
            State = TableAnchorState.Placement;
            _log("[Table] anchor not found -> placement" + (why == null ? "" : " (" + why + ")"));
        }

        void TickSave(double now)
        {
            if (_save != null)
            {
                if (!_save.IsCompleted) return;
                var task = _save;
                _save = null;
                if (task.IsFaulted || task.IsCanceled) _log("[Table] anchor save failed (" + (task.Exception?.GetBaseException().Message ?? "canceled") + ")");
                else OnSaved(task.Result);
            }
            if (!_pending || _saveDisabled || now - _pendingSince < SaveDelay) return;
            _pending = false;
            _savingPose = _pendingPose;
            _savingSource = _pendingSource;
            try { _save = _store.SaveAsync(_savingPose); }
            catch (Exception e) { _log("[Table] anchor save failed (" + e.Message + ")"); return; }
            if (_save.IsCompleted) TickSave(now);
        }

        void OnSaved(AnchorSaveResult r)
        {
            if (r.Status != AnchorStatus.Success)
            {
                if (r.Status == AnchorStatus.PermissionDenied) _saveDisabled = true;
                _log("[Table] anchor save failed (" + r.Detail + ")");
                return;
            }
            // 這一桌原本的錨點作廢；其他房間的留著
            if (ActiveUuid != Guid.Empty)
            {
                int old = _entries.FindIndex(e => e.uuid == ActiveUuid);
                if (old >= 0) _entries.RemoveAt(old);
                _store.Erase(ActiveUuid);
            }
            _entries.Insert(0, (r.Uuid, _savingSource));
            while (_entries.Count > MaxRooms)
            {
                _store.Erase(_entries[_entries.Count - 1].uuid);
                _entries.RemoveAt(_entries.Count - 1);
            }
            Persist();
            ActiveUuid = r.Uuid;
            _activePose = _savingPose;
            _log("[Table] anchor saved " + r.Uuid + " (" + _savingSource + ")");
        }

        void Persist() => _memory.Save(Encode(_entries));

        static bool Same(Pose a, Pose b) =>
            Vector3.Distance(a.position, b.position) <= SameSpotMeters && Quaternion.Angle(a.rotation, b.rotation) <= SameSpotDegrees;

        /// <summary>存檔格式：「uuid|來源;uuid|來源」，最近用的在前。</summary>
        public static string Encode(IEnumerable<(Guid uuid, TablePlacementSource source)> entries)
        {
            var parts = new List<string>();
            foreach (var (uuid, source) in entries) parts.Add(uuid.ToString("D") + "|" + source);
            return string.Join(";", parts);
        }

        public static List<(Guid uuid, TablePlacementSource source)> Decode(string value)
        {
            var list = new List<(Guid, TablePlacementSource)>();
            if (string.IsNullOrEmpty(value)) return list;
            foreach (var part in value.Split(';'))
            {
                var bits = part.Split('|');
                if (!Guid.TryParse(bits[0], out var uuid) || uuid == Guid.Empty) continue;
                var source = TablePlacementSource.None;
                if (bits.Length > 1 && Enum.TryParse(bits[1], out TablePlacementSource s) && Enum.IsDefined(typeof(TablePlacementSource), s)) source = s;
                if (list.Exists(e => e.Item1 == uuid)) continue;
                list.Add((uuid, source));
            }
            return list;
        }
    }
}
