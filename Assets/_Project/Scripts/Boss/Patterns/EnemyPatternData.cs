using BlueReaper.BulletPatterns;
using UnityEngine;

namespace BlueReaper.Data
{
    [CreateAssetMenu(fileName = "EnemyPatternData", menuName = "BlueReaper/EnemyPattern")]
    public class EnemyPatternData : EnemyActionData
    {
        public bool SelfDestructAfterPattern = false;
        public AnimationClip MotionClip = null;
        public int TelegraphType = 0; //todo
        public float RestTime = 0f;
        public float ChasingDistance = 10f;
        public PatternImplData[] Implementions = null;
    }
}
