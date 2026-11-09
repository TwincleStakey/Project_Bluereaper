using UnityEngine;

namespace BlueReaper.Core
{
    /// <summary>모든 유닛(플레이어·몬스터·보스)이 공유하는 스탯표. 유닛 종류마다 에셋 하나.</summary>
    [CreateAssetMenu(menuName = "BlueReaper/Unit Data", fileName = "UnitData")]
    public class UnitData : ScriptableObject
    {
        [Min(0f)] public float Attack = 10f;
        [Min(1f)] public float MaxHealth = 100f;
        [Min(0f)] public float MoveSpeed = 7f;
    }
}
