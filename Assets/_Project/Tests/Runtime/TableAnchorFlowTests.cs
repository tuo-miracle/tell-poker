using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Poker.Runtime;
using UnityEngine;

namespace Poker.Tests.Runtime
{
    /// <summary>
    /// 牌桌的空間錨點：放好（或雙手校準）後存起來，下次在同一個房間開啟就放回原位；找不到（換房間、錨點遺失）就走原本的放置流程。
    /// 全部用假的錨點儲存（實機才有 OVRSpatialAnchor）。
    /// </summary>
    public class TableAnchorFlowTests
    {
        sealed class FakeStore : ITableAnchorStore
        {
            public TaskCompletionSource<AnchorLoadResult> Load = new TaskCompletionSource<AnchorLoadResult>();
            public readonly List<IReadOnlyList<Guid>> LoadCalls = new List<IReadOnlyList<Guid>>();
            public readonly List<Pose> Saves = new List<Pose>();
            public readonly List<Guid> Erased = new List<Guid>();
            public Func<Pose, AnchorSaveResult> SaveResult = _ => AnchorSaveResult.Saved(Guid.NewGuid());
            public TaskCompletionSource<AnchorSaveResult> PendingSave;

            public Task<AnchorLoadResult> LoadAsync(IReadOnlyList<Guid> uuids)
            {
                LoadCalls.Add(uuids);
                return Load.Task;
            }

            public Task<AnchorSaveResult> SaveAsync(Pose pose)
            {
                Saves.Add(pose);
                if (PendingSave != null) return PendingSave.Task;
                return Task.FromResult(SaveResult(pose));
            }

            public void Erase(Guid uuid) => Erased.Add(uuid);
        }

        sealed class FakeMemory : IAnchorMemory
        {
            public string Value = "";
            public string Load() => Value;
            public void Save(string value) => Value = value;
        }

        FakeStore _store;
        FakeMemory _memory;
        TableAnchorFlow _flow;
        readonly List<string> _log = new List<string>();
        readonly List<(Pose pose, TablePlacementSource from)> _restored = new List<(Pose, TablePlacementSource)>();

        static readonly Pose DeskPose = new Pose(new Vector3(0.2f, 0.74f, 0.9f), Quaternion.Euler(0f, 20f, 0f));
        static readonly Guid Saved1 = new Guid("11111111-1111-1111-1111-111111111111");
        static readonly Guid Saved2 = new Guid("22222222-2222-2222-2222-222222222222");

        [SetUp]
        public void SetUp()
        {
            _store = new FakeStore();
            _memory = new FakeMemory();
            _log.Clear();
            _restored.Clear();
            _flow = new TableAnchorFlow(_store, _memory, _log.Add);
            _flow.Restore += (p, from) => _restored.Add((p, from));
        }

        bool Logged(string fragment) => _log.Exists(l => l.Contains(fragment));

        /// <summary>重拍用（debug.tell.firstrun）：存過的錨點這次不找，直接走放置（雙手校準手印）；舊錨點留著不刪。</summary>
        [Test]
        public void First_run_replay_skips_saved_anchors_but_keeps_them()
        {
            var saved = TableAnchorFlow.Encode(new[] { (Saved1, TablePlacementSource.HandCalibrated) });
            _memory.Value = saved;
            _flow.Begin(0, ignoreSaved: true);
            Assert.That(_flow.State, Is.EqualTo(TableAnchorState.Placement));
            Assert.That(_flow.HoldsPlacement, Is.False);
            Assert.That(_store.LoadCalls, Is.Empty, "saved anchors not looked for");
            Assert.That(_memory.Value, Is.EqualTo(saved), "saved anchors kept");
            Assert.That(Logged("first-run replay"), Is.True);
        }

        [TestCase("1", "", true, "1")]
        [TestCase("1", "1", false, "1")]
        [TestCase("2", "1", true, "2")]
        [TestCase("0", "1", false, "")]
        [TestCase("", "1", false, "")]
        [TestCase(" ", "", false, "")]
        public void First_run_replay_fires_once_per_value(string prop, string consumed, bool fires, string remembered)
        {
            Assert.That(FirstRunReplay.Decide(prop, consumed, out var next), Is.EqualTo(fires));
            Assert.That(next, Is.EqualTo(remembered));
        }

