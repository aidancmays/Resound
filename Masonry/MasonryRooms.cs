using System;
using System.Collections.Generic;
using Masonry;
using UnityEngine;

namespace Resound.Masonry
{
    /// <summary>
    /// Turns a Masonry layout into Resound rooms and doorways every time it's built: each piece becomes a room
    /// (its bounds, with acoustics picked by its module type), each joined socket pair a doorway, and each level
    /// link (stairs down to a crypt) a doorway between the two levels. Nothing on the pieces is changed. Put it next
    /// to the <see cref="LayoutBuilder"/>, or add it from code and call <see cref="Configure"/>.
    /// </summary>
    [RequireComponent(typeof(LayoutBuilder))]
    [DisallowMultipleComponent]
    public sealed class MasonryRooms : MonoBehaviour
    {
        [Serializable]
        public struct ModuleAcoustics
        {
            public ModuleType module;
            public RoomAcoustics acoustics;
        }

        [Tooltip("Acoustics for pieces of each module type.")]
        [SerializeField] List<ModuleAcoustics> modules = new();
        [Tooltip("Acoustics for pieces whose module type isn't listed.")]
        [SerializeField] RoomAcoustics fallback;
        [Tooltip("Height above a socket where its doorway is, metres (about head height).")]
        [SerializeField, Min(0f)] float doorwayHeight = 1.6f;
        [Tooltip("Extra height added to each piece's room, so the listener's head and things on top still count as inside.")]
        [SerializeField, Min(0f)] float extraHeight = 2f;

        LayoutBuilder builder;
        Func<Piece, RoomAcoustics> resolver;
        readonly Dictionary<Piece, Room> rooms = new();

        /// <summary>The room made for each built piece.</summary>
        public IReadOnlyDictionary<Piece, Room> Rooms => rooms;

        /// <summary>Raised after the rooms for a build have been registered.</summary>
        public event Action<MasonryRooms> RoomsBuilt;

        /// <summary>
        /// Picks acoustics in code instead of (or before) the module list: return null to fall through to the list.
        /// Rebuilds the rooms if the layout is already built.
        /// </summary>
        public void Configure(Func<Piece, RoomAcoustics> pickAcoustics, RoomAcoustics fallbackAcoustics = null)
        {
            resolver = pickAcoustics;
            if (fallbackAcoustics != null)
                fallback = fallbackAcoustics;
            if (builder != null && builder.Pieces.Count > 0)
                Register(builder);
        }

        void OnEnable()
        {
            builder = GetComponent<LayoutBuilder>();
            builder.Built += Register;
            builder.Cleared += Unregister;
            if (builder.Pieces.Count > 0)
                Register(builder);
        }

        void OnDisable()
        {
            if (builder != null)
            {
                builder.Built -= Register;
                builder.Cleared -= Unregister;
            }
            Unregister(builder);
        }

        void Unregister(LayoutBuilder _)
        {
            if (ResoundSystem.Exists)
                ResoundSystem.Graph.RemoveOwnedBy(this);
            rooms.Clear();
        }

        void Register(LayoutBuilder b)
        {
            if (!Application.isPlaying)
                return;
            Unregister(b);
            var graph = ResoundSystem.Graph;
            if (graph == null)
                return;

            foreach (var piece in b.Pieces)
            {
                if (piece == null)
                    continue;
                var t = piece.transform;
                var bounds = piece.Bounds;
                var size = Vector3.Scale(bounds.size, t.lossyScale);
                size.y += extraHeight;
                var center = t.TransformPoint(bounds.center) + Vector3.up * (extraHeight * 0.5f);
                var room = new Room(center, t.rotation, size, AcousticsFor(piece), this);
                graph.AddRoom(room);
                rooms[piece] = room;
            }

            // Doorways: each joined socket pair once.
            var seen = new HashSet<Socket>();
            foreach (var piece in b.Pieces)
            {
                if (piece == null || !rooms.TryGetValue(piece, out var room))
                    continue;
                foreach (var socket in piece.Sockets)
                {
                    var other = socket.JoinedTo;
                    if (other == null || seen.Contains(socket) || seen.Contains(other))
                        continue;
                    seen.Add(socket);
                    seen.Add(other);
                    var otherPiece = other.GetComponentInParent<Piece>();
                    if (otherPiece == null || !rooms.TryGetValue(otherPiece, out var otherRoom))
                        continue;
                    graph.Connect(room, otherRoom, socket.transform.position + Vector3.up * doorwayHeight);
                }
            }

            // Level links: the arrival piece of the next level stands on the link.
            foreach (var piece in b.Pieces)
            {
                if (piece == null || !rooms.TryGetValue(piece, out var room))
                    continue;
                foreach (var link in piece.GetComponentsInChildren<LevelLink>(true))
                {
                    var arrival = FindArrival(b, piece, link.transform.position);
                    if (arrival != null && rooms.TryGetValue(arrival, out var arrivalRoom))
                        graph.Connect(room, arrivalRoom, link.transform.position + Vector3.up * doorwayHeight);
                }
            }

            RoomsBuilt?.Invoke(this);
        }

        static Piece FindArrival(LayoutBuilder b, Piece from, Vector3 at)
        {
            Piece best = null;
            float bestDistance = 1f;
            foreach (var p in b.Pieces)
            {
                if (p == null || p == from || p.Level == from.Level)
                    continue;
                float d = Vector3.Distance(p.transform.position, at);
                if (d < bestDistance)
                {
                    best = p;
                    bestDistance = d;
                }
            }
            return best;
        }

        RoomAcoustics AcousticsFor(Piece piece)
        {
            var picked = resolver?.Invoke(piece);
            if (picked != null)
                return picked;
            foreach (var m in modules)
                if (m.module != null && m.module == piece.ModuleType)
                    return m.acoustics;
            return fallback;
        }
    }
}
