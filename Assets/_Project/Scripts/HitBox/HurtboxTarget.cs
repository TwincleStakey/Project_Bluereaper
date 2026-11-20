using BlueReaper.Core;
using BlueReaper.Player;
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

        internal static DamageResult TakeDamage(IDamageable target, Collider collider, AttackDamage attack, Unit source)
        {
            var part = collider.GetComponentInParent<Hurtbox>();
            var unit = (target as Component)?.GetComponentInParent<Unit>();
            float defense = unit && unit.Data ? unit.Data.Defense : 0f;
            float damage = DamageFormula.Calculate(attack.Power, attack.Multiplier, defense);
            if (unit && unit.TryGetComponent(out Enemy enemy) && enemy.Rank == EnemyRank.Normal
                && target is Health health && !health.IsDead && !health.NetworkDriven && !health.CombatLocked
                && source && source.TryGetComponent(out PlayerLife player))
                player.NotifyEnemyHit(enemy);

            return target.TakeDamage(part ? damage * (1f - part.DamageReduction) : damage, source);
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
