using BlueReaper.Core;
using UnityEngine;

public class MeleeHitBox : MonoBehaviour
{
    private Unit owner = null;
    private float damageMultiplier = 1f;

    public void Init(Unit unit, float damageMultiplier)
    {
        this.owner = unit;
        this.damageMultiplier = damageMultiplier;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent(out IDamageable damageable))
        {
            damageable.TakeDamage(owner.Data.Attack * damageMultiplier);
        }
    }
}
