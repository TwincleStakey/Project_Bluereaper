using UnityEngine;

[CreateAssetMenu(fileName = "EnemyPhase", menuName = "BlueReaper/EnemyPhase")]
public class EnemyPhase : ScriptableObject
{
    public uint[] PhaseTrigger_HPRatio_Permil; //0~1000, 1000 = 100%, idx0->phase1
}
