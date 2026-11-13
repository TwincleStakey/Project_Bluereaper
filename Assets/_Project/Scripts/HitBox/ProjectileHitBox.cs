using System;
using BlueReaper.BulletPatterns;
using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.HitBox
{
    public class ProjectileHitBox : MonoBehaviour
    {
        private Unit owner;
        private float damage;
        private bool detachedEnemyProjectile;
        private bool armed, returned;
        private Rigidbody body;
        private BulletPatternData pattern;
        private int pathIndex, emissionIndex;
        private float spawnedAt;
        private Vector3 origin;
        private Quaternion facing;
        private Action<ProjectileHitBox> onReturned;

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
        }

        private void OnDisable()
        {
            armed = false;
            returned = true;
            var callback = onReturned;
            onReturned = null;
            callback?.Invoke(this);
        }

        public void Init(Unit unit, float damageMultiplier)
        {
            owner = unit;
            detachedEnemyProjectile = false;
            damage = unit && unit.Data ? DamageFormula.Calculate(unit.Data.Attack, damageMultiplier) : 0f;
            returned = false;
            armed = true;
        }

        public void Init(Unit unit, BulletPatternData pattern, int pathIndex, int emissionIndex,
            Vector3 origin, Quaternion facing, Action<ProjectileHitBox> onReturned)
        {
            Init(unit, pattern.DamageMultiplier);
            InitFlight(pattern, pathIndex, emissionIndex, origin, facing, onReturned);
        }

        internal void InitDeathProjectile(float damage, BulletPatternData pattern, int pathIndex, int emissionIndex,
            Vector3 origin, Quaternion facing, Action<ProjectileHitBox> onReturned)
        {
            owner = null;
            detachedEnemyProjectile = true;
            this.damage = damage;
            returned = false;
            armed = true;
            InitFlight(pattern, pathIndex, emissionIndex, origin, facing, onReturned);
        }

        private void InitFlight(BulletPatternData pattern, int pathIndex, int emissionIndex,
            Vector3 origin, Quaternion facing, Action<ProjectileHitBox> onReturned)
        {
            this.pattern = pattern;
            this.pathIndex = pathIndex;
            this.emissionIndex = emissionIndex;
            spawnedAt = Time.time;
            this.origin = origin;
            this.facing = facing;
            this.onReturned = onReturned;
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
            if (!armed || returned) return;
            if (!detachedEnemyProjectile && (!owner || other.transform.IsChildOf(owner.transform))) return;
            if ((detachedEnemyProjectile || owner.GetComponent<Enemy>()) && other.GetComponentInParent<Enemy>()) return;
            var target = other.GetComponentInParent<IDamageable>();
            if (target != null)
            {
                armed = false;
                target.TakeDamage(damage);
                ReturnToPool();
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
