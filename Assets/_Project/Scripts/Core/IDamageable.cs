namespace BlueReaper.Core
{
    /// <summary>데미지를 받을 수 있는 모든 대상 (플레이어, 몬스터, 보스 등).</summary>
    public interface IDamageable
    {
        void TakeDamage(float amount);
    }
}
