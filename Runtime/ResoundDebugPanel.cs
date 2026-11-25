using UnityEngine;
using UnityEngine.InputSystem;

namespace Resound
{
    /// <summary>
    /// Developer overlay (toggle with <see cref="toggleKey"/>): the listener's room and reverb, each playing source's
    /// occlusion and routing, and a key that drops a looping test sound where you stand so you can walk away from it
    /// round corners and through doorways.
    /// </summary>
    public sealed class ResoundDebugPanel : MonoBehaviour
    {
        [SerializeField] Key toggleKey = Key.F7;
        [SerializeField] Key dropTestSoundKey = Key.F6;
        [Tooltip("Looped at the drop point. Empty = a plain clicking tone.")]
        [SerializeField] AudioClip testClip;
        [SerializeField] bool visible;

        GameObject testSound;
        Vector2 scroll;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard[toggleKey].wasPressedThisFrame)
                visible = !visible;
            if (visible && keyboard[dropTestSoundKey].wasPressedThisFrame)
                DropTestSound();
        }

        void DropTestSound()
        {
            var system = ResoundSystem.Instance;
            if (system == null || system.Listener == null)
                return;
            if (testSound != null)
            {
                Destroy(testSound);
                testSound = null;
                return;
            }
            testSound = new GameObject("Resound Test Sound");
            testSound.transform.position = system.Listener.transform.position;
            var source = testSound.AddComponent<AudioSource>();
            source.clip = testClip != null ? testClip : MakeClicks();
            source.loop = true;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 1.5f;
            source.maxDistance = 40f;
            source.dopplerLevel = 0f;
            testSound.AddComponent<ResoundSource>();
            source.Play();
        }

        static AudioClip MakeClicks()
        {
            int rate = AudioSettings.outputSampleRate;
            var data = new float[rate];
            for (int i = 0; i < data.Length; i++)
            {
                // Two short bright clicks per second: easy to place by ear, and the muffling is obvious.
                int t = i % (rate / 2);
                float env = Mathf.Exp(-t / (rate * 0.012f));
                data[i] = env * (Random.value * 2f - 1f) * 0.6f;
            }
            var clip = AudioClip.Create("Resound Clicks", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void OnGUI()
        {
            if (!visible)
                return;
            var system = ResoundSystem.Exists ? ResoundSystem.Instance : null;
            GUILayout.BeginArea(new Rect(10, 10, 360, Screen.height - 20), GUI.skin.box);
            GUILayout.Label("Resound");
            if (system == null)
            {
                GUILayout.Label("Not running");
                GUILayout.EndArea();
                return;
            }
            var graph = ResoundSystem.Graph;
            var room = system.ListenerRoom;
            GUILayout.Label($"Rooms {graph.Rooms.Count}  Doorways {graph.Portals.Count}");
            GUILayout.Label($"Listener room: {(room == null ? "none" : $"{OwnerName(room)} ({(room.Acoustics != null ? room.Acoustics.name : "no acoustics")}, {room.Volume:0} m3)")}");
            var r = system.CurrentReverb;
            GUILayout.Label($"Reverb: room {r.room:0} decay {r.decayTime:0.00}s reverb {r.reverb:0}");
            system.ReverbEnabled = GUILayout.Toggle(system.ReverbEnabled, "Reverb");
            system.RouteThroughDoorways = GUILayout.Toggle(system.RouteThroughDoorways, "Doorways");
            if (system.Limiter != null)
            {
                system.Limiter.Bypass = !GUILayout.Toggle(!system.Limiter.Bypass, $"Limiter ({system.Limiter.Gain:0.00})");
            }
            GUILayout.Label($"{dropTestSoundKey}: {(testSound == null ? "drop" : "remove")} test sound");
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var s in system.Sources)
            {
                if (s == null || s.Source == null || !s.Source.isPlaying)
                    continue;
                GUILayout.Label($"{s.name}: blocked {s.LastOcclusion:0.00}{(s.Routed ? $", via {s.Hops} doorway(s)" : "")}");
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        static string OwnerName(Room room) => room.Owner is Object o && o != null ? o.name : "room";

        void OnDrawGizmos()
        {
            if (!visible || !ResoundSystem.Exists)
                return;
            var graph = ResoundSystem.Graph;
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.8f);
            foreach (var p in graph.Portals)
            {
                Gizmos.DrawWireSphere(p.Position, 0.4f);
                Gizmos.DrawLine(p.Position, p.A.Center);
                Gizmos.DrawLine(p.Position, p.B.Center);
            }
        }
    }
}
