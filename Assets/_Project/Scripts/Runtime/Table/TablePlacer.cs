using System;
using System.Collections.Generic;
using Meta.XR;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>
    /// 把牌桌吸附到真實桌面。優先序：上次存的空間錨點（同一個房間就放回原位）&gt; MRUK TABLE 平面 &gt;
    /// EnvironmentRaycast 命中的水平面（只對齊高度）&gt; 虛擬桌。
    /// 啟動時先放虛擬桌（一定看得到東西），之後有更好的來源就升級；權限或房間資料晚到時會自動重新放置。
    /// 放到真實表面（含雙手校準）停穩後存成空間錨點（TableAnchorFlow）。
    /// MRUK 房間載入後把牆與家具換成牌桌座標交給 TableFrame.SetRoom，遊戲據此避開牆安排座位（SeatingPlanner）。
    /// 沒有 XR（Mac Editor）時只放虛擬桌，不碰 MRUK、錨點與任何 Meta native 呼叫。
    /// </summary>
    public sealed class TablePlacer : MonoBehaviour
    {
        [SerializeField] TableFrame table;
        [Tooltip("玩家頭部（OVRCameraRig 的 CenterEyeAnchor）")]
        [SerializeField] Transform head;
        [Tooltip("放 MRUK 與 EnvironmentRaycastManager 的物件，場景中預設停用；有 XR 才啟用")]
        [SerializeField] GameObject mixedRealityRoot;
        [SerializeField] float sceneReloadInterval = 15f;
        [SerializeField] float raycastInterval = 0.25f;

        const float HeadSettleSeconds = 1.0f;
        const int RaycastHitsNeeded = 3;
        /// <summary>EnvironmentRaycast 要求的空曠平面（寬 × 深，公尺）：比整張牌桌小，真實桌面上有雜物也還有機會成立。</summary>
        static readonly Vector2 PlaceBoxSize = new Vector2(0.5f, 0.3f);

        float _xrActiveSince = -1f;
        bool _headPlaced;
        bool _permissionRequested;
        bool _permissionGranted;
        bool _sceneLoaded;
        bool _recenterHooked;
        bool _loading;
        float _nextSceneLoad;
        float _nextRaycast;
        int _raycastStreak;
        float _raycastHeight;
        bool _mrukHooked;
        EnvironmentRaycastManager _raycaster;
        TableAnchorFlow _anchors;
        OvrTableAnchorStore _anchorStore;
        bool _roomDirty;
        float _followAnchorUntil = -1f;
        /// <summary>錨點放回的桌子被拒（不在面前）之後，這次只對齊面前的 MRUK 桌子（不要又選到背後那張）。</summary>
        bool _deskInFrontOnly;
        HandSurfaceCalibration _calibration;
        /// <summary>重拍用（FirstRunReplay，GameController 在寫實預覽版設）：這次不還原存過的錨點，重新放桌、出雙手手印。</summary>
        [System.NonSerialized] public bool IgnoreSavedAnchors;
        /// <summary>換位置校準搬得比這遠，就當成另一個房間／位置：舊錨點留著（回去還找得到），新位置另存一筆。</summary>
        const float NewPlaceMeters = TableRestoreCheck.MaxEdgeDistance;

        public TableFrame Table => table;
        /// <summary>玩家頭部（場景接好；測試可換）。</summary>
        public Transform Head { get => head; set => head = value; }
        /// <summary>最近一次 MRUK 載入結果（除錯用）。</summary>
        public string LastSceneStatus { get; private set; } = "none";

        void Start()
        {
            if (table == null) table = GetComponent<TableFrame>();
            table.Placed += OnTablePlaced;
            if (TryGetComponent(out _calibration)) _calibration.Relocated += OnRelocated;
            PlaceVirtual();
        }

        /// <summary>換位置校準：桌子已經搬好（HandCalibrated，會照常存成新錨點）；搬很遠就把舊錨點留給原本的地方。</summary>
        void OnRelocated(float distance, float yaw)
        {
            if (distance > NewPlaceMeters) _anchors?.DetachActive($"relocated {distance:F1} m away");
        }

        void OnTablePlaced(TableFrame f)
        {
            _anchors?.OnTablePlaced(f.WorldPose, f.Source, Time.unscaledTimeAsDouble);
            _roomDirty = true;   // 障礙物是牌桌座標：桌子動了就要重算
        }

        void OnDestroy()
        {
            if (table != null) table.Placed -= OnTablePlaced;
            if (_calibration != null) _calibration.Relocated -= OnRelocated;
            if (_recenterHooked && OVRManager.display != null) OVRManager.display.RecenteredPose -= OnRecentered;
            if (_mrukHooked && MRUK.Instance != null)
            {
                MRUK.Instance.SceneLoadedEvent.RemoveListener(OnSceneChanged);
                MRUK.Instance.RoomCreatedEvent.RemoveListener(OnRoomChanged);
                MRUK.Instance.RoomUpdatedEvent.RemoveListener(OnRoomChanged);
            }
        }

        /// <summary>強制重新放置（例如之後的「重設牌桌」手勢）。</summary>
        public void Replace()
        {
            _raycastStreak = 0;
            if (!TryPlaceFromMruk()) PlaceVirtual();
        }

        void Update()
        {
            if (!XrRuntime.IsActive) return;
            if (_xrActiveSince < 0f) _xrActiveSince = Time.unscaledTime;

            // 頭部追蹤穩定後，以真正的頭部位置重放一次虛擬桌
            if (!_headPlaced && Time.unscaledTime - _xrActiveSince > HeadSettleSeconds)
            {
                _headPlaced = true;
                if (table.Source != TablePlacementSource.MrukTable) PlaceVirtual();
                if (mixedRealityRoot != null && !mixedRealityRoot.activeSelf) mixedRealityRoot.SetActive(true);
            }
            if (!_headPlaced) return;

            // 空間錨點不需要 USE_SCENE（只要安裝時的 USE_ANCHOR_API），不必等場景權限
            if (_anchors == null)
            {
                _anchorStore = new OvrTableAnchorStore();
                _anchors = new TableAnchorFlow(_anchorStore, new PlayerPrefsAnchorMemory(), Debug.Log);
                _anchors.Restore += OnAnchorRestored;
                _anchors.Begin(Time.unscaledTimeAsDouble, IgnoreSavedAnchors);
            }
            bool holding = _anchors.HoldsPlacement;
            _anchors.Tick(Time.unscaledTimeAsDouble);
            // 找不到錨點、改走放置流程：找的時候 MRUK 事件可能已經來過（被擋下），補放一次
            if (holding && !_anchors.HoldsPlacement && !Locked) TryPlaceFromMruk();
            TickFollowAnchor();

            if (!_recenterHooked && OVRManager.display != null)
            {
                _recenterHooked = true;
                OVRManager.display.RecenteredPose += OnRecentered;
            }

            // IsPermissionGranted 在 Android 是 JNI 呼叫：授權後就不再查
            if (!_permissionGranted) _permissionGranted = OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene);
            if (!_permissionGranted)
            {
                if (!_permissionRequested)
                {
                    _permissionRequested = true;
                    OVRPermissionsRequester.Request(new[] { OVRPermissionsRequester.Permission.Scene });
                }
                return;   // 權限晚到：每幀輪詢，授權後下面的流程自然接上
            }

            HookMruk();
            // 房間一定要載入（就算桌子已經校準或從錨點放回）：座位要避開牆。
            // 載入成功後改靠 RoomCreated／RoomUpdated 事件，不再週期重載（避免週期性卡頓落在錄製資料裡）
            if (!_sceneLoaded && !_loading && Time.unscaledTime >= _nextSceneLoad) LoadScene();
            if (_roomDirty && _sceneLoaded) RefreshRoom();
            if (table.Source != TablePlacementSource.MrukTable && table.Source != TablePlacementSource.EnvironmentRaycast
                && !Locked && !_anchors.HoldsPlacement && Time.unscaledTime >= _nextRaycast)
            {
                _nextRaycast = Time.unscaledTime + raycastInterval;
                TryRaycastPlacement();
            }
        }

        /// <summary>系統 recenter：照 RecenterPlan 把桌子放到玩家面前（有真實桌面就對齊），並顯示雙手校準提示。</summary>
        void OnRecentered() => BringToFront("recenter");

        /// <summary>
        /// 把桌子叫到面前（系統 recenter 與選單「Move table here」共用，RecenterPlan）：校準過／錨點還原的桌子也搬，
        /// 面前有 MRUK 桌子就對齊（只看面前的），最後顯示雙手手印。新位置校準後照常存成新錨點。
        /// </summary>
        public void BringToFront(string reason)
        {
            if (table == null) table = GetComponent<TableFrame>();
            if (_calibration == null) TryGetComponent(out _calibration);
            var plan = RecenterPlan.For(table.Source);
            _followAnchorUntil = -1f;
            Debug.Log($"[Table] {reason} -> place in front (was {table.Source})");
            if (plan.PlaceInFront) { _raycastStreak = 0; PlaceVirtual(force: true); }   // 校準過／錨點還原的桌子也要移過來
            if (plan.LookForDeskInFront) _deskInFrontOnly = true;   // 只對齊面前的桌子，不選背後那張（PickTable frontOnly）
            if (plan.LookForDeskInFront && TryPlaceFromMruk()) Debug.Log($"[Table] {reason} -> snapped to the desk in front");
            if (plan.PromptCalibration && _calibration != null) _calibration.Recalibrate();
        }

        /// <summary>recenter 之後幾幀內錨點的世界姿勢會更新（OVRSpatialAnchor.Update），把桌子對齊過去。</summary>
        void TickFollowAnchor()
        {
            if (_followAnchorUntil < 0f) return;
            if (Time.unscaledTime > _followAnchorUntil) { _followAnchorUntil = -1f; return; }
            if (!_anchors.InSync || !_anchorStore.TryGetLivePose(out var pose)) return;
            var now = table.WorldPose;
            if (Vector3.Distance(now.position, pose.position) < 0.005f && Quaternion.Angle(now.rotation, pose.rotation) < 0.5f) return;
            _anchors.NoteAnchorMoved(pose);
            table.Place(pose, table.Source, table.RestoredFrom);
        }

        /// <summary>
        /// 錨點找到了：先確認它在玩家面前、搆得到、高度合理（TableRestoreCheck）。不合理（轉了方向、換了位置）就這次不用：
        /// 放到面前（面前有 MRUK 桌子就對齊）並顯示雙手提示；舊錨點留著不刪。
        /// </summary>
        void OnAnchorRestored(Pose pose, TablePlacementSource from)
        {
            var why = head != null ? TableRestoreCheck.Reject(pose, head.position, head.forward) : null;
            if (why != null)
            {
                Debug.Log($"[Table] restored anchor rejected ({why}) -> placement");
                _anchors.DetachActive("restore rejected");
                _deskInFrontOnly = true;
                _raycastStreak = 0;
                PlaceVirtual(force: true);
                if (TryPlaceFromMruk()) Debug.Log("[Table] rejected restore -> snapped to the desk in front");
                if (_calibration != null) _calibration.Recalibrate();
                return;
            }
            table.Place(pose, TablePlacementSource.Restored, from);
            Debug.Log($"[TablePlacer] table restored at {pose.position} (was {from})");
        }

        /// <summary>
        /// 玩家已用雙手校準過桌面（手真的碰到桌子，比 MRUK 或射線準），或從錨點放回上次的桌子：不再自動覆蓋。
        /// </summary>
        bool Locked => table != null && (table.Source == TablePlacementSource.HandCalibrated || table.Source == TablePlacementSource.Restored);

        void PlaceVirtual(bool force = false)
        {
            if (Locked && !force) return;
            if (head == null) return;
            table.Place(TablePlacement.Fallback(head.position, head.forward), TablePlacementSource.Virtual);
        }

        void HookMruk()
        {
            if (_mrukHooked || MRUK.Instance == null) return;
            _mrukHooked = true;
            MRUK.Instance.SceneLoadedEvent.AddListener(OnSceneChanged);
            MRUK.Instance.RoomCreatedEvent.AddListener(OnRoomChanged);
            MRUK.Instance.RoomUpdatedEvent.AddListener(OnRoomChanged);
        }

        void OnSceneChanged() { _roomDirty = true; TryPlaceFromMruk(); }
        void OnRoomChanged(MRUKRoom room) { _roomDirty = true; TryPlaceFromMruk(); }

        async void LoadScene()
        {
            if (MRUK.Instance == null) return;
            _loading = true;
            _nextSceneLoad = Time.unscaledTime + sceneReloadInterval;
            try
            {
                // false：沒有房間資料時不跳系統的空間設定 UI（MRUK.Shared.cs:271-276），直接用虛擬桌或 raycast
                var result = await MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false);
                LastSceneStatus = result.ToString();
                Debug.Log("[TablePlacer] LoadSceneFromDevice: " + result);
                if (result == MRUK.LoadDeviceResult.Success)
                {
                    _sceneLoaded = true;
                    _roomDirty = true;
                    TryPlaceFromMruk();
                }
            }
            catch (Exception e)
            {
                LastSceneStatus = "exception";
                Debug.LogWarning("[TablePlacer] LoadSceneFromDevice threw: " + e.Message);
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>從 MRUK 房間找 TABLE 平面。anchor 的本地 +Z 是平面法線、XY 是桌面（MRUKRoom.cs:1345、MRUKAnchor.cs:604）。</summary>
        bool TryPlaceFromMruk()
        {
            if (Locked) return true;
            if (_anchors != null && _anchors.HoldsPlacement) return false;
            if (head == null || MRUK.Instance == null || !MRUK.Instance.IsInitialized) return false;
            var rects = new List<TableRect>();
            foreach (var room in MRUK.Instance.Rooms)
            {
                if (room == null) continue;
                foreach (var anchor in room.Anchors)
                {
                    if (anchor == null || !anchor.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE) || !anchor.PlaneRect.HasValue) continue;
                    var t = anchor.transform;
                    if (t.forward.y < 0.7f) continue;   // 只要朝上的水平面
                    var r = anchor.PlaneRect.Value;
                    rects.Add(new TableRect(t.TransformPoint(new Vector3(r.center.x, r.center.y, 0f)), t.forward, t.right, t.up, r.size * 0.5f));
                }
            }
            int i = TablePlacement.PickTable(rects, head.position, head.forward, _deskInFrontOnly);
            if (i < 0) return false;
            table.Place(TablePlacement.FromRect(rects[i], head.position), TablePlacementSource.MrukTable);
            Debug.Log($"[TablePlacer] MRUK table {i + 1}/{rects.Count} size={rects[i].HalfExtents * 2f} at {table.transform.position}");
            return true;
        }

        /// <summary>
        /// 把牌桌所在房間的牆、地板輪廓與家具量體換成牌桌座標交給 TableFrame（SeatingPlanner 用）。
        /// 房間：包含牌桌的那間（MRUKRoom.IsPositionInRoom，MRUKRoom.cs:843）＞頭所在的那間（MRUK.GetCurrentRoom，MRUK.cs:442）。
        /// 牆：WallAnchors 的 PlaneRect 底邊（本地 XY 是牆面）；輪廓：GetRoomOutline（MRUKRoom.cs:550，地板高度的牆角）；
        /// 量體：VolumeBounds（MRUKAnchor.cs:126，本地座標）的 8 個角。
        /// </summary>
        void RefreshRoom()
        {
            _roomDirty = false;
            if (MRUK.Instance == null || !MRUK.Instance.IsInitialized) return;
            MRUKRoom room = null;
            var probe = table.transform.position + Vector3.up * 0.1f;
            foreach (var r in MRUK.Instance.Rooms)
                if (r != null && r.IsPositionInRoom(probe, false)) { room = r; break; }
            if (room == null) room = MRUK.Instance.GetCurrentRoom();
            if (room == null) return;

            var walls = new List<(Vector3, Vector3)>();
            foreach (var w in room.WallAnchors)
            {
                if (w == null || !w.PlaneRect.HasValue) continue;
                var rect = w.PlaneRect.Value;
                walls.Add((w.transform.TransformPoint(new Vector3(rect.xMin, rect.yMin, 0f)), w.transform.TransformPoint(new Vector3(rect.xMax, rect.yMin, 0f))));
            }
            var volumes = new List<(IReadOnlyList<Vector3>, string)>();
            foreach (var a in room.Anchors)
            {
                if (a == null || !a.VolumeBounds.HasValue) continue;
                var b = a.VolumeBounds.Value;
                var corners = new List<Vector3>(8);
                for (int i = 0; i < 8; i++)
                    corners.Add(a.transform.TransformPoint(new Vector3(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y, i / 4 == 0 ? b.min.z : b.max.z)));
                volumes.Add((corners, a.Label.ToString()));
            }
            var outline = room.GetRoomOutline();
            var felt = new Vector2(0f, TableDimensions.Depth * 0.5f);
            table.SetRoom(RoomObstacles.FromWorld(outline, walls, volumes, table.WorldToTable, felt));
            Debug.Log($"[Table] room: walls={walls.Count} volumes={volumes.Count} (after skipping the desk: {table.Room.VolumeCount}) outline={outline?.Count ?? 0}");
        }

        /// <summary>
        /// 從頭部朝前下方打一條射線到真實環境（不需要房間掃描，只需要 USE_SCENE 權限）。
        /// 只接受「朝上、在視線下 15–75 cm、連續幾次高度一致」的命中，避免打到地板或手。
        /// </summary>
        float _nextRaycastLog;

        void LogRaycast(string message)
        {
            if (Time.unscaledTime < _nextRaycastLog) return;
            _nextRaycastLog = Time.unscaledTime + 5f;
            Debug.Log("[TablePlacer] " + message);
        }

        void TryRaycastPlacement()
        {
            if (_raycaster == null && mixedRealityRoot != null) _raycaster = mixedRealityRoot.GetComponentInChildren<EnvironmentRaycastManager>();
            if (_raycaster == null || !_raycaster.isActiveAndEnabled) return;

            var f = TablePlacement.FlatForward(head.forward);
            var target = head.position + f * TablePlacement.SurfaceProbeForward + Vector3.down * 0.6f;
            var ray = new Ray(head.position, (target - head.position).normalized);
            // PlaceBox：表面要平整、沒有雜物、放得下這個範圍才回 true（EnvironmentRaycastManager.PlaceBox 文件註解），
            // 擋掉大腿、放在桌上的手背這類「朝上但不是桌面」的命中。box 的 z 對齊表面法線、y 對齊 upwards。
            var footprint = new Vector3(PlaceBoxSize.x, PlaceBoxSize.y, 0.01f);
            if (!_raycaster.PlaceBox(ray, footprint, f, out var hit))
            {
                _raycastStreak = 0;
                bool plain = _raycaster.Raycast(ray, out var probe);
                LogRaycast($"PlaceBox failed (status={hit.status}); plain raycast {(plain ? $"hit y={probe.point.y:F3} n={probe.normal}" : $"miss ({probe.status})")}");
                return;
            }

            float below = head.position.y - hit.point.y;
            bool ok = hit.normal.y > 0.9f && below > 0.15f && below < 0.75f;
            if (!ok) { _raycastStreak = 0; LogRaycast($"rejected hit y={hit.point.y:F3} n={hit.normal} below={below:F2}"); return; }

            if (_raycastStreak > 0 && Mathf.Abs(hit.point.y - _raycastHeight) > 0.02f) _raycastStreak = 0;
            _raycastHeight = _raycastStreak == 0 ? hit.point.y : Mathf.Lerp(_raycastHeight, hit.point.y, 0.5f);
            if (++_raycastStreak < RaycastHitsNeeded) return;

            var point = hit.point;
            point.y = _raycastHeight;
            if (Locked) return;
            table.Place(TablePlacement.FromSurfacePoint(point, Vector3.up, head.position, head.forward), TablePlacementSource.EnvironmentRaycast);
            Debug.Log($"[TablePlacer] EnvironmentRaycast surface at y={_raycastHeight:F3} (head y={head.position.y:F3})");
        }
    }
}
