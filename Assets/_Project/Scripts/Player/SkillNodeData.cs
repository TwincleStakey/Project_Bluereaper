using UnityEngine;

namespace BlueReaper.Player
{
    public enum SkillNodeKind { Transform, Core, Boost }

    public enum SkillStat { Attack, AttackRange, MoveSpeed }

    [CreateAssetMenu(menuName = "BlueReaper/Skill Node", fileName = "SkillNode")]
    public sealed class SkillNodeData : ScriptableObject
    {
        [SerializeField] private string displayName;
        [SerializeField] private SkillNodeKind kind = SkillNodeKind.Boost;
        [SerializeField, Min(0)] private int cost = 5;
        [SerializeField] private SkillNodeData parent;
        [SerializeField] private SkillStat stat;
        [SerializeField] private float percent = 20f;

        public string DisplayName => displayName;
        public SkillNodeKind Kind => kind;
        public int Cost => cost;
        public SkillNodeData Parent => parent;
        public SkillStat Stat => stat;
        public float Percent => percent;
    }
}
