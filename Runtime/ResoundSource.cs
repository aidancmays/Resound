using UnityEngine;

namespace Resound
{
    /// <summary>
    /// Makes an <see cref="AudioSource"/> sound like it's really in the level: muffled and quieter behind walls, and
    /// heard from the doorway it would reach you through when it's in another room (it slides its own object there,
    /// so Unity's distance falloff and panning follow the way round).
    /// </summary>
    /// <remarks>
    /// Because it moves its own transform, put the AudioSource and this on a child used only for sound (no
    /// renderers or colliders), placed where the sound comes from. Its starting local position is the true spot;
    /// on a root object call <see cref="SetAnchor"/> when the sound moves. Adds an <see cref="AudioLowPassFilter"/>
    /// and applies its gain in <c>OnAudioFilterRead</c>, so other scripts can keep setting the source's volume.
    /// Any script making the audio in <c>OnAudioFilterRead</c> must sit above this component.
    /// </remarks>
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    public sealed class ResoundSource : MonoBehaviour
    {
        [Tooltip("Muffle and quiet the sound when walls are in the way.")]
        [SerializeField] bool occlusion = true;
        [Tooltip("Low-pass cutoff when fully blocked, Hz.")]
        [SerializeField, Range(200f, 22000f)] float blockedCutoff = 900f;
        [Tooltip("Loudness when fully blocked (multiplies the volume).")]
        [SerializeField, Range(0f, 1f)] float blockedGain = 0.35f;
        [Tooltip("Hear it from the doorway it reaches you through when it's in another room.")]
        [SerializeField] bool throughDoorways = true;
        [Tooltip("Seconds to ease into a new result (higher = smoother, slower to react).")]
        [SerializeField, Range(0.02f, 1f)] float smoothing = 0.15f;
        [Tooltip("Colliders under this transform never block the sound (usually the thing making it). Empty = this object's root.")]
        [SerializeField] Transform ignoreColliders;

        const float Open = 22000f;

        AudioSource source;
        AudioLowPassFilter lowPass;
        Vector3 localAnchor;
        Vector3 worldAnchor;
        bool rooted;

        // Target from the last evaluation, and the eased current values.
        float targetCutoff = Open, cutoff = Open;
        float targetGain = 1f, gain = 1f;
        Vector3 targetOffset, offset;
        volatile float audioGain = 1f;
        float lastGain = 1f;

        /// <summary>How blocked the straight line to the listener was at the last update, 0..1.</summary>
        public float LastOcclusion { get; private set; }

        /// <summary>True when the sound is currently heard through a doorway.</summary>
        public bool Routed { get; private set; }

        /// <summary>How many doorways the sound passes on its way, when routed.</summary>
        public int Hops { get; private set; }

        /// <summary>Where the sound really is (before routing moves it).</summary>
        public Vector3 Anchor => rooted || transform.parent == null ? worldAnchor : transform.parent.TransformPoint(localAnchor);

        public AudioSource Source => source;

        public bool Occlusion { get => occlusion; set => occlusion = value; }
        public bool ThroughDoorways { get => throughDoorways; set => throughDoorways = value; }
        public float BlockedCutoff { get => blockedCutoff; set => blockedCutoff = Mathf.Clamp(value, 200f, Open); }
        public float BlockedGain { get => blockedGain; set => blockedGain = Mathf.Clamp01(value); }

        /// <summary>Colliders under this transform don't block the sound.</summary>
        public Transform IgnoreColliders { get => ignoreColliders; set => ignoreColliders = value; }

        void Awake()
        {
            source = GetComponent<AudioSource>();
            lowPass = GetComponent<AudioLowPassFilter>();
            if (lowPass == null)
                lowPass = gameObject.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = Open;
            rooted = transform.parent == null;
            localAnchor = transform.localPosition;
            worldAnchor = transform.position;
            if (ignoreColliders == null)
                ignoreColliders = transform.root;
            if (throughDoorways && (GetComponent<Renderer>() != null || GetComponent<Collider>() != null))
            {
                Debug.LogWarning($"[Resound] {name} has a renderer or collider, so it can't be moved to doorways; put the AudioSource on its own child. Routing is off for it.", this);
                throughDoorways = false;
            }
        }

        void OnEnable()
        {
            var system = ResoundSystem.Instance;
            if (system != null)
                system.Register(this);
        }

        void OnDisable()
        {
            if (ResoundSystem.Exists)
                ResoundSystem.Instance.Unregister(this);
            // Put it back where it belongs.
            offset = targetOffset = Vector3.zero;
            if (!rooted && transform.parent != null)
                transform.localPosition = localAnchor;
            else if (rooted)
                transform.position = worldAnchor;
        }

        /// <summary>For a sound on a root object: where it really is now. Also resets the true spot of a child sound.</summary>
        public void SetAnchor(Vector3 world)
        {
            if (rooted || transform.parent == null)
            {
                rooted = true;
                worldAnchor = world;
            }
            else
            {
                localAnchor = transform.parent.InverseTransformPoint(world);
            }
        }

        /// <summary>Jumps straight to the current result instead of easing (for a sound that just started).</summary>
        public void Snap()
        {
            cutoff = targetCutoff;
            gain = targetGain;
            offset = targetOffset;
            Apply();
        }

        /// <summary>Called by <see cref="ResoundSystem"/> a few times a second.</summary>
        internal void Evaluate(ResoundSystem system, Vector3 listener, Room listenerRoom)
        {
            if (source == null)
                return;
            var anchor = Anchor;
            float distance = Vector3.Distance(listener, anchor);
            if (!source.isPlaying || distance > source.maxDistance * 1.1f)
                return;

            var listenerRoot = system.Listener != null ? system.Listener.transform.root : null;
            float occ = occlusion ? system.Occlusion(listener, anchor, ignoreColliders, listenerRoot) : 0f;
            LastOcclusion = occ;

            Routed = false;
            Hops = 0;
            targetOffset = Vector3.zero;
            float muffle = occ;
            float quiet = occ;

            var graph = ResoundSystem.Graph;
            if (throughDoorways && system.RouteThroughDoorways && graph != null && occ > 0.05f && listenerRoom != null)
            {
                var sourceRoom = graph.Find(anchor);
                if (sourceRoom != null && sourceRoom != listenerRoom &&
                    graph.FindPath(listener, listenerRoom, anchor, sourceRoom, out var path, system.ClosedDoorCost))
                {
                    float direct = distance + occ * system.OcclusionDetour;
                    float around = path.length + (1f - path.openness) * system.ClosedDoorCost;
                    if (around < direct)
                    {
                        Routed = true;
                        Hops = path.hops;
                        var toDoor = path.nearestPortal - listener;
                        if (toDoor.sqrMagnitude < 1e-4f)
                            toDoor = anchor - listener;
                        var heard = listener + toDoor.normalized * path.length;
                        targetOffset = heard - anchor;
                        // Anything still in the way between you and the doorway counts too.
                        float doorOcc = occlusion ? system.Occlusion(listener, path.nearestPortal, ignoreColliders, listenerRoot) : 0f;
                        muffle = Mathf.Max(path.muffle, doorOcc);
                        quiet = Mathf.Max(1f - path.openness, doorOcc) * 0.8f;
                    }
                }
            }

            targetCutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(Open), Mathf.Log(blockedCutoff), Mathf.Clamp01(muffle)));
            targetGain = Mathf.Lerp(1f, blockedGain, Mathf.Clamp01(quiet));
        }

        void LateUpdate()
        {
            float k = 1f - Mathf.Exp(-Time.deltaTime / smoothing);
            cutoff = Mathf.Exp(Mathf.Lerp(Mathf.Log(cutoff), Mathf.Log(targetCutoff), k));
            gain = Mathf.Lerp(gain, targetGain, k);
            offset = Vector3.Lerp(offset, targetOffset, k);
            Apply();
        }

        void Apply()
        {
            lowPass.cutoffFrequency = cutoff;
            audioGain = gain;
            if (!throughDoorways)
                return;
            var anchor = Anchor;
            transform.position = anchor + offset;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float target = audioGain;
            float from = lastGain;
            if (Mathf.Approximately(from, 1f) && Mathf.Approximately(target, 1f))
                return;
            int frames = data.Length / channels;
            float step = frames > 0 ? (target - from) / frames : 0f;
            float g = from;
            for (int i = 0; i < data.Length; i += channels)
            {
                g += step;
                for (int c = 0; c < channels; c++)
                    data[i + c] *= g;
            }
            lastGain = target;
        }
    }
}
