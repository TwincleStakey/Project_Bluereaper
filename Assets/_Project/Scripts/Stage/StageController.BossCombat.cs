using System;
using System.Collections.Generic;
using BlueReaper.Core;
using BlueReaper.Player;
using UnityEngine;

namespace BlueReaper.Stage
{
    public partial class StageController
    {
        private readonly List<Health> bossParty = new(3);
        private float sharedBossHealth;
        private float sharedBossMaximum;
        private float lastBreathMultiplier;

        public (float Current, float Maximum) SharedBossHealth => (sharedBossHealth, sharedBossMaximum);
        public event Action<DamageAppliedInfo> SharedBossHealthDamaged;

        private void PrepareBossCombat(bool bossField)
        {
            EndBossCombat();
            if (!bossField) return;

            foreach (var record in units.Values)
                if (!record.Field && !record.Enemy && record.Health)
                    bossParty.Add(record.Health);

            if (bossParty.Count < 2) return;
            double total = 0d;
            foreach (Health member in bossParty)
                total += member.GetComponent<Unit>().Data.MaxHealth;

            sharedBossMaximum = ScaleHealth(total, stageData.SharedHealthMultiplier, 1f);
            lastBreathMultiplier = stageData.LastBreathHealthMultiplier;
            RestoreBossParty();
        }

        private void RestoreBossParty()
        {
            if (runFinished) return;
            sharedBossHealth = sharedBossMaximum;
            foreach (Health member in bossParty)
            {
                if (!member) continue;
                member.ConsumeSharedHealth = sharedBossMaximum > 0f ? ConsumeSharedBossHealth : null;
                if (member.TryGetComponent(out PlayerLife life))
                {
                    life.ConfigureLastBreath(false);
                    life.RestoreForBossPhase();
                }
                member.SetHealthPool(member.GetComponent<Unit>().Data.MaxHealth);
            }
            PublishSharedBossHealth();
        }

        private float ConsumeSharedBossHealth(float amount, Unit source)
        {
            float before = sharedBossHealth;
            if (!(before > 0f)) return 0f;

            float after = Mathf.Max(0f, before - amount);
            sharedBossHealth = after;
            if (after <= 0f)
            {
                foreach (Health member in bossParty)
                {
                    if (!member) continue;
                    member.ConsumeSharedHealth = null;
                    if (member.TryGetComponent(out PlayerLife life)) life.ConfigureLastBreath(true);
                    float max = ScaleHealth(member.GetComponent<Unit>().Data.MaxHealth, lastBreathMultiplier, .25f);
                    member.SetHealthPool(max);
                }
            }

            PublishSharedBossHealth();
            var damage = new DamageAppliedInfo(source, before, after);
            SharedBossHealthDamaged?.Invoke(damage);
            return damage.HealthLost;
        }

        private void PublishSharedBossHealth()
        {
            foreach (Health member in bossParty)
                if (member && member.TryGetComponent(out PlayerLife life))
                    life.SetSharedBossHealth(sharedBossHealth, sharedBossMaximum);
        }

        private void EndBossCombat()
        {
            if (bossParty.Count == 0) return;
            bool wasShared = sharedBossMaximum > 0f;
            sharedBossHealth = sharedBossMaximum = 0f;
            foreach (Health member in bossParty)
            {
                if (!member) continue;
                member.ConsumeSharedHealth = null;
                if (member.TryGetComponent(out PlayerLife life))
                {
                    life.ConfigureLastBreath(false);
                    life.SetSharedBossHealth(0f, 0f);
                    if (wasShared && !runFinished) life.RestoreForBossPhase();
                }
                if (wasShared && !runFinished) member.SetHealthPool(member.GetComponent<Unit>().Data.MaxHealth);
            }
            bossParty.Clear();
        }

        private void InitializeEnemyCombat(GameObject instance)
        {
            if (bossParty.Count == 0 || !instance.TryGetComponent(out Enemy enemy) || enemy.Rank != EnemyRank.Boss) return;

            var multipliers = stageData.BossHealthMultipliersByPartySize;
            float multiplier = bossParty.Count switch { 2 => multipliers.y, 3 => multipliers.z, _ => multipliers.x };
            float maximum = ScaleHealth(enemy.GetComponent<Unit>().Data.MaxHealth, multiplier, 1f);
            enemy.GetComponent<Health>().SetHealthPool(maximum);
        }

        private bool IsBossPhaseTransitioning()
        {
            foreach (var record in units.Values)
                if (record.Enemy && !record.Dead && record.Enemy.IsPhaseTransitioning) return true;
            return false;
        }

        private static float ScaleHealth(double maximum, float multiplier, float defaultMultiplier)
        {
            if (!(multiplier > 0f) || !float.IsFinite(multiplier)) multiplier = defaultMultiplier;
            return (float)Math.Clamp(maximum * multiplier, 1d, float.MaxValue);
        }
    }
}
