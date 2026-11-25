using UnityEngine;

namespace Resound
{
    /// <summary>
    /// How a kind of room sounds. The reverb is picked from the room's size: rooms up to <see cref="smallVolume"/>
    /// cubic metres get <see cref="small"/>, rooms of <see cref="largeVolume"/> or more get <see cref="large"/>, and
    /// sizes between blend the two, so a narrow passage rings short and a great hall rings long.
    /// </summary>
    [CreateAssetMenu(menuName = "Resound/Room Acoustics", fileName = "Room Acoustics")]
    public sealed class RoomAcoustics : ScriptableObject
    {
        [Tooltip("Reverb in a room of Small Volume or less.")]
        public ReverbSettings small = ReverbSettings.StoneRoom;
        [Tooltip("Reverb in a room of Large Volume or more.")]
        public ReverbSettings large = ReverbSettings.Cave;
        [Tooltip("Cubic metres at or below which a room uses the Small reverb.")]
        [Min(1f)] public float smallVolume = 150f;
        [Tooltip("Cubic metres at or above which a room uses the Large reverb.")]
        [Min(1f)] public float largeVolume = 1500f;

        [Tooltip("Open to the sky. Used by games to tell outdoor rooms apart (for example to keep weather sounds out of indoor ones).")]
        public bool outdoors;

        [Tooltip("How much a doorway out of this room muffles a sound passing through it (0 = not at all, 1 = fully).")]
        [Range(0f, 1f)] public float doorwayMuffle = 0.25f;

        /// <summary>The reverb for a room of this many cubic metres.</summary>
        public ReverbSettings ReverbFor(float volume)
        {
            if (largeVolume <= smallVolume)
                return volume >= largeVolume ? large : small;
            // Log scale: doubling a room's size matters about as much at any size.
            float t = Mathf.InverseLerp(Mathf.Log(smallVolume), Mathf.Log(largeVolume), Mathf.Log(Mathf.Max(1f, volume)));
            return ReverbSettings.Lerp(small, large, t);
        }

        /// <summary>A runtime instance with the given reverbs (not saved as an asset).</summary>
        public static RoomAcoustics Create(string name, ReverbSettings small, ReverbSettings large, bool outdoors = false,
            float smallVolume = 150f, float largeVolume = 1500f, float doorwayMuffle = 0.25f)
        {
            var a = CreateInstance<RoomAcoustics>();
            a.name = name;
            a.small = small;
            a.large = large;
            a.outdoors = outdoors;
            a.smallVolume = smallVolume;
            a.largeVolume = largeVolume;
            a.doorwayMuffle = doorwayMuffle;
            a.hideFlags = HideFlags.DontSave;
            return a;
        }
    }
}
