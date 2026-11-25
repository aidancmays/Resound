using System.Collections.Generic;
using UnityEngine;

namespace Resound
{
    /// <summary>
    /// Runs Resound: owns the <see cref="RoomGraph"/>, follows the scene's <see cref="AudioListener"/>, keeps one
    /// reverb zone on the listener that blends to the acoustics of the room they're in, and updates a few
    /// <see cref="ResoundSource"/>s each frame (occlusion and routing through doorways). Put one in your first scene
    /// to set it up; if there is none, one is made with default settings the first time anything needs it.
    /// </summary>
    /// <remarks>
    /// Reverb zones affect every 3D sound that doesn't bypass them; 2D music and UI sounds should set
    /// <see cref="AudioSource.bypassReverbZones"/> (Dynamic Music already does).
    /// </remarks>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class ResoundSystem : MonoBehaviour
    {
        [Header("Occlusion")]
        [Tooltip("Layers that block sound. Leave out triggers-only layers, small props and anything see-through to sound.")]
        [SerializeField] LayerMask occluders = 1; // Default
        [Tooltip("Rays cast from the listener to each source (more = smoother as things pass in front, but costs more).")]
        [SerializeField, Range(1, 9)] int raysPerSource = 5;
        [Tooltip("How far apart the extra rays land around the source, metres.")]
        [SerializeField, Min(0f)] float raySpread = 0.6f;
        [Tooltip("How much each wall a ray passes through blocks it (0..1). Two walls block more than one.")]
        [SerializeField, Range(0f, 1f)] float perWall = 0.65f;

        [Header("Routing")]
        [Tooltip("Hear sounds in other rooms from the nearest doorway along the shortest way round, when that's shorter than the muffled way through the walls.")]
        [SerializeField] bool routeThroughDoorways = true;
        [Tooltip("How much a fully blocked straight line counts against hearing a sound through the walls, in metres. Higher = sound goes round through doorways more often.")]
        [SerializeField, Min(0f)] float occlusionDetour = 18f;
        [Tooltip("Extra metres a shut doorway adds to a route through it.")]
        [SerializeField, Min(0f)] float closedDoorCost = 20f;

        [Header("Budget")]
        [Tooltip("Sources updated per frame. The rest keep easing toward their last result.")]
        [SerializeField, Min(1)] int sourcesPerFrame = 8;

        [Header("Reverb")]
        [Tooltip("Acoustics used where the listener is in no room. Empty = no reverb outside rooms.")]
        [SerializeField] RoomAcoustics outsideRooms;
        [Tooltip("Seconds to blend from one room's reverb to the next.")]
        [SerializeField, Min(0.01f)] float reverbBlendSeconds = 0.6f;
        [Tooltip("Switch the listener reverb off entirely.")]
        [SerializeField] bool reverbEnabled = true;

        static ResoundSystem instance;
        static bool quitting;

        readonly RoomGraph graph = new();
        readonly List<ResoundSource> sources = new();
        int cursor;

        AudioListener listener;
        float nextListenerSearch;
        AudioReverbZone zone;
        ReverbSettings currentReverb = ReverbSettings.Off;
        ReverbSettings fromReverb = ReverbSettings.Off;
        ReverbSettings toReverb = ReverbSettings.Off;
        float blend = 1f;
        Room listenerRoom;
        RoomAcoustics overrideOutside;

        /// <summary>The running system, made on first use if no scene has one.</summary>
        public static ResoundSystem Instance
        {
            get
            {
                if (instance == null && !quitting && Application.isPlaying)
                {
                    instance = FindAnyObjectByType<ResoundSystem>();
                    if (instance == null)
                        new GameObject("Resound").AddComponent<ResoundSystem>();
                }
                return instance;
            }
        }

        public static bool Exists => instance != null;

        /// <summary>Rooms and doorways. Add the level's rooms here (or use <see cref="ResoundRoom"/> / the Masonry adapter).</summary>
        public static RoomGraph Graph => Instance != null ? instance.graph : null;

        public LayerMask Occluders { get => occluders; set => occluders = value; }
        public int RaysPerSource { get => raysPerSource; set => raysPerSource = Mathf.Clamp(value, 1, 9); }
        public float RaySpread { get => raySpread; set => raySpread = Mathf.Max(0f, value); }
        public float PerWall { get => perWall; set => perWall = Mathf.Clamp01(value); }
        public bool RouteThroughDoorways { get => routeThroughDoorways; set => routeThroughDoorways = value; }
        public float OcclusionDetour { get => occlusionDetour; set => occlusionDetour = Mathf.Max(0f, value); }
        public float ClosedDoorCost { get => closedDoorCost; set => closedDoorCost = Mathf.Max(0f, value); }
        public bool ReverbEnabled { get => reverbEnabled; set => reverbEnabled = value; }

        /// <summary>
        /// Acoustics where the listener is in no room, set by code (for example per level: open air on the surface,
        /// stone underground). Null falls back to the inspector value.
        /// </summary>
        public RoomAcoustics OutsideRooms { get => overrideOutside != null ? overrideOutside : outsideRooms; set => overrideOutside = value; }

        /// <summary>The listener Resound follows, or null when the scene has none.</summary>
        public AudioListener Listener => listener;

        /// <summary>The room the listener is in, or null.</summary>
        public Room ListenerRoom => listenerRoom;

        public ReverbSettings CurrentReverb => currentReverb;

        public IReadOnlyList<ResoundSource> Sources => sources;

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }
            instance = this;
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
            zone = gameObject.AddComponent<AudioReverbZone>();
            zone.minDistance = 5000f;
            zone.maxDistance = 5001f;
            ReverbSettings.Off.ApplyTo(zone);
        }

        void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        void OnApplicationQuit() => quitting = true;

        internal void Register(ResoundSource source)
        {
            if (!sources.Contains(source))
                sources.Add(source);
        }

        internal void Unregister(ResoundSource source) => sources.Remove(source);

        void Update()
        {
            FindListener();
            if (listener == null)
                return;

            var position = listener.transform.position;
            transform.position = position;
            listenerRoom = graph.Find(position);
            UpdateReverb();

            int count = Mathf.Min(sourcesPerFrame, sources.Count);
            for (int i = 0; i < count; i++)
            {
                cursor = (cursor + 1) % sources.Count;
                sources[cursor].Evaluate(this, position, listenerRoom);
            }
        }

        void FindListener()
        {
            if (listener != null && listener.isActiveAndEnabled)
                return;
            if (Time.unscaledTime < nextListenerSearch)
                return;
            nextListenerSearch = Time.unscaledTime + 0.5f;
            listener = FindAnyObjectByType<AudioListener>();
        }

        void UpdateReverb()
        {
            zone.enabled = reverbEnabled;
            if (!reverbEnabled)
                return;

            var outside = OutsideRooms;
            var target = listenerRoom != null ? listenerRoom.Reverb : outside != null ? outside.ReverbFor(float.MaxValue) : ReverbSettings.Off;
            if (!Same(target, toReverb))
            {
                fromReverb = currentReverb;
                toReverb = target;
                blend = 0f;
            }
            if (blend < 1f)
            {
                blend = Mathf.Min(1f, blend + Time.unscaledDeltaTime / reverbBlendSeconds);
                currentReverb = ReverbSettings.Lerp(fromReverb, toReverb, Mathf.SmoothStep(0f, 1f, blend));
                currentReverb.ApplyTo(zone);
            }
        }

        static bool Same(in ReverbSettings a, in ReverbSettings b) =>
            Mathf.Approximately(a.room, b.room) && Mathf.Approximately(a.decayTime, b.decayTime) &&
            Mathf.Approximately(a.reverb, b.reverb) && Mathf.Approximately(a.reflections, b.reflections) &&
            Mathf.Approximately(a.roomHF, b.roomHF);

        // Occlusion -----------------------------------------------------------------------------------------------

        readonly RaycastHit[] hits = new RaycastHit[16];

        static readonly Vector2[] RayPattern =
        {
            new(0f, 0f), new(1f, 0f), new(-1f, 0f), new(0f, 1f), new(0f, -1f),
            new(0.7f, 0.7f), new(-0.7f, 0.7f), new(0.7f, -0.7f), new(-0.7f, -0.7f),
        };

        /// <summary>
        /// How blocked the straight line from <paramref name="from"/> to <paramref name="to"/> is, 0 (clear) to 1
        /// (solid), from several rays spread around <paramref name="to"/>. Colliders under either ignore root don't count.
        /// </summary>
        public float Occlusion(Vector3 from, Vector3 to, Transform ignoreA = null, Transform ignoreB = null)
        {
            var line = to - from;
            float distance = line.magnitude;
            if (distance < 0.01f)
                return 0f;
            var forward = line / distance;
            var right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 1e-4f)
                right = Vector3.right;
            right.Normalize();
            var up = Vector3.Cross(forward, right);

            float total = 0f;
            int rays = Mathf.Clamp(raysPerSource, 1, RayPattern.Length);
            for (int r = 0; r < rays; r++)
            {
                var offset = (right * RayPattern[r].x + up * RayPattern[r].y) * raySpread;
                var target = to + offset;
                var dir = target - from;
                float len = dir.magnitude;
                int n = Physics.RaycastNonAlloc(from, dir / len, hits, len, occluders, QueryTriggerInteraction.Ignore);
                int walls = 0;
                for (int i = 0; i < n; i++)
                {
                    var t = hits[i].transform;
                    if ((ignoreA != null && t.IsChildOf(ignoreA)) || (ignoreB != null && t.IsChildOf(ignoreB)))
                        continue;
                    walls++;
                }
                total += 1f - Mathf.Pow(1f - perWall, walls);
            }
            return total / rays;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
            quitting = false;
        }
    }
}
