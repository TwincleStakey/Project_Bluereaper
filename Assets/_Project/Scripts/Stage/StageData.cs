using UnityEngine;

namespace BlueReaper.Stage
{
    [CreateAssetMenu(fileName = "StageData", menuName = "BlueReaper/StageData")]
    public class StageData : ScriptableObject
    {
        [Min(1)] public int StageId;
        public string DisplayName;
        public StageField[] Fields = null;

        [Tooltip("Boss HP multiplier for 1, 2, and 3 players.")]
        public Vector3 BossHealthMultipliersByPartySize = Vector3.one;
        [Min(0.01f)] public float SharedHealthMultiplier = 1f;
        [Min(0.01f)] public float LastBreathHealthMultiplier = 0.25f;

        public string Title => string.IsNullOrWhiteSpace(DisplayName) ? name.Replace('_', ' ') : DisplayName;

        internal bool HasFields
        {
            get
            {
                if (Fields == null || Fields.Length == 0) return false;
                foreach (StageField stageField in Fields)
                    if (!stageField) return false;
                return true;
            }
        }
    }
}
