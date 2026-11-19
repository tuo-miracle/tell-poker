using System;
using System.Collections;
using NUnit.Framework;
using Poker.Gestures;
using Poker.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Poker.Tests
{
    public class HandSurfaceCalibrationTests
    {
        /// <summary>直接設定雙手的假資料來源（牌桌座標）。</summary>
        sealed class FakeHands : IHandsFrameSource
        {
            public HandsFrame Current { get; } = new HandsFrame();
            public event Action<HandsFrame> FrameUpdated;
            /// <summary>只追蹤右手，食指指尖在 tip（牌桌座標）；用來點按鈕。</summary>
            public void PushTip(double time, Vector3 tip)
            {
                Current.Time = time;
                Current.Left.Tracked = false;
                Fill(Current.Right, new System.Numerics.Vector3(tip.x, tip.y + 0.05f, tip.z - 0.08f));
                Current.Right.Positions[(int)HandJoint.IndexTip] = new System.Numerics.Vector3(tip.x, tip.y, tip.z);
                FrameUpdated?.Invoke(Current);
            }

            public void Push(double time, float palmY)
            {
                Current.Time = time;
                Fill(Current.Left, new System.Numerics.Vector3(-0.15f, palmY, 0.2f));
                Fill(Current.Right, new System.Numerics.Vector3(0.15f, palmY, 0.2f));
                FrameUpdated?.Invoke(Current);
            }
            /// <summary>雙手平放、左右各 15 cm，整組手相對目前牌桌轉 yawDeg（含手腕與中指指尖，所以有手指方向）。</summary>
            public void PushTurned(double time, float palmY, float yawDeg, float halfSpacing = 0.15f, Vector3? at = null)
            {
                Current.Time = time;
                var q = Quaternion.Euler(0f, yawDeg, 0f);
                var center = at.HasValue ? new Vector3(at.Value.x, palmY, at.Value.z) : new Vector3(0f, palmY, 0.2f);
                FillTurned(Current.Left, center + q * new Vector3(-halfSpacing, 0f, 0f), q);
                FillTurned(Current.Right, center + q * new Vector3(halfSpacing, 0f, 0f), q);
                FrameUpdated?.Invoke(Current);
            }
            static void FillTurned(HandPose h, Vector3 palm, Quaternion q)
            {
                Fill(h, N(palm));
                var r = new System.Numerics.Quaternion(q.x, q.y, q.z, q.w);
                for (int j = 0; j < HandPose.JointCount; j++) h.Rotations[j] = r;
                h.Positions[(int)HandJoint.Wrist] = N(palm + q * new Vector3(0f, 0.01f, -0.075f));
                h.Positions[(int)HandJoint.MiddleTip] = N(palm + q * new Vector3(0f, 0f, 0.1f));
            }
            static System.Numerics.Vector3 N(Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);

            static void Fill(HandPose h, System.Numerics.Vector3 p)
            {
                h.Tracked = true;
                h.Confidence = 1f;
                for (int j = 0; j < HandPose.JointCount; j++) { h.Positions[j] = p; h.Rotations[j] = System.Numerics.Quaternion.Identity; }
            }
        }

        [UnityTest]
        public IEnumerator Resting_both_hands_on_the_real_desk_moves_the_table_to_that_height()
        {
            var go = new GameObject("Table");
            var table = go.AddComponent<TableFrame>();
            table.Place(new Pose(new Vector3(0f, 0.9f, 0.5f), Quaternion.identity), TablePlacementSource.Virtual);
            var cal = go.AddComponent<HandSurfaceCalibration>();
            var hands = new FakeHands();
            cal.Init(table, hands);

            // 真實桌面比虛擬桌低 8.3 cm（加上手掌厚度）
            float palmY = -0.083f + SurfaceCalibrator.PalmThickness;
            for (double t = 0; t < SurfaceCalibrator.DwellSeconds + 0.3; t += 1.0 / 72.0)
            {
                hands.Push(t, palmY);
                yield return null;
            }

            Assert.That(table.Source, Is.EqualTo(TablePlacementSource.HandCalibrated));
            Assert.That(table.transform.position.y, Is.EqualTo(0.9f - 0.083f).Within(0.003f));
            Assert.That(cal.PromptVisible, Is.False, "hand prints disappear after calibration");
            UnityEngine.Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator Recalibrate_button_lets_the_player_place_the_table_again()
        {
            var go = new GameObject("Table");
            var table = go.AddComponent<TableFrame>();
            table.Place(new Pose(new Vector3(0f, 0.9f, 0.5f), Quaternion.identity), TablePlacementSource.Virtual);
            var cal = go.AddComponent<HandSurfaceCalibration>();
            var hands = new FakeHands();
            cal.Init(table, hands);
            double t = 0;
            for (; t < SurfaceCalibrator.DwellSeconds + 0.3; t += 1.0 / 72.0) { hands.Push(t, -0.05f); yield return null; }
            Assert.That(cal.PromptVisible, Is.False);
            float firstY = table.transform.position.y;

            // 食指從按鈕上方往下點，再抬起
            Vector3 b = cal.RecalibrateButtonCenter;
            for (int i = 0; i < 20; i++) { hands.PushTip(t += 1.0 / 72.0, b + Vector3.up * Mathf.Lerp(0.08f, 0.005f, i / 19f)); yield return null; }
            for (int i = 0; i < 10; i++) { hands.PushTip(t += 1.0 / 72.0, b + Vector3.up * 0.08f); yield return null; }
            Assert.That(cal.PromptVisible, Is.True, "pressing the button shows the hand prints again");

            float palmY = 0.04f + SurfaceCalibrator.PalmThickness;   // 這次手放在比目前牌桌高 4 cm 的地方
            for (double e = t + SurfaceCalibrator.DwellSeconds + 0.3; t < e; t += 1.0 / 72.0) { hands.Push(t, palmY); yield return null; }
            Assert.That(cal.PromptVisible, Is.False);
            Assert.That(table.transform.position.y, Is.EqualTo(firstY + 0.04f).Within(0.003f));
            UnityEngine.Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator Prompt_shows_until_calibrated()
        {
            var go = new GameObject("Table");
            var table = go.AddComponent<TableFrame>();
            table.Place(new Pose(new Vector3(0f, 0.9f, 0.5f), Quaternion.identity), TablePlacementSource.Virtual);
            var cal = go.AddComponent<HandSurfaceCalibration>();
            cal.Init(table, new FakeHands());
            yield return null;
            Assert.That(cal.PromptVisible, Is.True);
            UnityEngine.Object.Destroy(go);
        }
    

        static float WorldYaw(TableFrame t) => t.transform.rotation.eulerAngles.y;

        [UnityTest]
        public IEnumerator Table_placed_with_a_wrong_yaw_turns_to_face_the_way_the_flat_hands_face()
        {
            var go = new GameObject("Table");
            var table = go.AddComponent<TableFrame>();
            // 開機時頭偏了 30°：牌桌歪向一邊
            table.Place(new Pose(new Vector3(0f, 0.9f, 0.5f), Quaternion.Euler(0f, 30f, 0f)), TablePlacementSource.Virtual);
            var cal = go.AddComponent<HandSurfaceCalibration>();
            var hands = new FakeHands();
            cal.Init(table, hands);

            // 玩家雙手其實朝世界正前方擺：相對牌桌是 −30°
            float palmY = -0.05f + SurfaceCalibrator.PalmThickness;
            Vector3 handsCenterWorld = table.TableToWorld(new Vector3(0f, -0.05f, 0.2f));
            double t = 0;
            for (; t < SurfaceCalibrator.DwellSeconds + 0.3; t += 1.0 / 72.0) { hands.PushTurned(t, palmY, -30f); yield return null; }

            Assert.That(cal.IsCalibrated, Is.True);
            Assert.That(Mathf.DeltaAngle(WorldYaw(table), 0f), Is.EqualTo(0f).Within(1f), "table now faces the way the hands face");
            Assert.That(Vector3.Angle(table.transform.up, Vector3.up), Is.LessThan(0.1f), "table stays level");
            Vector3 expectedOrigin = handsCenterWorld - Vector3.forward * HandSurfaceCalibration.HandsToEdge;
            Assert.That(Vector3.Distance(table.transform.position, expectedOrigin), Is.LessThan(0.005f), "player's edge sits at the hands");

            // 重新校準按鈕仍然能用：這次手相對（已轉正的）牌桌轉 +10°
            Vector3 b = cal.RecalibrateButtonCenter;
            for (int i = 0; i < 20; i++) { hands.PushTip(t += 1.0 / 72.0, b + Vector3.up * Mathf.Lerp(0.08f, 0.005f, i / 19f)); yield return null; }
            for (int i = 0; i < 10; i++) { hands.PushTip(t += 1.0 / 72.0, b + Vector3.up * 0.08f); yield return null; }
            Assert.That(cal.PromptVisible, Is.True);
            for (double e = t + SurfaceCalibrator.DwellSeconds + 0.3; t < e; t += 1.0 / 72.0) { hands.PushTurned(t, SurfaceCalibrator.PalmThickness, 10f); yield return null; }
            Assert.That(cal.IsCalibrated, Is.True);
            Assert.That(Mathf.DeltaAngle(WorldYaw(table), 10f), Is.EqualTo(0f).Within(1f));
            UnityEngine.Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator Flat_palms_end_up_on_either_side_of_the_hole_cards()
        {
            var go = new GameObject("Table");
            var table = go.AddComponent<TableFrame>();
            table.Place(new Pose(new Vector3(0f, 0.9f, 0.5f), Quaternion.identity), TablePlacementSource.Virtual);
            var cal = go.AddComponent<HandSurfaceCalibration>();
            var hands = new FakeHands();
            cal.Init(table, hands);
            Vector3 handsCenterWorld = table.TableToWorld(new Vector3(0f, -0.05f, 0.2f));
            for (double t = 0; t < SurfaceCalibrator.DwellSeconds + 0.3; t += 1.0 / 72.0) { hands.PushTurned(t, -0.05f + SurfaceCalibrator.PalmThickness, 0f); yield return null; }
            Assert.That(cal.IsCalibrated, Is.True);

            // 校準後，手牌中心就在兩手掌中間（使用者：「雙手掌的位置就是我手牌的兩側」）
            var layout = new TableLayout();
            var cards = table.TableToWorld(new Vector3(layout.CardsCenter.X, 0f, layout.CardsCenter.Z));
            Assert.That(new Vector2(cards.x - handsCenterWorld.x, cards.z - handsCenterWorld.z).magnitude, Is.LessThan(0.005f), "hole cards sit between the palms");

            // 校準提示的手印緊貼手牌兩側：不壓到牌，內緣離牌邊不超過 3 cm
            float cardOuterX = layout.CardRightCenter.X + layout.CardSize.X * 0.5f;
            foreach (var name in new[] { "LeftPrint", "RightPrint" })
            {
                var print = go.transform.Find("HandPrints/" + name);
                Assert.That(print, Is.Not.Null, name);
                float inner = Mathf.Abs(print.localPosition.x) - print.localScale.x * 0.5f;
                Assert.That(inner, Is.GreaterThanOrEqualTo(cardOuterX), name + " does not cover the cards");
                Assert.That(inner - cardOuterX, Is.LessThanOrEqualTo(0.03f), name + " hugs the cards");
                Assert.That(print.localPosition.z, Is.EqualTo(layout.CardsCenter.Z).Within(0.005f), name + " level with the cards");
            }
            UnityEngine.Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator Hands_too_close_together_keep_the_old_yaw_but_still_set_the_height()
        {
            var go = new GameObject("Table");
            var table = go.AddComponent<TableFrame>();
            table.Place(new Pose(new Vector3(0f, 0.9f, 0.5f), Quaternion.Euler(0f, 30f, 0f)), TablePlacementSource.Virtual);
            var cal = go.AddComponent<HandSurfaceCalibration>();
            var hands = new FakeHands();
            cal.Init(table, hands);
            for (double t = 0; t < SurfaceCalibrator.DwellSeconds + 0.3; t += 1.0 / 72.0)
            {
                hands.PushTurned(t, -0.05f + SurfaceCalibrator.PalmThickness, -30f, halfSpacing: 0.02f);   // 兩手相距 4 cm
                yield return null;
            }
            Assert.That(cal.IsCalibrated, Is.True);
            Assert.That(Mathf.DeltaAngle(WorldYaw(table), 30f), Is.EqualTo(0f).Within(0.1f));
            Assert.That(table.transform.position.y, Is.EqualTo(0.85f).Within(0.003f));
            UnityEngine.Object.Destroy(go);
        }
    
        // ---------- 換位置校準（鎖定時不用提示：雙手平放在明顯不同的地方 2 s） ----------

        static (GameObject go, TableFrame table, HandSurfaceCalibration cal, FakeHands hands) LockedTable()
        {
            var go = new GameObject("Table");
            var table = go.AddComponent<TableFrame>();
            table.Place(new Pose(new Vector3(0f, 0.9f, 0.5f), Quaternion.identity), TablePlacementSource.Virtual);
            var cal = go.AddComponent<HandSurfaceCalibration>();
            var hands = new FakeHands();
            cal.Init(table, hands);
            table.Place(new Pose(new Vector3(0f, 0.9f, 0.5f), Quaternion.identity), TablePlacementSource.Restored, TablePlacementSource.HandCalibrated);
            // 頭：人已經走到新位置（雙手後方 45 cm、眼睛比新桌面高 45 cm）；不靠 Camera.main（別的測試可能留下場景相機）
            var head = new GameObject("Head").transform;
            head.SetParent(go.transform, false);
            head.position = new Vector3(NewSpot.x + 0.45f, 0.8f + 0.45f, 0.5f + NewSpot.z);
            cal.Head = head;
            return (go, table, cal, hands);
        }

        /// <summary>另一個地方：往左 1 m、往前 0.3 m、低 10 cm，人向左轉 90°。</summary>
        static readonly Vector3 NewSpot = new Vector3(-1.0f, 0f, 0.5f);
        const float NewSpotPalmY = -0.1f + SurfaceCalibrator.PalmThickness;

        [Test]
        public void Locked_table_moves_to_where_both_palms_rest_flat_for_2_seconds_without_a_prompt()
        {
            var (go, table, cal, hands) = LockedTable();
            Assert.That(cal.PromptVisible, Is.False);
            var events = new System.Collections.Generic.List<(float dist, float yaw)>();
            cal.Relocated += (d, y) => events.Add((d, y));
            double t = 0;
            for (; t < 1.5; t += 1.0 / 72.0) hands.PushTurned(t, NewSpotPalmY, -90f, at: NewSpot);
            Assert.That(table.Source, Is.EqualTo(TablePlacementSource.Restored), "1.5 s is not enough (prompted is 1 s, this is 2 s)");
            Assert.That(table.transform.position, Is.EqualTo(new Vector3(0f, 0.9f, 0.5f)));

            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"^\[Table\] recalibrated at new spot \(dist=1\.\d\dm yaw=-9\d°\)"));
            for (; t < 2.3; t += 1.0 / 72.0) hands.PushTurned(t, NewSpotPalmY, -90f, at: NewSpot);

            Assert.That(table.Source, Is.EqualTo(TablePlacementSource.HandCalibrated));
            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(Mathf.DeltaAngle(WorldYaw(table), -90f), Is.EqualTo(0f).Within(1f));
            Assert.That(table.transform.position.y, Is.EqualTo(0.9f - 0.1f).Within(0.003f));
            // 同提示校準：雙手在新的手牌區兩側（手印的位置）
            var handsCenterWorld = new Vector3(NewSpot.x, 0f, NewSpot.z) + new Vector3(0f, 0.9f, 0.5f);
            var local = table.WorldToTable(handsCenterWorld);
            Assert.That(local.x, Is.EqualTo(0f).Within(0.01f));
            Assert.That(local.z, Is.EqualTo(HandSurfaceCalibration.HandsToEdge).Within(0.01f));
            Assert.That(cal.PromptVisible, Is.False);

            // 手還放著：不會再觸發一次（新座標下就是自己的手牌區）
            for (double e = t + 3; t < e; t += 1.0 / 72.0) hands.PushTurned(t, 0f + SurfaceCalibrator.PalmThickness, 0f, at: new Vector3(0f, 0f, HandSurfaceCalibration.HandsToEdge));
            Assert.That(events.Count, Is.EqualTo(1));
            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void Locked_table_ignores_palms_resting_on_its_own_cards()
        {
            var (go, table, cal, hands) = LockedTable();
            cal.Head.position = new Vector3(0f, 0.9f + 0.45f, 0.5f - 0.3f);   // 坐在這張桌子前
            for (double t = 0; t < 6; t += 1.0 / 72.0) hands.PushTurned(t, SurfaceCalibrator.PalmThickness, 0f, at: new Vector3(0f, 0f, HandSurfaceCalibration.HandsToEdge));
            Assert.That(table.Source, Is.EqualTo(TablePlacementSource.Restored));
            Assert.That(table.transform.position, Is.EqualTo(new Vector3(0f, 0.9f, 0.5f)));
            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void Unlocked_table_shows_the_prompt_and_does_not_relocate_by_itself()
        {
            var go = new GameObject("Table");
            var table = go.AddComponent<TableFrame>();
            table.Place(new Pose(new Vector3(0f, 0.9f, 0.5f), Quaternion.identity), TablePlacementSource.Virtual);
            var cal = go.AddComponent<HandSurfaceCalibration>();
            var hands = new FakeHands();
            cal.Init(table, hands);
            for (double t = 0; t < 3; t += 1.0 / 72.0) hands.PushTurned(t, NewSpotPalmY, -90f, at: NewSpot);
            Assert.That(table.Source, Is.EqualTo(TablePlacementSource.Virtual));
            Assert.That(cal.PromptVisible, Is.True);
            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void Calibrated_table_placed_virtual_again_shows_the_prompt()
        {
            var (go, table, cal, _) = LockedTable();
            Assert.That(cal.PromptVisible, Is.False);
            table.Place(new Pose(new Vector3(0f, 0.8f, 0.2f), Quaternion.identity), TablePlacementSource.Virtual);
            Assert.That(cal.PromptVisible, Is.True, "no desk under a virtual table: ask for the two-hands calibration");
            UnityEngine.Object.DestroyImmediate(go);
        }

        sealed class SavingStore : ITableAnchorStore
        {
            public readonly System.Collections.Generic.List<Pose> Saves = new System.Collections.Generic.List<Pose>();
            public System.Threading.Tasks.Task<AnchorLoadResult> LoadAsync(System.Collections.Generic.IReadOnlyList<Guid> uuids) =>
                System.Threading.Tasks.Task.FromResult(AnchorLoadResult.Found(uuids[0], new Pose(new Vector3(0f, 0.9f, 0.5f), Quaternion.identity)));
            public System.Threading.Tasks.Task<AnchorSaveResult> SaveAsync(Pose pose) { Saves.Add(pose); return System.Threading.Tasks.Task.FromResult(AnchorSaveResult.Saved(Guid.NewGuid())); }
            public void Erase(Guid uuid) { }
        }

        sealed class Memory : IAnchorMemory { public string Value; public string Load() => Value; public void Save(string v) => Value = v; }

        [Test]
        public void Relocated_table_is_saved_as_the_new_anchor()
        {
            var (go, table, cal, hands) = LockedTable();
            var store = new SavingStore();
            var memory = new Memory { Value = TableAnchorFlow.Encode(new[] { (Guid.NewGuid(), TablePlacementSource.HandCalibrated) }) };
            var flow = new TableAnchorFlow(store, memory, null);
            double now = 0;
            table.Placed += f => flow.OnTablePlaced(f.WorldPose, f.Source, now);   // 同 TablePlacer.OnTablePlaced
            flow.Begin(0);
            flow.Tick(0.01);
            Assert.That(flow.State, Is.EqualTo(TableAnchorState.Restored));
            for (double t = 0; t < 2.3; t += 1.0 / 72.0) { now = t; hands.PushTurned(t, NewSpotPalmY, -90f, at: NewSpot); flow.Tick(t); }
            Assert.That(table.Source, Is.EqualTo(TablePlacementSource.HandCalibrated));
            flow.Tick(now + TableAnchorFlow.SaveDelay + 0.1);
            Assert.That(store.Saves.Count, Is.EqualTo(1));
            Assert.That(Vector3.Distance(store.Saves[0].position, table.transform.position), Is.LessThan(1e-4f));
            Assert.That(TableAnchorFlow.Decode(memory.Value)[0].source, Is.EqualTo(TablePlacementSource.HandCalibrated));
            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void Restored_calibrated_table_does_not_ask_for_calibration_again()
        {
            var go = new GameObject("Table");
            var table = go.AddComponent<TableFrame>();
            table.Place(new Pose(new Vector3(0f, 0.9f, 0.5f), Quaternion.identity), TablePlacementSource.Virtual);
            var cal = go.AddComponent<HandSurfaceCalibration>();
            cal.Init(table, new FakeHands());
            Assert.That(cal.PromptVisible, Is.True);
            table.Place(new Pose(new Vector3(0f, 0.74f, 0.5f), Quaternion.identity), TablePlacementSource.Restored, TablePlacementSource.HandCalibrated);
            Assert.That(cal.PromptVisible, Is.False, "上次親手校準過、錨點也找回來了：不用再按一次");
            cal.Recalibrate();
            Assert.That(cal.PromptVisible, Is.True, "重新校準按鈕照樣能用");
            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void Restored_auto_placed_table_keeps_the_prompt()
        {
            var go = new GameObject("Table");
            var table = go.AddComponent<TableFrame>();
            table.Place(new Pose(new Vector3(0f, 0.9f, 0.5f), Quaternion.identity), TablePlacementSource.Virtual);
            var cal = go.AddComponent<HandSurfaceCalibration>();
            cal.Init(table, new FakeHands());
            table.Place(new Pose(new Vector3(0f, 0.74f, 0.5f), Quaternion.identity), TablePlacementSource.Restored, TablePlacementSource.MrukTable);
            Assert.That(cal.PromptVisible, Is.True);
            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void Prompt_says_what_to_do_and_matches_whether_there_is_a_real_desk()
        {
            var go = new GameObject("Table");
            var table = go.AddComponent<TableFrame>();
            table.Place(new Pose(new Vector3(0f, 0.9f, 0.5f), Quaternion.identity), TablePlacementSource.Virtual);
            var cal = go.AddComponent<HandSurfaceCalibration>();
            cal.Init(table, new FakeHands());
            var label = go.transform.Find("HandPrints/PromptLabel");
            Assert.That(label, Is.Not.Null);
            var text = label.GetComponent<TextMesh>();
            Assert.That(text.text, Does.Contain("palms").IgnoreCase);
            Assert.That(text.text, Does.Contain("height").IgnoreCase, "沒有真的桌子：手放的高度就是桌子的高度");
            table.Place(new Pose(new Vector3(0f, 0.74f, 0.5f), Quaternion.identity), TablePlacementSource.MrukTable);
            Assert.That(text.text, Does.Contain("desk").IgnoreCase, "有真的桌子：手平放在桌面上");
            // 字在手印中間、牌的前方（靠玩家），不要壓在牌上
            Assert.That(label.localPosition.z, Is.LessThan(new TableLayout().CardsCenter.Z - new TableLayout().CardSize.Y * 0.5f));
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
