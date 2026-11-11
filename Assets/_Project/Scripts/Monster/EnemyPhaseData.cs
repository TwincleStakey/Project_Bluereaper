using UnityEngine;

namespace BlueReaper.Data
{
    [CreateAssetMenu(fileName = "EnemyPhaseData", menuName = "BlueReaper/EnemyPhase")]
    public class EnemyPhaseData : ScriptableObject
    {
        public uint[] PhaseTrigger_HPRatio_Permil; //0~1000, 1000 = 100%, idx0->phase1
    }
}
