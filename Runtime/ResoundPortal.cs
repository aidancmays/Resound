using UnityEngine;

namespace Resound
{
    /// <summary>
    /// A hand-placed doorway between the two rooms either side of it (found <see cref="probe"/> metres along its
    /// forward and back axes). Set <see cref="Openness"/> from a door's state so a shut door dulls what's behind it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResoundPortal : MonoBehaviour
    {
        [Tooltip("How far to each side to look for the rooms this joins, metres.")]
        [SerializeField, Min(0.1f)] float probe = 1f;
        [Tooltip("1 = open, 0 = shut.")]
        [SerializeField, Range(0f, 1f)] float openness = 1f;

        Portal portal;

        public float Openness
        {
            get => openness;
            set
            {
                openness = Mathf.Clamp01(value);
                if (portal != null)
                    portal.Openness = openness;
            }
        }

        public Portal Portal => portal;

        void OnEnable() => Reconnect();

        void OnDisable()
        {
            if (portal != null && ResoundSystem.Exists)
                ResoundSystem.Graph.RemovePortal(portal);
            portal = null;
        }

        /// <summary>Finds the rooms either side again (after rooms were added or moved).</summary>
        public void Reconnect()
        {
            var graph = ResoundSystem.Graph;
            if (graph == null || !isActiveAndEnabled)
                return;
            if (portal != null)
                graph.RemovePortal(portal);
            portal = null;
            var p = transform.position;
            var a = graph.Find(p + transform.forward * probe, 0f);
            var b = graph.Find(p - transform.forward * probe, 0f);
            if (a != null && b != null && a != b)
                portal = graph.Connect(a, b, p, openness);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = portal != null ? new Color(0.3f, 1f, 0.5f, 0.9f) : new Color(1f, 0.3f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.3f);
            Gizmos.DrawLine(transform.position - transform.forward * probe, transform.position + transform.forward * probe);
        }
    }
}
