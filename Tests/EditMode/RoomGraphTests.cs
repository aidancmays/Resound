using NUnit.Framework;
using UnityEngine;

namespace Resound.Tests
{
    public class RoomGraphTests
    {
        // Three 10 m rooms in a row along X (A | B | C), doorways at x = 5 and x = 15.
        static RoomGraph Corridor(out Room a, out Room b, out Room c, float doorZ = 0f)
        {
            var g = new RoomGraph();
            a = g.AddRoom(new Room(new Vector3(0, 2, 0), Quaternion.identity, new Vector3(10, 4, 10)));
            b = g.AddRoom(new Room(new Vector3(10, 2, 0), Quaternion.identity, new Vector3(10, 4, 10)));
            c = g.AddRoom(new Room(new Vector3(20, 2, 0), Quaternion.identity, new Vector3(10, 4, 10)));
            g.Connect(a, b, new Vector3(5, 1.6f, doorZ));
            g.Connect(b, c, new Vector3(15, 1.6f, doorZ));
            return g;
        }

        [Test]
        public void FindsSmallestRoomContainingPoint()
        {
            var g = new RoomGraph();
            var big = g.AddRoom(new Room(Vector3.zero, Quaternion.identity, new Vector3(20, 10, 20)));
            var small = g.AddRoom(new Room(Vector3.zero, Quaternion.identity, new Vector3(4, 4, 4)));
            Assert.AreSame(small, g.Find(Vector3.zero));
            Assert.AreSame(big, g.Find(new Vector3(8, 0, 0)));
            Assert.IsNull(g.Find(new Vector3(50, 0, 0)));
        }

        [Test]
        public void RotatedRoomContainsAlongItsOwnAxes()
        {
            var room = new Room(Vector3.zero, Quaternion.Euler(0, 90, 0), new Vector3(10, 4, 2));
            Assert.IsTrue(room.Contains(new Vector3(0, 0, 4)));
            Assert.IsFalse(room.Contains(new Vector3(4, 0, 0)));
        }

        [Test]
        public void PathGoesThroughEveryDoorwayInOrder()
        {
            var g = Corridor(out var a, out _, out var c);
            var listener = new Vector3(0, 1.6f, 0);
            var sound = new Vector3(20, 1.6f, 0);
            Assert.IsTrue(g.FindPath(listener, a, sound, c, out var path));
            Assert.AreEqual(2, path.hops);
            Assert.AreEqual(20f, path.length, 0.01f);
            Assert.AreEqual(new Vector3(5, 1.6f, 0), path.nearestPortal);
            Assert.AreEqual(1f, path.openness, 1e-5f);
        }

        [Test]
        public void PrefersOpenWayRoundOverShutDoor()
        {
            // A and C meet directly through a shut door, or the long way through B.
            // The way through B bends out to doorways at z = 8, so the straight shortcut is shorter when open.
            var g = Corridor(out var a, out _, out var c, doorZ: 8f);
            var shortcut = g.Connect(a, c, new Vector3(10, 1.6f, 0), openness: 0f);
            Assert.IsTrue(g.FindPath(new Vector3(0, 1.6f, 0), a, new Vector3(20, 1.6f, 0), c, out var shut, closedCost: 50f));
            Assert.AreEqual(2, shut.hops);
            shortcut.Openness = 1f;
            Assert.IsTrue(g.FindPath(new Vector3(0, 1.6f, 0), a, new Vector3(20, 1.6f, 0), c, out var open, closedCost: 50f));
            Assert.AreEqual(1, open.hops);
        }

        [Test]
        public void UnconnectedRoomsHaveNoPath()
        {
            var g = new RoomGraph();
            var a = g.AddRoom(new Room(Vector3.zero, Quaternion.identity, Vector3.one * 4));
            var b = g.AddRoom(new Room(Vector3.right * 10, Quaternion.identity, Vector3.one * 4));
            Assert.IsFalse(g.FindPath(Vector3.zero, a, Vector3.right * 10, b, out _));
        }

        [Test]
        public void RemovingRoomRemovesItsDoorways()
        {
            var g = Corridor(out _, out var b, out _);
            g.RemoveRoom(b);
            Assert.AreEqual(2, g.Rooms.Count);
            Assert.AreEqual(0, g.Portals.Count);
        }

        [Test]
        public void ReverbGrowsWithRoomSize()
        {
            var acoustics = RoomAcoustics.Create("Stone", ReverbSettings.StoneRoom, ReverbSettings.Cave);
            Assert.AreEqual(ReverbSettings.StoneRoom.decayTime, acoustics.ReverbFor(10f).decayTime, 1e-4f);
            Assert.AreEqual(ReverbSettings.Cave.decayTime, acoustics.ReverbFor(1e6f).decayTime, 1e-4f);
            float mid = acoustics.ReverbFor(500f).decayTime;
            Assert.Greater(mid, ReverbSettings.StoneRoom.decayTime);
            Assert.Less(mid, ReverbSettings.Cave.decayTime);
            Object.DestroyImmediate(acoustics);
        }
    }
}
