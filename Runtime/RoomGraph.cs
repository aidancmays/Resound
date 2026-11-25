using System.Collections.Generic;
using UnityEngine;

namespace Resound
{
    /// <summary>A box of space that sounds one way: a level piece, a chamber, a stretch of open ground.</summary>
    public sealed class Room
    {
        internal readonly List<Portal> portals = new();

        public Room(Vector3 center, Quaternion rotation, Vector3 size, RoomAcoustics acoustics = null, object owner = null)
        {
            Center = center;
            Rotation = rotation;
            Extents = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) * 0.5f;
            Acoustics = acoustics;
            Owner = owner;
        }

        public Vector3 Center { get; }
        public Quaternion Rotation { get; }
        public Vector3 Extents { get; }
        public RoomAcoustics Acoustics { get; set; }

        /// <summary>Whatever registered the room (a piece, a component), for finding it again.</summary>
        public object Owner { get; }

        public float Volume => 8f * Extents.x * Extents.y * Extents.z;

        public IReadOnlyList<Portal> Portals => portals;

        public bool Contains(Vector3 point, float margin = 0f)
        {
            var local = Quaternion.Inverse(Rotation) * (point - Center);
            return Mathf.Abs(local.x) <= Extents.x + margin
                && Mathf.Abs(local.y) <= Extents.y + margin
                && Mathf.Abs(local.z) <= Extents.z + margin;
        }

