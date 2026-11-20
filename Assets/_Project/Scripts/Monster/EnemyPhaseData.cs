using UnityEngine;

namespace BlueReaper.Data
{
    [CreateAssetMenu(fileName = "EnemyPhaseData", menuName = "BlueReaper/EnemyPhase")]
    public class EnemyPhaseData : ScriptableObject
    {
        [Min(0f)] public float TransitionSeconds;
        public uint[] PhaseTrigger_HPRatio_Permil; //0~1000, 1000 = 100%, idx0->phase1
    }
}
