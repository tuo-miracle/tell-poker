using NUnit.Framework;
using Poker.Runtime;
using UnityEngine;

namespace Poker.Tests.Runtime
{
    public class TableFrameTests
    {
        GameObject _go;
        TableFrame _frame;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TableFrameTest");
            _frame = _go.AddComponent<TableFrame>();
            _frame.Place(new Pose(new Vector3(0.3f, 0.74f, 1.1f), Quaternion.Euler(0f, 35f, 0f)), TablePlacementSource.Virtual);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void WorldToTable_ThenBack_ReturnsSamePosition()
        {
            var world = new Vector3(-0.2f, 0.9f, 1.4f);
            var back = _frame.TableToWorld(_frame.WorldToTable(world));
            Assert.That(Vector3.Distance(world, back), Is.LessThan(1e-5f));
        }

        [Test]
        public void TableToWorld_ThenBack_ReturnsSameRotation()
        {
            var table = Quaternion.Euler(10f, -70f, 25f);
            var back = _frame.WorldToTable(_frame.TableToWorld(table));
            Assert.That(Quaternion.Angle(table, back), Is.LessThan(0.01f));
        }

        [Test]
        public void PoseRoundTrip_IsIdentity()
        {
            var world = new Pose(new Vector3(1f, 2f, 3f), Quaternion.Euler(40f, 10f, -20f));
            var back = _frame.TableToWorld(_frame.WorldToTable(world));
            Assert.That(Vector3.Distance(world.position, back.position), Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(world.rotation, back.rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void Origin_MapsToZero_AndAxesFollowFrame()
        {
            Assert.That(_frame.WorldToTable(new Vector3(0.3f, 0.74f, 1.1f)).magnitude, Is.LessThan(1e-5f));
            // 牌桌 +Z 是框架的 forward（世界繞 Y 35°）
            var fwd = Quaternion.Euler(0f, 35f, 0f) * Vector3.forward;
            var p = _frame.WorldToTable(new Vector3(0.3f, 0.74f, 1.1f) + fwd * 0.5f + Vector3.up * 0.1f);
            Assert.That(Vector3.Distance(p, new Vector3(0f, 0.1f, 0.5f)), Is.LessThan(1e-5f));
        }

        [Test]
        public void Place_RaisesPlacedAndRecordsSource()
        {
            int calls = 0;
            _frame.Placed += _ => calls++;
            _frame.Place(new Pose(Vector3.zero, Quaternion.identity), TablePlacementSource.MrukTable);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(_frame.IsPlaced, Is.True);
            Assert.That(_frame.Source, Is.EqualTo(TablePlacementSource.MrukTable));
        }

        [Test]
        public void Restored_placement_remembers_where_it_came_from()
        {
            _frame.Place(new Pose(Vector3.one, Quaternion.identity), TablePlacementSource.Restored, TablePlacementSource.HandCalibrated);
            Assert.That(_frame.Source, Is.EqualTo(TablePlacementSource.Restored));
            Assert.That(_frame.RestoredFrom, Is.EqualTo(TablePlacementSource.HandCalibrated));
            _frame.Place(new Pose(Vector3.zero, Quaternion.identity), TablePlacementSource.MrukTable);
            Assert.That(_frame.RestoredFrom, Is.EqualTo(TablePlacementSource.None));
        }

        [Test]
        public void SetRoom_RaisesRoomChanged()
        {
            int calls = 0;
            _frame.RoomChanged += _ => calls++;
            Assert.That(_frame.Room, Is.Null, "沒有房間資料");
            var room = new RoomObstacles();
            _frame.SetRoom(room);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(_frame.Room, Is.SameAs(room));
        }

        [Test]
        public void NumericsConversion_RoundTrips()
        {
            var v = new Vector3(1f, -2f, 3f);
            var q = Quaternion.Euler(10f, 20f, 30f);
            Assert.That(v.ToNumerics().ToUnity(), Is.EqualTo(v));
            Assert.That(Quaternion.Angle(q.ToNumerics().ToUnity(), q), Is.LessThan(0.001f));
        }
    }
}
