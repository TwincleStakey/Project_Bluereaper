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
        public VfxAutoDespawn CastVfxPrefab;
        [Min(0f)] public float CastVfxDelaySeconds;
        public int TelegraphType = 0; //todo
        public float RestTime = 0f;
        public float ChasingDistance = 10f;
        public PatternImplData[] Implementations = null;
    }
}
