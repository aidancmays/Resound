using System;
using UnityEngine;

namespace Resound
{
    /// <summary>
    /// One reverb, in the units of Unity's <see cref="AudioReverbZone"/> (I3DL2: levels in millibels, times in
    /// seconds). <see cref="Lerp"/> blends two of them, which is how the listener's reverb eases between rooms.
    /// </summary>
    [Serializable]
    public struct ReverbSettings
    {
        [Tooltip("Room effect level at mid frequencies, mB (-10000 = no reverb, 0 = full).")]
        [Range(-10000f, 0f)] public float room;
        [Tooltip("Room effect high-frequency level, mB.")]
        [Range(-10000f, 0f)] public float roomHF;
        [Tooltip("Room effect low-frequency level, mB.")]
        [Range(-10000f, 0f)] public float roomLF;
        [Tooltip("Reverberation decay time at mid frequencies, seconds.")]
        [Range(0.1f, 20f)] public float decayTime;
        [Tooltip("High-frequency to mid-frequency decay time ratio.")]
        [Range(0.1f, 2f)] public float decayHFRatio;
        [Tooltip("Early reflections level relative to the room effect, mB.")]
        [Range(-10000f, 1000f)] public float reflections;
        [Tooltip("Initial reflection delay, seconds.")]
        [Range(0f, 0.3f)] public float reflectionsDelay;
        [Tooltip("Late reverberation level relative to the room effect, mB.")]
        [Range(-10000f, 2000f)] public float reverb;
        [Tooltip("Late reverberation delay relative to the first reflection, seconds.")]
        [Range(0f, 0.1f)] public float reverbDelay;
        [Tooltip("Echo density in the late reverberation decay, percent.")]
        [Range(0f, 100f)] public float diffusion;
        [Tooltip("Modal density in the late reverberation decay, percent.")]
        [Range(0f, 100f)] public float density;

        public const float HFReference = 5000f;
        public const float LFReference = 250f;

        public ReverbSettings(float room, float roomHF, float roomLF, float decayTime, float decayHFRatio, float reflections,
            float reflectionsDelay, float reverb, float reverbDelay, float diffusion = 100f, float density = 100f)
        {
            this.room = room;
            this.roomHF = roomHF;
            this.roomLF = roomLF;
            this.decayTime = decayTime;
            this.decayHFRatio = decayHFRatio;
            this.reflections = reflections;
            this.reflectionsDelay = reflectionsDelay;
            this.reverb = reverb;
            this.reverbDelay = reverbDelay;
            this.diffusion = diffusion;
            this.density = density;
        }

        // The classic EAX / I3DL2 environment presets (the same numbers Unity's AudioReverbPreset uses).
        public static ReverbSettings Off => new(-10000f, -10000f, 0f, 1f, 1f, -2602f, 0.007f, 200f, 0.011f, 0f, 0f);
        public static ReverbSettings Generic => new(-1000f, -100f, 0f, 1.49f, 0.83f, -2602f, 0.007f, 200f, 0.011f);
        public static ReverbSettings Room => new(-1000f, -454f, 0f, 0.4f, 0.83f, -1646f, 0.002f, 53f, 0.003f);
        public static ReverbSettings StoneRoom => new(-1000f, -300f, 0f, 2.31f, 0.64f, -711f, 0.012f, 83f, 0.017f);
        public static ReverbSettings Hallway => new(-1000f, -300f, 0f, 1.49f, 0.59f, -1219f, 0.007f, 441f, 0.011f);
        public static ReverbSettings StoneCorridor => new(-1000f, -237f, 0f, 2.7f, 0.79f, -1214f, 0.013f, 395f, 0.02f);
        public static ReverbSettings Cave => new(-1000f, 0f, 0f, 2.91f, 1.3f, -602f, 0.015f, -302f, 0.022f);
        public static ReverbSettings Arena => new(-1000f, -698f, 0f, 7.24f, 0.33f, -1166f, 0.02f, 16f, 0.03f);
        public static ReverbSettings Hangar => new(-1000f, -1000f, 0f, 10.05f, 0.23f, -602f, 0.02f, 198f, 0.03f);
        public static ReverbSettings Forest => new(-1000f, -3300f, 0f, 1.49f, 0.54f, -2560f, 0.162f, -229f, 0.088f, 79f, 100f);
        public static ReverbSettings Plain => new(-1000f, -2000f, 0f, 1.49f, 0.5f, -2466f, 0.179f, -1926f, 0.1f, 21f, 100f);
        public static ReverbSettings Mountains => new(-1000f, -2500f, 0f, 1.49f, 0.21f, -2780f, 0.3f, -1434f, 0.1f, 27f, 100f);
        public static ReverbSettings SewerPipe => new(-1000f, -1000f, 0f, 2.81f, 0.14f, 429f, 0.014f, 1023f, 0.021f, 80f, 60f);