        [Test]
        public void Without_a_saved_anchor_it_goes_straight_to_placement()
        {
            _flow.Begin(0);
            Assert.That(_flow.State, Is.EqualTo(TableAnchorState.Placement));
            Assert.That(_flow.HoldsPlacement, Is.False);
            Assert.That(_store.LoadCalls, Is.Empty);
        }

        [Test]
        public void Saved_anchor_found_restores_the_table_exactly_and_logs_it()
        {
            _memory.Value = TableAnchorFlow.Encode(new[] { (Saved1, TablePlacementSource.HandCalibrated) });
            _flow.Begin(0);
            Assert.That(_flow.State, Is.EqualTo(TableAnchorState.Restoring));
            Assert.That(_flow.HoldsPlacement, Is.True, "找錨點的時候先不要讓 MRUK／射線把桌子搬走");
            Assert.That(_store.LoadCalls[0], Is.EqualTo(new[] { Saved1 }));

            _flow.Tick(0.5);
            Assert.That(_restored, Is.Empty);
            _store.Load.SetResult(AnchorLoadResult.Found(Saved1, DeskPose));
            _flow.Tick(1.0);

            Assert.That(_flow.State, Is.EqualTo(TableAnchorState.Restored));
            Assert.That(_flow.HoldsPlacement, Is.False);
            Assert.That(_restored.Count, Is.EqualTo(1));
            Assert.That(Vector3.Distance(_restored[0].pose.position, DeskPose.position), Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(_restored[0].pose.rotation, DeskPose.rotation), Is.LessThan(0.01f));
            Assert.That(_restored[0].from, Is.EqualTo(TablePlacementSource.HandCalibrated));
            Assert.That(_flow.ActiveUuid, Is.EqualTo(Saved1));
            Assert.That(Logged("[Table] restored from anchor " + Saved1), Is.True, string.Join("\n", _log));
        }

        [Test]
        public void Restored_table_is_not_saved_again()
        {
            _memory.Value = TableAnchorFlow.Encode(new[] { (Saved1, TablePlacementSource.MrukTable) });
            _flow.Begin(0);
            _store.Load.SetResult(AnchorLoadResult.Found(Saved1, DeskPose));
            _flow.Tick(0.1);
            _flow.OnTablePlaced(DeskPose, TablePlacementSource.Restored, 0.1);
            _flow.Tick(10);
            Assert.That(_store.Saves, Is.Empty);
        }

        [Test]
        public void Anchor_not_found_falls_back_to_placement_and_saves_the_new_table()
        {
            _memory.Value = TableAnchorFlow.Encode(new[] { (Saved1, TablePlacementSource.MrukTable) });
            _flow.Begin(0);
            _store.Load.SetResult(AnchorLoadResult.NotFound());
            _flow.Tick(0.2);
            Assert.That(_flow.State, Is.EqualTo(TableAnchorState.Placement));
            Assert.That(_flow.HoldsPlacement, Is.False);
            Assert.That(_restored, Is.Empty);
            Assert.That(Logged("[Table] anchor not found -> placement"), Is.True, string.Join("\n", _log));

            // 這個房間的新牌桌：存成新錨點，舊的（別的房間）留著
            _flow.OnTablePlaced(DeskPose, TablePlacementSource.MrukTable, 1.0);
            _flow.Tick(1.0 + TableAnchorFlow.SaveDelay + 0.01);
            Assert.That(_store.Saves.Count, Is.EqualTo(1));
            var entries = TableAnchorFlow.Decode(_memory.Value);
            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(entries[1].uuid, Is.EqualTo(Saved1), "別的房間的錨點留著，回到那個房間還能找回來");
            Assert.That(_store.Erased, Is.Empty);
        }

        [Test]
        public void Restore_that_takes_too_long_gives_up_and_ignores_a_late_answer()
        {
            _memory.Value = TableAnchorFlow.Encode(new[] { (Saved1, TablePlacementSource.MrukTable) });
            _flow.Begin(0);
            _flow.Tick(TableAnchorFlow.RestoreTimeout + 0.1);
            Assert.That(_flow.State, Is.EqualTo(TableAnchorState.Placement));
            Assert.That(Logged("[Table] anchor not found -> placement"), Is.True);
            _store.Load.SetResult(AnchorLoadResult.Found(Saved1, DeskPose));
            _flow.Tick(TableAnchorFlow.RestoreTimeout + 1);
            Assert.That(_restored, Is.Empty, "已經走放置流程了，晚到的結果不能再把桌子搬走");
        }

