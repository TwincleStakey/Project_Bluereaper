namespace BlueReaper.Core
{
    public enum DamageResult { Ignored, Applied, Killed }

    /// <summary>데미지를 받을 수 있는 모든 대상 (플레이어, 몬스터, 보스 등).</summary>
    public interface IDamageable
    {
        DamageResult TakeDamage(float amount);
    }
}
