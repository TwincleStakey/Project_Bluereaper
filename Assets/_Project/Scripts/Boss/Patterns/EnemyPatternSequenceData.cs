using UnityEngine;

namespace BlueReaper.Data
{
    [CreateAssetMenu(fileName = "EnemyPatternSequenceData", menuName = "BlueReaper/EnemyPatternSequenceData")]
    public class EnemyPatternSequenceData : EnemyActionData
    {
        public EnemyPatternData[] Steps = null;
    }
}
