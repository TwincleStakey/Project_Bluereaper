using BlueReaper.Data;
using UnityEngine;

[CreateAssetMenu(fileName = "EnemyPatternSequenceData", menuName = "BlueReaper/EnemyPatternSequenceData")]
public class EnemyPatternSequenceData : EnemyActionData
{
    public EnemyPatternData[] Steps = null;
}
