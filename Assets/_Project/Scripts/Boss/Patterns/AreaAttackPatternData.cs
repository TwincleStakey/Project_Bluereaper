using System;
using BlueReaper.HitBox;
using BlueReaper.Vfx;
using UnityEngine;

namespace BlueReaper.Data
{
    public enum AreaPlacementOrigin { Caster, Field }

    [CreateAssetMenu(menuName = "BlueReaper/Area Attack Pattern")]
    public sealed class AreaAttackPatternData : PatternImplData
    {
        public AreaHitShape Shape;
        public AreaPlacementOrigin PlacementOrigin;
        public Vector3 LocalCenter = Vector3.up;
        public float LocalYaw;
        [Min(0.01f)] public float Height = 4f;
        [Min(0.01f)] public float Radius = 2f;
        [Min(0f)] public float InnerRadius = 1f;
        public Vector2 RectangleSize = new(2f, 4f);
        [Range(1f, 360f)] public float SectorAngle = 120f;
        [Min(0f)] public float PlacementDelaySeconds;
        [Min(0f)] public float HitStartSeconds;
        public bool RepeatHits;
        [Min(0.01f)] public float HitDurationSeconds = 1f;
        [Min(0.01f)] public float HitRepeatIntervalSeconds = 0.2f;
        [Min(0f)] public float DamageMultiplier = 1f;
        public VfxAutoDespawn AttackVfxPrefab;
        public VfxAutoDespawn HitVfxPrefab;
        [Min(0f)] public float HitVfxDelaySeconds;
        public AreaAttackShapeData[] AdditionalShapes = Array.Empty<AreaAttackShapeData>();

        internal Vector3 HitSize => AreaAttackShapeData.GetHitSize(Shape, Height, Radius, RectangleSize);
    }

    [Serializable]
    public sealed class AreaAttackShapeData
    {
        public AreaHitShape Shape;
        public Vector3 LocalCenter = Vector3.up;
        public float LocalYaw;
        [Min(0.01f)] public float Height = 4f;
        [Min(0.01f)] public float Radius = 2f;
        [Min(0f)] public float InnerRadius = 1f;
        public Vector2 RectangleSize = new(2f, 4f);
        [Range(1f, 360f)] public float SectorAngle = 120f;

        internal Vector3 HitSize => GetHitSize(Shape, Height, Radius, RectangleSize);

        internal static Vector3 GetHitSize(AreaHitShape shape, float height, float radius, Vector2 rectangleSize) =>
            shape == AreaHitShape.Rectangle
                ? new Vector3(Mathf.Max(0.01f, rectangleSize.x), Mathf.Max(0.01f, height), Mathf.Max(0.01f, rectangleSize.y))
                : new Vector3(Mathf.Max(0.01f, radius), Mathf.Max(0.01f, height), 0f);
    }
}
