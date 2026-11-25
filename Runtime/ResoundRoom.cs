using UnityEngine;

namespace Resound
{
    /// <summary>
    /// A hand-placed room: a box (in this object's space) with its acoustics, registered while enabled. For fixed
    /// scenes such as rest areas; procedural levels register their rooms from code (see the Masonry adapter).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResoundRoom : MonoBehaviour
    {
        [SerializeField] Bounds bounds = new(new Vector3(0f, 2f, 0f), new Vector3(10f, 4f, 10f));
        [SerializeField] RoomAcoustics acoustics;

        public Room Room { get; private set; }

        public Bounds Bounds { get => bounds; set => bounds = value; }
        public RoomAcoustics Acoustics
        {
            get => acoustics;
            set
            {
                acoustics = value;
                if (Room != null)
                    Room.Acoustics = value;
            }
        }

        void OnEnable()
        {
            var graph = ResoundSystem.Graph;
            if (graph == null)
                return;
            var scale = transform.lossyScale;
            Room = graph.AddRoom(new Room(transform.TransformPoint(bounds.center), transform.rotation, Vector3.Scale(bounds.size, scale), acoustics, this));
            foreach (var portal in FindObjectsByType<ResoundPortal>(FindObjectsSortMode.None))
                portal.Reconnect();
        }

        void OnDisable()
        {
            if (Room != null && ResoundSystem.Exists)
                ResoundSystem.Graph.RemoveRoom(Room);
            Room = null;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.7f, 0.3f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
