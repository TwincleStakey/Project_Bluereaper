using BlueReaper.BulletPatterns;
using BlueReaper.Vfx;
using UnityEngine;

namespace BlueReaper.Data
{
    public enum PatternPlacementMode { Default, CaptureTargets, ReuseCaptured }

    [CreateAssetMenu(fileName = "EnemyPatternData", menuName = "BlueReaper/EnemyPattern")]
    public class EnemyPatternData : EnemyActionData
    {
        public bool SelfDestructAfterPattern = false;
        public AnimationClip MotionClip = null;
        [Tooltip("Placement for telegraphs, area effects and bullets in this step.")]
        public PatternPlacementMode PlacementMode;
        public GameObject TelegraphPrefab;
        [Min(0f)] public float TelegraphDurationSeconds;
        public VfxAutoDespawn CastVfxPrefab;
        [Min(0f)] public float CastVfxDelaySeconds;
        public float RestTime = 0f;
        public float ChasingDistance = 10f;
        public PatternImplData[] Implementations = null;
    }
}
