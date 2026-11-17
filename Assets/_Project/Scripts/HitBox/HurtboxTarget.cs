using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.HitBox
{
    internal static class HurtboxTarget
    {
        private static readonly int Layer = LayerMask.NameToLayer("Hurtbox");
        internal static int Mask => Layer >= 0 ? 1 << Layer : 0;

        internal static IDamageable Find(Collider collider) =>
            collider && collider.gameObject.layer == Layer ? collider.GetComponentInParent<IDamageable>() : null;

        internal static object Key(IDamageable target, Collider collider) =>
            (object)collider.GetComponentInParent<Hurtbox>() ?? target;

        internal static float Damage(Collider collider, float damage)
        {
            var part = collider.GetComponentInParent<Hurtbox>();
            return part ? damage * (1f - part.DamageReduction) : damage;
        }

        internal static IDamageable Find(Collider collider, LayerMask ownerLayers, out Unit unit)
        {
            unit = null;
            var target = Find(collider);
            if (target is not Component receiver) return null;

            unit = receiver.GetComponentInParent<Unit>();
            return unit && (ownerLayers.value & (1 << unit.gameObject.layer)) != 0 ? target : null;
        }
    }
}
