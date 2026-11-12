using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.Data
{
    public enum SummonPositionType { Owner = 0, Target = 1, FieldCenter = 2 }

    //TODO : 현재는 enemy만 받고 있는데 필요에 따라 지형 지물이나 포탑? 같은 오브젝트도 고려해볼것
    [CreateAssetMenu(fileName = "SummonPatternData", menuName = "BlueReaper/SummonPatternData")]
    public class SummonPatternData : PatternImplData
    {
        public SummonPositionType SummonPositionType = SummonPositionType.Owner;
        public Enemy[] SummonPrefab = null;
        public uint[] SummonCount = null;
        [Min(0f)] public float SummonRadius = 0f;

        internal string GetValidationError()
        {
            if (SummonPositionType is not (SummonPositionType.Owner or SummonPositionType.Target or SummonPositionType.FieldCenter))
                return "Unknown summon position type.";
            if (SummonRadius < 0f || float.IsNaN(SummonRadius) || float.IsInfinity(SummonRadius))
                return "Summon radius must be finite and non-negative.";
            if (SummonPrefab == null || SummonCount == null || SummonPrefab.Length == 0
                || SummonPrefab.Length != SummonCount.Length)
                return "Summon prefabs and counts must have the same non-zero length.";

            for (int i = 0; i < SummonPrefab.Length; i++)
            {
                if (!SummonPrefab[i] || SummonCount[i] == 0)
                    return $"Summon entry {i + 1} requires an enemy prefab and a positive count.";
            }

            return null;
        }
    }
}
