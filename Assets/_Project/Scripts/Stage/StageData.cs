using UnityEngine;

namespace BlueReaper.Stage
{
    [CreateAssetMenu(fileName = "StageData", menuName = "BlueReaper/StageData")]
    public class StageData : ScriptableObject
    {
        public StageField[] Fields = null;
    }
}
