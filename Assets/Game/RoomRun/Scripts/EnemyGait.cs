// Stride speeds of an animated enemy model's in-place locomotion clips, so EnemyController can play them at the rate the
// enemy actually moves (no foot sliding). Speeds are in hip heights per second, measured from the clips' planted foot in
// Blender (ArtSource/DarkMonsters/stride.py); the world speed is that times the avatar's hip height in the world.
using UnityEngine;

namespace RoomRun
{
    public sealed class EnemyGait : MonoBehaviour
    {
        [Tooltip("The Move state's clip at speed 1, in hip heights per second.")]
        public float moveStride = 1.35f;
        [Tooltip("The Run state's clip (dashes) at speed 1, in hip heights per second.")]
        public float runStride = 2.5f;
    }
}
