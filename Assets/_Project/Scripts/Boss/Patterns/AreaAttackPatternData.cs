using BlueReaper.HitBox;
using UnityEngine;

namespace BlueReaper.Data
{
    [CreateAssetMenu(menuName = "BlueReaper/Area Attack Pattern")]
    public sealed class AreaAttackPatternData : PatternImplData
    {
        public AreaHitShape Shape;
        public Vector3 LocalCenter = Vector3.up;
        [Min(0.01f)] public float Height = 4f;
        [Min(0.01f)] public float Radius = 2f;
        public Vector2 RectangleSize = new(2f, 4f);
        [Range(1f, 360f)] public float SectorAngle = 120f;
        [Min(0f)] public float HitStartSeconds;
        public bool RepeatHits;
        [Min(0.01f)] public float HitDurationSeconds = 1f;
        [Min(0.01f)] public float HitRepeatIntervalSeconds = 0.2f;
        [Min(0f)] public float DamageMultiplier = 1f;

        internal Vector3 HitSize => Shape == AreaHitShape.Rectangle
            ? new Vector3(Mathf.Max(0.01f, RectangleSize.x), Mathf.Max(0.01f, Height), Mathf.Max(0.01f, RectangleSize.y))
            : new Vector3(Mathf.Max(0.01f, Radius), Mathf.Max(0.01f, Height), 0f);
    }
}
