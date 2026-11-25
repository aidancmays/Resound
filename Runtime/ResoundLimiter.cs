using UnityEngine;

namespace Resound
{
    /// <summary>
    /// A safety limiter on the final mix. Sits on the <see cref="AudioListener"/>'s object, so it sees everything
    /// the player hears: when the mix (a voice up close plus its reverb, several loud sounds at once) would go past
    /// full scale, it turns the whole mix down smoothly instead of letting it clip into harsh distortion.
    /// <see cref="ResoundSystem"/> adds one to the listener it follows.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResoundLimiter : MonoBehaviour
    {
        [Tooltip("Peak level the mix is held under (1 = full scale).")]
        [SerializeField, Range(0.5f, 1f)] float ceiling = 0.9f;
        [Tooltip("Seconds to recover after turning down.")]
        [SerializeField, Range(0.02f, 2f)] float release = 0.25f;

        volatile bool bypass;
        float gain = 1f;
        float releaseStep;

        /// <summary>How far the limiter is turning the mix down right now (1 = not at all).</summary>
        public float Gain => gain;

        public bool Bypass { get => bypass; set => bypass = value; }

        void Awake() => UpdateRelease();
        void OnValidate() => UpdateRelease();

        void UpdateRelease()
        {
            int rate = Mathf.Max(8000, AudioSettings.outputSampleRate);
            releaseStep = 1f / (release * rate);
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (bypass)
                return;
            float c = ceiling;
            float g = gain;
            for (int i = 0; i < data.Length; i += channels)
            {
                float peak = 0f;
                for (int k = 0; k < channels; k++)
                {
                    float a = data[i + k];
                    if (a < 0f) a = -a;
                    if (a > peak) peak = a;
                }
                // Instant attack: never let a frame through above the ceiling. Slow release back up.
                float needed = peak * g > c ? c / peak : 1f;
                if (needed < g)
                    g = needed;
                else
                    g = Mathf.Min(1f, g + releaseStep);
                for (int k = 0; k < channels; k++)
                    data[i + k] *= g;
            }
            gain = g;
        }
    }
}