        /// <summary>The reverb for this room, from its acoustics and size; null acoustics give no reverb.</summary>
        public ReverbSettings Reverb => Acoustics != null ? Acoustics.ReverbFor(Volume) : ReverbSettings.Off;
    }

    /// <summary>An opening between two rooms that sound passes through: a doorway, an arch, a stairwell.</summary>
    public sealed class Portal
    {
        public Portal(Room a, Room b, Vector3 position, float openness = 1f)
        {
            A = a;
            B = b;
            Position = position;
            Openness = openness;
        }

        public Room A { get; }
        public Room B { get; }
        public Vector3 Position { get; set; }

        /// <summary>1 = wide open, 0 = shut (a closed door). Sound through it is quieter and duller as it closes.</summary>
        public float Openness { get; set; }

        public Room Other(Room room) => room == A ? B : room == B ? A : null;
    }

    /// <summary>The result of <see cref="RoomGraph.FindPath"/>: the doorways a sound passes on its way to the listener.</summary>
    public struct SoundPath
    {
        /// <summary>Total distance from the listener, through every doorway, to the sound.</summary>
        public float length;
        /// <summary>How many doorways the sound goes through.</summary>
        public int hops;
        /// <summary>The first doorway on the way from the listener: where the sound seems to come from.</summary>
        public Vector3 nearestPortal;
        /// <summary>Product of the openness of every doorway on the way (1 = all open).</summary>
        public float openness;
        /// <summary>How much the doorways muffle the sound, 0..1.</summary>
        public float muffle;
    }

    /// <summary>
    /// Rooms and the portals between them, with a shortest-route search for sound. Plain C#, so it can be built
    /// and tested without a scene. <see cref="ResoundSystem"/> owns the one a game uses.
    /// </summary>
    public sealed class RoomGraph
    {
        readonly List<Room> rooms = new();
        readonly List<Portal> portals = new();

        // Dijkstra scratch, reused between searches.
        readonly Dictionary<Portal, float> cost = new();
        readonly Dictionary<Portal, Portal> previous = new();
        readonly HashSet<Portal> done = new();

        public IReadOnlyList<Room> Rooms => rooms;
        public IReadOnlyList<Portal> Portals => portals;

        /// <summary>Raised whenever rooms or portals are added or removed.</summary>
        public event System.Action Changed;

        public int Version { get; private set; }

        public Room AddRoom(Room room)
        {
            rooms.Add(room);
            Touch();
            return room;
        }

        public Portal Connect(Room a, Room b, Vector3 position, float openness = 1f)
        {
            if (a == null || b == null || a == b)
                return null;
            var portal = new Portal(a, b, position, openness);
            portals.Add(portal);
            a.portals.Add(portal);
            b.portals.Add(portal);
            Touch();
            return portal;
        }

        public void RemovePortal(Portal portal)
        {
            if (portal == null || !portals.Remove(portal))
                return;
            portal.A.portals.Remove(portal);
            portal.B.portals.Remove(portal);
            Touch();
        }

        public void RemoveRoom(Room room)
        {
            if (room == null || !rooms.Remove(room))
                return;
            foreach (var p in room.portals.ToArray())
                RemovePortal(p);
            Touch();
        }

        /// <summary>Removes every room registered with this owner (and their portals).</summary>
        public void RemoveOwnedBy(object owner)
        {
            for (int i = rooms.Count - 1; i >= 0; i--)
                if (Equals(rooms[i].Owner, owner))
                    RemoveRoom(rooms[i]);
        }

        public void Clear()
        {
            rooms.Clear();
            portals.Clear();
            Touch();
        }

        /// <summary>
        /// The room a point is in: the smallest one containing it, so a chamber placed inside a bigger space wins.
        /// Null when the point is in none.
        /// </summary>
        public Room Find(Vector3 point, float margin = 0.25f)
        {
            Room best = null;
            float bestVolume = float.MaxValue;
            foreach (var room in rooms)
            {
                if (!room.Contains(point, margin))
                    continue;
                float v = room.Volume;
                if (v < bestVolume)
                {
                    best = room;
                    bestVolume = v;
                }
            }
            return best;
        }

        /// <summary>
        /// Shortest way through doorways between the listener at <paramref name="from"/> (in
        /// <paramref name="fromRoom"/>) and a sound at <paramref name="to"/> (in <paramref name="toRoom"/>). Shut doorways cost extra
        /// (<paramref name="closedCost"/> metres when fully shut), so sound prefers open ways round.
        /// False when the rooms aren't connected.
        /// </summary>
        public bool FindPath(Vector3 from, Room fromRoom, Vector3 to, Room toRoom, out SoundPath path, float closedCost = 20f)
        {
            path = default;
            if (fromRoom == null || toRoom == null)
                return false;
            if (fromRoom == toRoom)
            {
                path.length = Vector3.Distance(from, to);
                path.nearestPortal = from;
                path.openness = 1f;
                return true;
            }

            cost.Clear();
            previous.Clear();
            done.Clear();
            foreach (var p in fromRoom.portals)
                Relax(p, null, Vector3.Distance(from, p.Position) + ClosedCost(p, closedCost));

            Portal goal = null;
            float goalCost = float.MaxValue;
            while (true)
            {
                Portal current = null;
                float currentCost = float.MaxValue;
                foreach (var pair in cost)
                    if (!done.Contains(pair.Key) && pair.Value < currentCost)
                    {
                        current = pair.Key;
                        currentCost = pair.Value;
                    }
                if (current == null || currentCost >= goalCost)
                    break;
                done.Add(current);

                if (current.A == toRoom || current.B == toRoom)
                {
                    float total = currentCost + Vector3.Distance(current.Position, to);
                    if (total < goalCost)
                    {
                        goalCost = total;
                        goal = current;
                    }
                }

                // Walk on through either room the portal opens into.
                Step(current, current.A, currentCost, closedCost);
                Step(current, current.B, currentCost, closedCost);
            }

            if (goal == null)
                return false;

            // Walk back from the sound's end to the listener's end, adding up the real distance (without the closed-door cost).
            float length = Vector3.Distance(goal.Position, to);
            float openness = 1f;
            float clear = 1f;
            int hops = 0;
            var p2 = goal;
            Portal first = goal;
            while (p2 != null)
            {
                hops++;
                openness *= Mathf.Clamp01(p2.Openness);
                clear *= 1f - DoorwayMuffle(p2);
                var prev = previous.TryGetValue(p2, out var pp) ? pp : null;
                length += prev != null ? Vector3.Distance(prev.Position, p2.Position) : Vector3.Distance(from, p2.Position);
                first = p2;
                p2 = prev;
            }

            path.length = length;
            path.hops = hops;
            path.nearestPortal = first.Position;
            path.openness = openness;
            path.muffle = 1f - clear * Mathf.Lerp(0.15f, 1f, openness);
            return true;
        }

        void Step(Portal current, Room room, float currentCost, float closedCost)
        {
            if (room == null)
                return;
            foreach (var next in room.portals)
                if (next != current && !done.Contains(next))
                    Relax(next, current, currentCost + Vector3.Distance(current.Position, next.Position) + ClosedCost(next, closedCost));
        }

        void Relax(Portal portal, Portal from, float value)
        {
            if (cost.TryGetValue(portal, out var existing) && existing <= value)
                return;
            cost[portal] = value;
            if (from != null)
                previous[portal] = from;
            else
                previous.Remove(portal);
        }

        static float ClosedCost(Portal p, float closedCost) => (1f - Mathf.Clamp01(p.Openness)) * closedCost;

        static float DoorwayMuffle(Portal p)
        {
            float a = p.A.Acoustics != null ? p.A.Acoustics.doorwayMuffle : 0.25f;
            float b = p.B.Acoustics != null ? p.B.Acoustics.doorwayMuffle : 0.25f;
            return Mathf.Clamp01(Mathf.Max(a, b));
        }

        void Touch()
        {
            Version++;
            Changed?.Invoke();
        }
    }
}
