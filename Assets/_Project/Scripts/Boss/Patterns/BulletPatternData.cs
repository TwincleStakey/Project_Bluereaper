using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlueReaper.BulletPatterns
{
    public enum BulletLayout { Manual, Line, Circle, Fan }
    public enum BulletPathPreset { Straight, Corner, Curve }
    public enum BulletMovementType { Path, Linear }

    [Serializable]
    public sealed class BulletPath
    {
        public BulletPathPreset Preset = BulletPathPreset.Straight;
        public Vector3 Start;
        public Vector3 Control = new Vector3(0f, 0f, 5f);
        public Vector3 End = new Vector3(0f, 0f, 10f);

        public Vector3 Evaluate(float progress)
        {
            float t = Mathf.Clamp01(progress);
            switch (Preset)
            {
                case BulletPathPreset.Corner:
                    return t < 0.5f ? Vector3.Lerp(Start, Control, t * 2f)
                        : Vector3.Lerp(Control, End, t * 2f - 1f);
                case BulletPathPreset.Curve:
                    return Vector3.Lerp(Vector3.Lerp(Start, Control, t), Vector3.Lerp(Control, End, t), t);
                default:
                    return Vector3.Lerp(Start, End, t);
            }
        }

        public Vector3 Tangent(float progress)
        {
            float t = Mathf.Clamp01(progress);
            Vector3 direction;
            switch (Preset)
            {
                case BulletPathPreset.Corner:
                    direction = t < 0.5f ? Control - Start : End - Control;
                    break;
                case BulletPathPreset.Curve:
                    direction = Vector3.Lerp(Control - Start, End - Control, t);
                    break;
                default:
                    direction = End - Start;
                    break;
            }
            if (direction.sqrMagnitude < 0.000001f) direction = End - Start;
            return direction.sqrMagnitude < 0.000001f ? Vector3.forward : direction.normalized;
        }
    }

    [CreateAssetMenu(fileName = "BulletPatternData", menuName = "BlueReaper/Bullet Pattern")]
    public sealed class BulletPatternData : PatternImplData
    {
        public GameObject BulletPrefab;
        public BulletMovementType MovementType = BulletMovementType.Path;
        public bool AimAtTargetEachEmission;
        [Min(1)] public int LinearCount = 1;
        [Min(0f)] public float LinearSpeed = 10f;
        [Min(0.01f)] public float LinearLifetime = 2f;
        [Min(0f)] public float StartTime = 0f;
        public BulletLayout Layout = BulletLayout.Line;
        public Vector3 LayoutCenter;
        [Min(0f)] public float Spacing = 1f;
        [Min(0f)] public float Radius = 2f;
        [Range(0f, 360f)] public float FanAngle = 90f;
        // Retained for older assets and empty-path initialization; authored paths keep their edited length.
        [HideInInspector, Min(0f)] public float TravelDistance = 10f;
        [Min(0.01f)] public float TravelDuration = 2f;
        [Min(1)] public int RepeatCount = 1;
        [Min(0f)] public float RepeatInterval = 0.8f;
        public bool DespawnAtEnd = true;
        public bool AlignToPath = true;
        public float DirectionOffset;
        [Min(0f)] public float DamageMultiplier = 1f;
        public List<BulletPath> Paths = new List<BulletPath> { new BulletPath() };

        public int MaximumCount => MovementType == BulletMovementType.Linear ? LinearCount : Paths == null ? 0 : Paths.Count;
        public float TotalDuration => (RepeatCount - 1) * RepeatInterval
            + (MovementType == BulletMovementType.Linear ? LinearLifetime : TravelDuration);

        public void Resize(int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            if (MovementType == BulletMovementType.Linear) { LinearCount = count; return; }
            if (Paths == null) Paths = new List<BulletPath>();
            if (count == Paths.Count) return;
            if (count < Paths.Count)
            {
                Paths.RemoveRange(count, Paths.Count - count);
                return;
            }
            BulletPath last = Paths.Count == 0 ? null : Paths[Paths.Count - 1];
            while (Paths.Count < count)
                Paths.Add(last == null ? CreateDefaultPath() : new BulletPath
                {
                    Preset = last.Preset, Start = last.Start, Control = last.Control, End = last.End
                });
            GenerateLayout();
        }

        public void GenerateLayout()
        {
            if (MovementType == BulletMovementType.Linear || Layout == BulletLayout.Manual) return;
            for (int i = 0; i < MaximumCount; i++)
            {
                BulletPath path = Paths[i] ?? CreateDefaultPath();
                Vector3 controlOffset = path.Control - path.Start;
                Vector3 endOffset = path.End - path.Start;
                GetLayoutSlot(i, out Vector3 start, out float angle);
                Vector3 heading = Vector3.ProjectOnPlane(endOffset, Vector3.up);
                if (heading.sqrMagnitude < 0.000001f) heading = Vector3.ProjectOnPlane(controlOffset, Vector3.up);
                float sourceAngle = heading.sqrMagnitude < 0.000001f ? 0f : Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
                Quaternion rotation = Quaternion.Euler(0f, angle - sourceAngle, 0f);
                path.Start = start;
                path.Control = start + rotation * controlOffset;
                path.End = start + rotation * endOffset;
                Paths[i] = path;
            }
        }

        private BulletPath CreateDefaultPath() => new BulletPath
        {
            Control = Vector3.forward * (TravelDistance * 0.5f), End = Vector3.forward * TravelDistance
        };

        private void GetLayoutSlot(int index, out Vector3 start, out float angle)
        {
            start = LayoutCenter;
            angle = 0f;
            if (Layout == BulletLayout.Line)
                start += Vector3.right * ((index - (MaximumCount - 1) * 0.5f) * Spacing);
            else
            {
                angle = Layout == BulletLayout.Circle ? 360f * index / MaximumCount
                    : Mathf.Approximately(FanAngle, 360f) ? -180f + 360f * index / MaximumCount
                    : MaximumCount == 1 ? 0f : -FanAngle * 0.5f + FanAngle * index / (MaximumCount - 1);
                start += Quaternion.Euler(0f, angle, 0f) * Vector3.forward * Radius;
            }
        }

        public bool TryGetLinearLaunch(int bulletIndex, out Vector3 position, out Vector3 direction)
        {
            position = Vector3.zero;
            direction = Vector3.forward;
            if (MovementType != BulletMovementType.Linear || bulletIndex < 0 || bulletIndex >= MaximumCount
                || Layout < BulletLayout.Line || Layout > BulletLayout.Fan || !Finite(LayoutCenter)
                || !IsFinite(Spacing) || Spacing < 0f || !IsFinite(Radius) || Radius < 0f
                || !IsFinite(FanAngle) || FanAngle < 0f || FanAngle > 360f) return false;
            GetLayoutSlot(bulletIndex, out position, out float angle);
            direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            return Finite(position);
        }

        public bool TryEvaluate(int bulletIndex, int emissionIndex, float elapsed, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (bulletIndex < 0 || bulletIndex >= MaximumCount
                || emissionIndex < 0 || emissionIndex >= RepeatCount || !IsFinite(elapsed)
                || !IsFinite(RepeatInterval) || RepeatInterval < 0f || !IsFinite(DirectionOffset))
                return false;
            float launchTime = emissionIndex * RepeatInterval;
            float age = elapsed - launchTime;
            if (MovementType == BulletMovementType.Linear)
            {
                if (age < 0f || !IsFinite(LinearSpeed) || LinearSpeed < 0f || !IsFinite(LinearLifetime)
                    || LinearLifetime <= 0f || !IsFinite(LinearSpeed * LinearLifetime)
                    || age >= LinearLifetime || Mathf.Approximately(age, LinearLifetime)
                    || !TryGetLinearLaunch(bulletIndex, out Vector3 start, out Vector3 direction)) return false;
                position = start + direction * (LinearSpeed * age);
                rotation = Quaternion.LookRotation(AlignToPath ? direction : Vector3.forward, Vector3.up)
                    * Quaternion.Euler(0f, DirectionOffset, 0f);
                return Finite(position);
            }
            if (MovementType != BulletMovementType.Path || Paths[bulletIndex] == null
                || !IsFinite(TravelDuration) || TravelDuration <= 0f) return false;
            // Repeated float times can put the final frame just below the arrival time.
            if (age < 0f || (DespawnAtEnd && (age >= TravelDuration || Mathf.Approximately(age, TravelDuration)))) return false;
            float progress = Mathf.Clamp01(age / TravelDuration);
            BulletPath path = Paths[bulletIndex];
            if (!Finite(path.Start) || !Finite(path.Control) || !Finite(path.End) || !IsFinite(DirectionOffset)) return false;
            position = path.Evaluate(progress);
            Vector3 forward = AlignToPath ? path.Tangent(progress) : Vector3.forward;
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up;
            rotation = Quaternion.LookRotation(forward, up) * Quaternion.Euler(0f, DirectionOffset, 0f);
            return true;
        }

        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public string GetValidationError()
        {
            if (MovementType != BulletMovementType.Path && MovementType != BulletMovementType.Linear)
                return "이동 방식이 올바르지 않습니다.";
            if (!IsFinite(StartTime) || StartTime < 0f || !IsFinite(StartTime + TotalDuration))
                return "발사 시작 지연은 0 이상의 유한한 값이어야 합니다.";
            if (MaximumCount < 1) return "1회 생성량은 최소 1발이어야 합니다.";
            if (MovementType == BulletMovementType.Path && (!IsFinite(TravelDuration) || TravelDuration <= 0f))
                return "이동 시간은 0보다 큰 유한한 값이어야 합니다.";
            if (MovementType == BulletMovementType.Linear && (!IsFinite(LinearSpeed) || LinearSpeed < 0f
                || !IsFinite(LinearLifetime) || LinearLifetime <= 0f || !IsFinite(LinearSpeed * LinearLifetime)))
                return "선형 속도는 0 이상, 유지 시간은 0보다 큰 유한한 값이어야 합니다.";
            if (Layout < BulletLayout.Manual || Layout > BulletLayout.Fan
                || (MovementType == BulletMovementType.Linear && Layout == BulletLayout.Manual))
                return "선형 탄막은 Line, Circle, Fan 배치를 사용해주세요.";
            if (RepeatCount < 1 || !IsFinite(RepeatInterval) || RepeatInterval < 0f || !IsFinite(TotalDuration))
                return "반복 횟수는 1 이상, 주기는 0 이상이며 전체 시간은 유한해야 합니다.";
            if (!IsFinite(DamageMultiplier) || DamageMultiplier < 0f || !IsFinite(DirectionOffset))
                return "데미지 배율은 0 이상, 방향 보정은 유한한 값이어야 합니다.";
            if (!IsFinite(Spacing) || Spacing < 0f || !IsFinite(Radius) || Radius < 0f
                || !IsFinite(FanAngle) || FanAngle < 0f || FanAngle > 360f
                || (MovementType == BulletMovementType.Path && (!IsFinite(TravelDistance) || TravelDistance < 0f))
                || !Finite(LayoutCenter))
                return "배치 간격·반경·거리는 0 이상, 부채꼴 각도는 0~360도이며 좌표는 유한해야 합니다.";
            if (MovementType == BulletMovementType.Linear) return null;
            foreach (BulletPath path in Paths)
                if (path == null || !Finite(path.Start) || !Finite(path.Control) || !Finite(path.End))
                    return "모든 탄의 시작점·제어점·끝점은 유한한 좌표여야 합니다.";
            return null;
        }

        private static bool Finite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }
}