        [Test]
        public void Permission_denied_falls_back_and_stops_saving()
        {
            _memory.Value = TableAnchorFlow.Encode(new[] { (Saved1, TablePlacementSource.MrukTable) });
            _flow.Begin(0);
            _store.Load.SetResult(AnchorLoadResult.PermissionDenied("FailurePermissionInsufficient"));
            _flow.Tick(0.1);
            Assert.That(_flow.State, Is.EqualTo(TableAnchorState.Placement));
            Assert.That(Logged("permission"), Is.True, string.Join("\n", _log));
            _flow.OnTablePlaced(DeskPose, TablePlacementSource.HandCalibrated, 1);
            _flow.Tick(10);
            Assert.That(_store.Saves, Is.Empty);
        }

        [Test]
        public void Load_failure_or_exception_falls_back_to_placement()
        {
            _memory.Value = TableAnchorFlow.Encode(new[] { (Saved1, TablePlacementSource.MrukTable) });
            _flow.Begin(0);
            _store.Load.SetException(new InvalidOperationException("boom"));
            _flow.Tick(0.1);
            Assert.That(_flow.State, Is.EqualTo(TableAnchorState.Placement));
            Assert.That(Logged("-> placement"), Is.True);
        }

        [Test]
        public void Calibrating_while_the_restore_is_pending_wins()
        {
            _memory.Value = TableAnchorFlow.Encode(new[] { (Saved1, TablePlacementSource.MrukTable) });
            _flow.Begin(0);
            _flow.OnTablePlaced(DeskPose, TablePlacementSource.HandCalibrated, 0.5);
            Assert.That(_flow.State, Is.EqualTo(TableAnchorState.Placement));
            Assert.That(_flow.HoldsPlacement, Is.False);
            _store.Load.SetResult(AnchorLoadResult.Found(Saved1, new Pose(Vector3.one, Quaternion.identity)));
            _flow.Tick(0.6);
            Assert.That(_restored, Is.Empty, "玩家親手校準的桌子優先");
            _flow.Tick(0.5 + TableAnchorFlow.SaveDelay + 0.01);
            Assert.That(_store.Saves.Count, Is.EqualTo(1));
        }

