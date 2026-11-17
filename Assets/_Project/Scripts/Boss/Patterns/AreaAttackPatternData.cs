using System;
using BlueReaper.HitBox;
using BlueReaper.Vfx;
using UnityEngine;

namespace BlueReaper.Data
{
    public enum AreaPlacementOrigin { Caster, Field }
    public enum AreaExpansionMode { None, Filled, MovingRing }

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
        public bool ContinueAfterPlacement;
        [Min(0f)] public float HitStartSeconds;
        public AreaExpansionMode ExpansionMode;
        [Min(0.01f)] public float ExpansionDurationSeconds = 1f;
        [Range(0f, 1f)] public float ExpansionStartScale;
        [Min(0f)] public float RingStartRadius;
        public bool RepeatHits;
        [Min(0.01f)] public float HitDurationSeconds = 1f;
        [Min(0.01f)] public float HitRepeatIntervalSeconds = 0.2f;
        [Min(0f)] public float DamageMultiplier = 1f;
        public VfxAutoDespawn AttackVfxPrefab;
        public VfxAutoDespawn HitVfxPrefab;
        [Min(0f)] public float HitVfxDelaySeconds;
        public AreaAttackShapeData[] AdditionalShapes = Array.Empty<AreaAttackShapeData>();

        public float ActiveDuration => Mathf.Max(
            RepeatHits ? HitDurationSeconds : 0f,
            ExpansionMode == AreaExpansionMode.None ? 0f : ExpansionDurationSeconds);

        public string GetValidationError()
        {
            if (PlacementOrigin is not (AreaPlacementOrigin.Caster or AreaPlacementOrigin.Field)
                || !float.IsFinite(HitStartSeconds) || !(PlacementDelaySeconds >= 0f
                    && PlacementDelaySeconds <= Mathf.Max(0f, HitStartSeconds))
                || (RepeatHits && (!float.IsFinite(HitDurationSeconds) || !float.IsFinite(HitRepeatIntervalSeconds))))
                return "배치 기준과 판정 시간을 확인하세요. 배치는 첫 판정보다 늦을 수 없습니다.";
            if (ExpansionMode == AreaExpansionMode.None) return null;
            if (ExpansionMode is not (AreaExpansionMode.Filled or AreaExpansionMode.MovingRing)
                || !float.IsFinite(ExpansionDurationSeconds) || ExpansionDurationSeconds <= 0f)
                return "확대 시간은 유한한 양수여야 합니다.";
            if (ExpansionMode == AreaExpansionMode.Filled && !(ExpansionStartScale >= 0f && ExpansionStartScale <= 1f))
                return "시작 크기 비율은 0~1이어야 합니다.";
            if (ExpansionMode == AreaExpansionMode.MovingRing && (Shape != AreaHitShape.Donut
                || AdditionalShapes is { Length: > 0 } || !float.IsFinite(Radius)
                || !(InnerRadius >= 0f && InnerRadius < Radius && RingStartRadius >= 0f
                    && RingStartRadius <= Radius)))
                return "이동 고리는 추가 도형이 없는 도넛이어야 합니다. 안쪽·바깥쪽 반경과 시작 반경을 확인하세요.";
            return null;
        }

        public (Vector3 Size, float InnerRadius) EvaluateHitShape(float elapsed, AreaAttackShapeData additional = null)
        {
            var shape = additional?.Shape ?? Shape;
            float height = additional?.Height ?? Height;
            float outer = additional?.Radius ?? Radius;
            float inner = additional?.InnerRadius ?? InnerRadius;
            Vector2 rectangle = additional?.RectangleSize ?? RectangleSize;
            if (ExpansionMode != AreaExpansionMode.None && ExpansionDurationSeconds > 0f)
            {
                float progress = Mathf.Clamp01(elapsed / ExpansionDurationSeconds);
                if (ExpansionMode == AreaExpansionMode.Filled)
                {
                    float scale = Mathf.Lerp(ExpansionStartScale, 1f, progress);
                    outer *= scale;
                    inner *= scale;
                    rectangle *= scale;
                }
                else if (ExpansionMode == AreaExpansionMode.MovingRing && additional == null)
                {
                    float startInner = Mathf.Max(0f, RingStartRadius - (outer - inner));
                    outer = Mathf.Lerp(RingStartRadius, outer, progress);
                    inner = Mathf.Lerp(startInner, inner, progress);
                }
            }
            return (AreaAttackShapeData.GetHitSize(shape, height, outer, rectangle), inner);
        }
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

        internal static Vector3 GetHitSize(AreaHitShape shape, float height, float radius, Vector2 rectangleSize) =>
            shape == AreaHitShape.Rectangle
                ? new Vector3(Mathf.Max(0.01f, rectangleSize.x), Mathf.Max(0.01f, height), Mathf.Max(0.01f, rectangleSize.y))
                : new Vector3(Mathf.Max(0.01f, radius), Mathf.Max(0.01f, height), 0f);
    }
}
