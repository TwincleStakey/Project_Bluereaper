using BlueReaper.BulletPatterns;
using BlueReaper.Vfx;
using UnityEngine;

namespace BlueReaper.Data
{
    [CreateAssetMenu(fileName = "EnemyPatternData", menuName = "BlueReaper/EnemyPattern")]
    public class EnemyPatternData : EnemyActionData
    {
        public bool SelfDestructAfterPattern = false;
        public AnimationClip MotionClip = null;
        public GameObject TelegraphPrefab;
        [Min(0f)] public float TelegraphDurationSeconds;
        public VfxAutoDespawn CastVfxPrefab;
        [Min(0f)] public float CastVfxDelaySeconds;
        public float RestTime = 0f;
        public float ChasingDistance = 10f;
        public PatternImplData[] Implementations = null;
    }
}