        public static ReverbSettings Lerp(in ReverbSettings a, in ReverbSettings b, float t)
        {
            t = Mathf.Clamp01(t);
            return new ReverbSettings(
                Mathf.Lerp(a.room, b.room, t),
                Mathf.Lerp(a.roomHF, b.roomHF, t),
                Mathf.Lerp(a.roomLF, b.roomLF, t),
                Mathf.Lerp(a.decayTime, b.decayTime, t),
                Mathf.Lerp(a.decayHFRatio, b.decayHFRatio, t),
                Mathf.Lerp(a.reflections, b.reflections, t),
                Mathf.Lerp(a.reflectionsDelay, b.reflectionsDelay, t),
                Mathf.Lerp(a.reverb, b.reverb, t),
                Mathf.Lerp(a.reverbDelay, b.reverbDelay, t),
                Mathf.Lerp(a.diffusion, b.diffusion, t),
                Mathf.Lerp(a.density, b.density, t));
        }

        /// <summary>Writes these values into a reverb zone (switching it to the User preset).</summary>
        public void ApplyTo(AudioReverbZone zone)
        {
            if (zone.reverbPreset != AudioReverbPreset.User)
                zone.reverbPreset = AudioReverbPreset.User;
            zone.room = Mathf.RoundToInt(room);
            zone.roomHF = Mathf.RoundToInt(roomHF);
            zone.roomLF = Mathf.RoundToInt(roomLF);
            zone.decayTime = decayTime;
            zone.decayHFRatio = decayHFRatio;
            zone.reflections = Mathf.RoundToInt(reflections);
            zone.reflectionsDelay = reflectionsDelay;
            zone.reverb = Mathf.RoundToInt(reverb);
            zone.reverbDelay = reverbDelay;
            zone.diffusion = diffusion;
            zone.density = density;
            zone.HFReference = HFReference;
            zone.LFReference = LFReference;
        }

        /// <summary>
        /// Writes these values into a reverb filter on one source (its own reverb, inside its effect chain).
        /// <paramref name="wetOffset"/> is added to the room level, in mB (negative = drier, e.g. for close sounds).
        /// </summary>
        public void ApplyTo(AudioReverbFilter filter, float wetOffset = 0f)
        {
            if (filter.reverbPreset != AudioReverbPreset.User)
                filter.reverbPreset = AudioReverbPreset.User;
            filter.dryLevel = 0f;
            filter.room = Mathf.Clamp(room + wetOffset, -10000f, 0f);
            filter.roomHF = roomHF;
            filter.roomLF = roomLF;
            filter.decayTime = decayTime;
            filter.decayHFRatio = decayHFRatio;
            filter.reflectionsLevel = reflections;
            filter.reflectionsDelay = reflectionsDelay;
            filter.reverbLevel = reverb;
            filter.reverbDelay = reverbDelay;
            filter.diffusion = diffusion;
            filter.density = density;
            filter.hfReference = HFReference;
            filter.lfReference = LFReference;
        }
    }
}
