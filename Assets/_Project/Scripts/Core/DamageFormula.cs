namespace BlueReaper.Core
{
    /// <summary>데미지 계산식. 모든 공격이 이 식을 거친다 (치명타·방어력 등은 여기에 추가).</summary>
    public static class DamageFormula
    {
        public static float Calculate(float attack, float multiplier) => attack * multiplier;
    }
}
