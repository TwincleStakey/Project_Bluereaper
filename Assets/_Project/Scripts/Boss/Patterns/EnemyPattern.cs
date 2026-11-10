using BlueReaper.BulletPatterns;
using BlueReaper.Data;
using UnityEngine;

[CreateAssetMenu(fileName = "EnemyPattern", menuName = "BlueReaper/EnemyPattern")]
public class EnemyPattern : ScriptableObject
{
    public AnimationClip MotionClip = null;
    public int TelegraphType = 0; //todo
    public float RestTime = 0f;
    public float ChasingDistance = 10f;
    public MeleeAttackPattern MeleeAttack = null;
    public BulletPattern BulletAttack = null;

}
