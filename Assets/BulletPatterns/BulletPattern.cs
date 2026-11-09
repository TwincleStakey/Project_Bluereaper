using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlueReaper.BulletPatterns
{
    public enum BulletLayout { Manual, Line, Circle, Fan }
    public enum BulletPathPreset { Straight, Corner, Curve }

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

    [CreateAssetMenu(fileName = "BulletPattern", menuName = "BlueReaper/Bullet Pattern")]
    public sealed class BulletPattern : ScriptableObject
    {
        public GameObject BulletPrefab;
        public BulletLayout Layout = BulletLayout.Line;
        public Vector3 LayoutCenter;
        [Min(0f)] public float Spacing = 1f;
        [Min(0f)] public float Radius = 2f;
        [Range(0f, 360f)] public float FanAngle = 90f;
        [Min(0f)] public float TravelDistance = 10f;
        [Min(0.01f)] public float TravelDuration = 2f;
        [Min(1)] public int RepeatCount = 1;
        [Min(0f)] public float RepeatInterval = 0.8f;
        public bool DespawnAtEnd = true;
        public bool AlignToPath = true;
        public float DirectionOffset;
        [Min(0f)] public float DamageMultiplier = 1f;
        public List<BulletPath> Paths = new List<BulletPath> { new BulletPath() };

        public int MaximumCount => Paths == null ? 0 : Paths.Count;
        public float TotalDuration => (RepeatCount - 1) * RepeatInterval + TravelDuration;

        public void Resize(int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            if (Paths == null) Paths = new List<BulletPath>();
            if (count < Paths.Count) Paths.RemoveRange(count, Paths.Count - count);
            while (Paths.Count < count) Paths.Add(new BulletPath());
        }

        public void GenerateLayout()
        {
            if (Layout == BulletLayout.Manual) return;
            for (int i = 0; i < MaximumCount; i++)
            {
                Vector3 direction = Vector3.forward;
                Vector3 start = LayoutCenter;
                if (Layout == BulletLayout.Line)
                    start += Vector3.right * ((i - (MaximumCount - 1) * 0.5f) * Spacing);
                else
                {
                    float angle = Layout == BulletLayout.Circle ? 360f * i / MaximumCount
                        : MaximumCount == 1 ? 0f : -FanAngle * 0.5f + FanAngle * i / (MaximumCount - 1);
                    direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                    start += direction * Radius;
                }
                BulletPathPreset preset = Paths[i] == null ? BulletPathPreset.Straight : Paths[i].Preset;
                Paths[i] = new BulletPath
                {
                    Preset = preset, Start = start, End = start + direction * TravelDistance,
                    Control = start + direction * TravelDistance * 0.5f
                };
            }
        }

        public bool TryEvaluate(int bulletIndex, int emissionIndex, float elapsed, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (bulletIndex < 0 || bulletIndex >= MaximumCount || Paths[bulletIndex] == null
                || emissionIndex < 0 || emissionIndex >= RepeatCount || !IsFinite(elapsed)
                || !IsFinite(TravelDuration) || TravelDuration <= 0f || !IsFinite(RepeatInterval) || RepeatInterval < 0f)
                return false;
            float launchTime = emissionIndex * RepeatInterval;
            float age = elapsed - launchTime;
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
            if (MaximumCount == 0) return "최소 한 발의 경로가 필요합니다.";
            if (!IsFinite(TravelDuration) || TravelDuration <= 0f) return "이동 시간은 0보다 큰 유한한 값이어야 합니다.";
            if (RepeatCount < 1 || !IsFinite(RepeatInterval) || RepeatInterval < 0f || !IsFinite(TotalDuration))
                return "반복 횟수는 1 이상, 주기는 0 이상이며 전체 시간은 유한해야 합니다.";
            if (!IsFinite(DamageMultiplier) || DamageMultiplier < 0f || !IsFinite(DirectionOffset))
                return "데미지 배율은 0 이상, 방향 보정은 유한한 값이어야 합니다.";
            if (!IsFinite(Spacing) || Spacing < 0f || !IsFinite(Radius) || Radius < 0f
                || !IsFinite(FanAngle) || FanAngle < 0f || FanAngle > 360f
                || !IsFinite(TravelDistance) || TravelDistance < 0f || !Finite(LayoutCenter))
                return "배치 간격·반경·거리는 0 이상, 부채꼴 각도는 0~360도이며 좌표는 유한해야 합니다.";
            foreach (BulletPath path in Paths)
                if (path == null || !Finite(path.Start) || !Finite(path.Control) || !Finite(path.End))
                    return "모든 탄의 시작점·제어점·끝점은 유한한 좌표여야 합니다.";
            return null;
        }

        private static bool Finite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }
}