        [Test]
        public void Placement_is_saved_once_it_settles_and_virtual_tables_are_not_saved()
        {
            _flow.Begin(0);
            _flow.OnTablePlaced(DeskPose, TablePlacementSource.Virtual, 0);
            _flow.Tick(10);
            Assert.That(_store.Saves, Is.Empty, "虛擬桌跟著頭放，不是房間裡的東西");

            _flow.OnTablePlaced(DeskPose, TablePlacementSource.EnvironmentRaycast, 10);
            _flow.Tick(10 + TableAnchorFlow.SaveDelay * 0.5);
            Assert.That(_store.Saves, Is.Empty, "剛放好先等一下（MRUK 可能馬上又改）");
            var later = new Pose(DeskPose.position + Vector3.right * 0.1f, DeskPose.rotation);
            _flow.OnTablePlaced(later, TablePlacementSource.MrukTable, 10 + TableAnchorFlow.SaveDelay * 0.5);
            _flow.Tick(10 + TableAnchorFlow.SaveDelay * 1.6);
            Assert.That(_store.Saves.Count, Is.EqualTo(1));
            Assert.That(Vector3.Distance(_store.Saves[0].position, later.position), Is.LessThan(1e-5f), "存最後的位置");
            var entries = TableAnchorFlow.Decode(_memory.Value);
            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].source, Is.EqualTo(TablePlacementSource.MrukTable));
            Assert.That(_flow.ActiveUuid, Is.EqualTo(entries[0].uuid));
            Assert.That(Logged("[Table] anchor saved " + entries[0].uuid), Is.True, string.Join("\n", _log));
        }

        [Test]
        public void Recalibrating_replaces_the_active_anchor_and_erases_the_old_one()
        {
            _memory.Value = TableAnchorFlow.Encode(new[] { (Saved1, TablePlacementSource.MrukTable), (Saved2, TablePlacementSource.HandCalibrated) });
            _flow.Begin(0);
            _store.Load.SetResult(AnchorLoadResult.Found(Saved1, DeskPose));
            _flow.Tick(0.1);
            var moved = new Pose(DeskPose.position + Vector3.forward * 0.05f, DeskPose.rotation);
            _flow.OnTablePlaced(moved, TablePlacementSource.HandCalibrated, 1);
            _flow.Tick(1 + TableAnchorFlow.SaveDelay + 0.01);
            Assert.That(_store.Saves.Count, Is.EqualTo(1));
            Assert.That(_store.Erased, Is.EqualTo(new[] { Saved1 }));
            var entries = TableAnchorFlow.Decode(_memory.Value);
            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(entries[0].uuid, Is.Not.EqualTo(Saved1));
            Assert.That(entries[0].source, Is.EqualTo(TablePlacementSource.HandCalibrated));
            Assert.That(entries[1].uuid, Is.EqualTo(Saved2));
        }

        [Test]
        public void Rejected_restore_keeps_the_old_anchor_for_later_and_saves_the_new_table_separately()
        {
            // 放回來的桌子不在玩家面前（TableRestoreCheck）：這次不用，但不刪（可能是另一個位置／房間的桌子）
            _memory.Value = TableAnchorFlow.Encode(new[] { (Saved1, TablePlacementSource.HandCalibrated) });
            _flow.Begin(0);
            _store.Load.SetResult(AnchorLoadResult.Found(Saved1, DeskPose));
            _flow.Tick(0.1);
            _flow.DetachActive("restore rejected");
            Assert.That(_flow.State, Is.EqualTo(TableAnchorState.Placement));
            Assert.That(_flow.ActiveUuid, Is.EqualTo(Guid.Empty));
            Assert.That(_flow.InSync, Is.False, "recenter must not follow the rejected anchor");
            Assert.That(Logged("[Table] keeping anchor " + Saved1), Is.True, string.Join("\n", _log));

            var elsewhere = new Pose(new Vector3(-1.5f, 0.7f, 0.2f), Quaternion.Euler(0f, -90f, 0f));
            _flow.OnTablePlaced(elsewhere, TablePlacementSource.HandCalibrated, 1);
            _flow.Tick(1 + TableAnchorFlow.SaveDelay + 0.01);
            Assert.That(_store.Saves.Count, Is.EqualTo(1));
            Assert.That(_store.Erased, Is.Empty);
            var entries = TableAnchorFlow.Decode(_memory.Value);
            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(entries[0].uuid, Is.EqualTo(_flow.ActiveUuid), "new table first (most recent)");
            Assert.That(entries[1].uuid, Is.EqualTo(Saved1));
        }

        [Test]
        public void Detaching_without_an_active_anchor_does_nothing()
        {
            _flow.Begin(0);
            _flow.DetachActive("relocated far");
            Assert.That(_flow.State, Is.EqualTo(TableAnchorState.Placement));
            Assert.That(_log.Exists(l => l.Contains("keeping anchor")), Is.False);
        }

        [Test]
        public void Recalibrating_in_the_same_spot_keeps_the_anchor()
        {
            _memory.Value = TableAnchorFlow.Encode(new[] { (Saved1, TablePlacementSource.HandCalibrated) });
            _flow.Begin(0);
            _store.Load.SetResult(AnchorLoadResult.Found(Saved1, DeskPose));
            _flow.Tick(0.1);
            var same = new Pose(DeskPose.position + Vector3.up * 0.005f, DeskPose.rotation);
            _flow.OnTablePlaced(same, TablePlacementSource.HandCalibrated, 1);
            _flow.Tick(10);
            Assert.That(_store.Saves, Is.Empty);
        }

        [Test]
        public void Remembers_at_most_three_rooms_and_erases_the_oldest()
        {
            var g3 = Guid.NewGuid();
            _memory.Value = TableAnchorFlow.Encode(new[] { (Saved1, TablePlacementSource.MrukTable), (Saved2, TablePlacementSource.MrukTable), (g3, TablePlacementSource.MrukTable) });
            _flow.Begin(0);
            Assert.That(_store.LoadCalls[0], Is.EqualTo(new[] { Saved1, Saved2, g3 }), "依最近使用的順序找");
            _store.Load.SetResult(AnchorLoadResult.NotFound());
            _flow.Tick(0.1);
            _flow.OnTablePlaced(DeskPose, TablePlacementSource.MrukTable, 1);
            _flow.Tick(1 + TableAnchorFlow.SaveDelay + 0.01);
            var entries = TableAnchorFlow.Decode(_memory.Value);
            Assert.That(entries.Count, Is.EqualTo(TableAnchorFlow.MaxRooms));
            Assert.That(entries.ConvertAll(e => e.uuid), Has.No.Member(g3));
            Assert.That(_store.Erased, Is.EqualTo(new[] { g3 }));
        }

        [Test]
        public void Restored_room_moves_to_the_front_of_the_list()
        {
            _memory.Value = TableAnchorFlow.Encode(new[] { (Saved1, TablePlacementSource.MrukTable), (Saved2, TablePlacementSource.HandCalibrated) });
            _flow.Begin(0);
            _store.Load.SetResult(AnchorLoadResult.Found(Saved2, DeskPose));
            _flow.Tick(0.1);
            var entries = TableAnchorFlow.Decode(_memory.Value);
            Assert.That(entries[0].uuid, Is.EqualTo(Saved2));
            Assert.That(entries[1].uuid, Is.EqualTo(Saved1));
            Assert.That(_restored[0].from, Is.EqualTo(TablePlacementSource.HandCalibrated));
        }

        [Test]
        public void Save_failure_is_logged_and_nothing_is_remembered()
        {
            _store.SaveResult = _ => AnchorSaveResult.Failed("FailureInsufficientView");
            _flow.Begin(0);
            _flow.OnTablePlaced(DeskPose, TablePlacementSource.HandCalibrated, 0);
            _flow.Tick(TableAnchorFlow.SaveDelay + 0.01);
            Assert.That(_memory.Value, Is.Empty);
            Assert.That(Logged("[Table] anchor save failed (FailureInsufficientView)"), Is.True, string.Join("\n", _log));
        }

        [Test]
        public void A_save_in_flight_does_not_start_a_second_one()
        {
            _store.PendingSave = new TaskCompletionSource<AnchorSaveResult>();
            _flow.Begin(0);
            _flow.OnTablePlaced(DeskPose, TablePlacementSource.HandCalibrated, 0);
            _flow.Tick(TableAnchorFlow.SaveDelay + 0.01);
            _flow.Tick(TableAnchorFlow.SaveDelay + 1);
            Assert.That(_store.Saves.Count, Is.EqualTo(1));
            _store.PendingSave.SetResult(AnchorSaveResult.Saved(Saved2));
            _flow.Tick(TableAnchorFlow.SaveDelay + 2);
            Assert.That(_flow.ActiveUuid, Is.EqualTo(Saved2));
        }

        [Test]
        public void InSync_only_when_the_live_anchor_matches_the_current_table()
        {
            // recenter 後 TablePlacer 用錨點把桌子放回原位；桌子剛換位置、新錨點還沒存好時不能用舊錨點
            _memory.Value = TableAnchorFlow.Encode(new[] { (Saved1, TablePlacementSource.HandCalibrated) });
            _flow.Begin(0);
            Assert.That(_flow.InSync, Is.False);
            _store.Load.SetResult(AnchorLoadResult.Found(Saved1, DeskPose));
            _flow.Tick(0.1);
            Assert.That(_flow.InSync, Is.True);
            _flow.OnTablePlaced(new Pose(Vector3.one, Quaternion.identity), TablePlacementSource.HandCalibrated, 1);
            Assert.That(_flow.InSync, Is.False, "新位置還沒存");
            _flow.Tick(1 + TableAnchorFlow.SaveDelay + 0.01);
            Assert.That(_flow.InSync, Is.True);
        }

        [Test]
        public void Following_the_anchor_after_a_recenter_is_not_a_new_table()
        {
            // 系統 recenter 後錨點的世界姿勢換了，TablePlacer 把桌子對齊到錨點：同一張桌子，不要重存、也不要失去同步
            _flow.Begin(0);
            _flow.OnTablePlaced(DeskPose, TablePlacementSource.HandCalibrated, 0);
            _flow.Tick(TableAnchorFlow.SaveDelay + 0.01);
            Assert.That(_store.Saves.Count, Is.EqualTo(1));
            var shifted = new Pose(DeskPose.position + new Vector3(0.4f, 0f, -0.3f), DeskPose.rotation * Quaternion.Euler(0f, 30f, 0f));
            _flow.NoteAnchorMoved(shifted);
            _flow.OnTablePlaced(shifted, TablePlacementSource.HandCalibrated, 5);
            Assert.That(_flow.InSync, Is.True);
            _flow.Tick(20);
            Assert.That(_store.Saves.Count, Is.EqualTo(1));
        }

        [Test]
        public void Garbage_in_memory_is_ignored()
        {
            _memory.Value = "not-a-guid|MrukTable;;" + Saved1 + "|Nope";
            var entries = TableAnchorFlow.Decode(_memory.Value);
            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].uuid, Is.EqualTo(Saved1));
        }
    }
}
