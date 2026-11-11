using System;
using System.Collections.Generic;
using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.HitBox
{
    public class MeleeHitBox : MonoBehaviour
    {
        private Unit owner;
        private float damage;
        private bool armed;
        private Collider[] hitColliders;
        private Action<IDamageable> onContact;
        private readonly HashSet<IDamageable> hitTargets = new();

        private void Awake()
        {
            hitColliders = GetComponents<Collider>();
            foreach (var hitCollider in hitColliders) hitCollider.isTrigger = true;
            SetArmed(false);
        }

        public void Init(Unit unit, float damageMultiplier, Action<IDamageable> onContact = null)
        {
            owner = unit;
            damage = unit && unit.Data ? DamageFormula.Calculate(unit.Data.Attack, damageMultiplier) : 0f;
            this.onContact = onContact;
            hitTargets.Clear();
        }

        public void SetArmed(bool value)
        {
            armed = value;
            if (hitColliders == null) return;
            foreach (var hitCollider in hitColliders) hitCollider.enabled = value;
        }

        private void OnDisable() => SetArmed(false);
        private void OnTriggerEnter(Collider other) => TryHit(other);
        private void OnTriggerStay(Collider other) => TryHit(other);

        private void TryHit(Collider other)
        {
            if (!armed || !owner || other.transform.IsChildOf(owner.transform)) return;
            if (owner.GetComponent<Enemy>() && other.GetComponentInParent<Enemy>()) return;
            var target = other.GetComponentInParent<IDamageable>();
            if (target == null) return;
            if (onContact != null) onContact(target);
            else if (hitTargets.Add(target)) target.TakeDamage(damage);
        }
    }
}
