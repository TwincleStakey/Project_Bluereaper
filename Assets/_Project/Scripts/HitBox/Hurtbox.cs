using UnityEngine;

namespace BlueReaper.HitBox
{
    [DisallowMultipleComponent]
    public sealed class Hurtbox : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float damageReduction;

        public float DamageReduction => Mathf.Clamp01(damageReduction);
    }
}
