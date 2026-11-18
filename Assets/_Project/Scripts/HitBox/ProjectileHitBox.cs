using System;
using BlueReaper.BulletPatterns;
using BlueReaper.Core;
using BlueReaper.Vfx;
using UnityEngine;

namespace BlueReaper.HitBox
{
    public class ProjectileHitBox : MonoBehaviour
    {
        private Unit owner;
        private AttackDamage damage;
        private bool detachedEnemyProjectile;
        private bool armed, returned;
        private Rigidbody body;
        private BulletPatternData pattern;
        private int pathIndex, emissionIndex;
        private float spawnedAt;
        private Vector3 origin;
        private Quaternion facing;
        private Action<ProjectileHitBox> onReturned;
        private Func<bool> vfxGuard;
        private HitWindow hits;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (!body) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            foreach (var hitCollider in GetComponentsInChildren<Collider>(true)) hitCollider.isTrigger = true;
        }

        private void OnEnable()
        {
            armed = false;
            returned = false;
            pattern = null;
            vfxGuard = null;
        }

        private void OnDisable()
        {
            armed = false;
            returned = true;
            hits?.Reset();
            var callback = onReturned;
            onReturned = null;
            callback?.Invoke(this);
        }

        public void Init(Unit unit, float damageMultiplier)
        {
            owner = unit;
            detachedEnemyProjectile = false;
            damage = new AttackDamage(unit && unit.Data ? unit.Data.Attack : 0f, damageMultiplier);
            returned = false;
            armed = true;
        }

        public void Init(Unit unit, BulletPatternData pattern, int pathIndex, int emissionIndex,
            Vector3 origin, Quaternion facing, Action<ProjectileHitBox> onReturned, Func<bool> vfxGuard = null)
        {
            Init(unit, pattern.DamageMultiplier);
            InitFlight(pattern, pathIndex, emissionIndex, origin, facing, onReturned, vfxGuard);
        }

        internal void InitDeathProjectile(AttackDamage damage, BulletPatternData pattern, int pathIndex, int emissionIndex,
            Vector3 origin, Quaternion facing, Action<ProjectileHitBox> onReturned, Func<bool> vfxGuard = null)
        {
            owner = null;
            detachedEnemyProjectile = true;
            this.damage = damage;
            returned = false;
            armed = true;
            InitFlight(pattern, pathIndex, emissionIndex, origin, facing, onReturned, vfxGuard);
        }

        private void InitFlight(BulletPatternData pattern, int pathIndex, int emissionIndex,
            Vector3 origin, Quaternion facing, Action<ProjectileHitBox> onReturned, Func<bool> vfxGuard)
        {
            this.pattern = pattern;
            hits?.Reset();
            if (pattern.UseHitWindow) hits ??= new HitWindow();
            this.pathIndex = pathIndex;
            this.emissionIndex = emissionIndex;
            spawnedAt = Time.time;
            this.origin = origin;
            this.facing = facing;
            this.onReturned = onReturned;
            this.vfxGuard = vfxGuard;
        }

        internal static ProjectileHitBox Spawn(BulletPatternData pattern, int pathIndex, int emissionIndex,
            Vector3 origin, Quaternion facing)
        {
            if (!pattern.TryEvaluate(pathIndex, emissionIndex, emissionIndex * pattern.RepeatInterval,
                out var position, out var rotation)) return null;

            var instance = ObjectPool.Spawn(pattern.BulletPrefab, origin + facing * position, facing * rotation);
            var projectile = instance.GetComponent<ProjectileHitBox>();
            if (projectile) return projectile;

            ObjectPool.Despawn(instance);
            Debug.LogWarning("Bullet prefab requires ProjectileHitBox.", pattern);
            return null;
        }

        private void FixedUpdate()
        {
            if (!armed || returned || !pattern) return;
            // The first physics timestamp can precede the Update that spawned this volley.
            float elapsed = emissionIndex * pattern.RepeatInterval + Mathf.Max(0f, Time.time - spawnedAt);
            if (!pattern.TryEvaluate(pathIndex, emissionIndex, elapsed, out var position, out var rotation))
            {
                ReturnToPool();
                return;
            }
            body.MovePosition(origin + facing * position);
            body.MoveRotation(facing * rotation);
        }

        private void OnTriggerEnter(Collider other) => TryHit(other);
        private void OnTriggerStay(Collider other) => TryHit(other);

        private void TryHit(Collider other)
        {
            if (!isActiveAndEnabled || !armed || returned) return;
            if (!detachedEnemyProjectile && (!owner || other.transform.IsChildOf(owner.transform))) return;
            if ((detachedEnemyProjectile || owner.GetComponent<Enemy>()) && other.GetComponentInParent<Enemy>()) return;
            var target = HurtboxTarget.Find(other);
            if (target != null)
            {
                bool continuous = pattern && pattern.UseHitWindow;
                if (continuous)
                {
                    float elapsed = Time.time - spawnedAt - pattern.HitStartSeconds;
                    int slice = HitWindow.Slice(elapsed, pattern.HitDurationSeconds,
                        pattern.RepeatHits, pattern.HitRepeatIntervalSeconds);
                    if (hits == null || !hits.Claim(HurtboxTarget.Key(target, other), slice)) return;
                }
                else armed = false;
                var hitVfx = pattern ? pattern.HitVfxPrefab : null;
                float vfxDelay = pattern ? pattern.HitVfxDelaySeconds : 0f;
                var guard = vfxGuard;
                Vector3 point = hitVfx ? other.ClosestPoint(transform.position) : default;
                Quaternion rotation = transform.rotation;
                var result = HurtboxTarget.TakeDamage(target, other, damage, owner);
                if (result is DamageResult.Applied or DamageResult.Killed)
                    VfxAutoDespawn.Play(hitVfx, point, rotation, vfxDelay, guard);
                if (!continuous) ReturnToPool();
            }
            else if (other.gameObject.layer == LayerMask.NameToLayer("Environment")) ReturnToPool();
        }

        public void ReturnToPool()
        {
            if (returned) return;
            returned = true;
            armed = false;
            var callback = onReturned;
            onReturned = null;
            callback?.Invoke(this);
            ObjectPool.Despawn(gameObject);
        }
    }
}
