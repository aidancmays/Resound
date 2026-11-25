using System.Collections.Generic;
using UnityEngine;

namespace Resound
{
    /// <summary>
    /// Fire-and-forget 3D sounds that get occlusion and doorway routing too, from a small pool:
    /// <c>ResoundOneShots.Play(clip, position)</c>.
    /// </summary>
    public static class ResoundOneShots
    {
        static readonly List<ResoundSource> pool = new();

        /// <summary>Plays a clip at a point. Returns the source so callers can tweak it (pitch, rolloff) before it's heard.</summary>
        public static AudioSource Play(AudioClip clip, Vector3 position, float volume = 1f, float maxDistance = 30f, float pitch = 1f)
        {
            if (clip == null)
                return null;
            var voice = Take();
            var source = voice.Source;
            voice.SetAnchor(position);
            voice.transform.position = position;
            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.maxDistance = maxDistance;
            source.Play();
            var system = ResoundSystem.Instance;
            if (system != null && system.Listener != null)
                voice.Evaluate(system, system.Listener.transform.position, system.ListenerRoom);
            voice.Snap();
            return source;
        }

        static ResoundSource Take()
        {
            foreach (var v in pool)
                if (v != null && !v.Source.isPlaying)
                    return v;
            pool.RemoveAll(v => v == null);
            // Each voice is a root object so it can sit anywhere and survives scene loads.
            var go = new GameObject($"Resound One Shot {pool.Count}");
            Object.DontDestroyOnLoad(go);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 1f;
            source.dopplerLevel = 0f;
            var voice = go.AddComponent<ResoundSource>();
            pool.Add(voice);
            return voice;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            pool.Clear();
        }
    }
}
