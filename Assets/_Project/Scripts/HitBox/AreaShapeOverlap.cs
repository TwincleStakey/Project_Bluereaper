using System;
using UnityEngine;

namespace BlueReaper.HitBox
{
    // Support point tests keep the curved boundary and the attack height in the same 3D query.
    internal readonly struct AreaShapeOverlap
    {
        internal const float ContactTolerance = 0.0001f;
        private readonly Vector3 center;
        private readonly float radius;
        private readonly float halfHeight;
        private readonly Sector first;
        private readonly Sector second;
        private readonly bool split;

        internal AreaShapeOverlap(Vector3 center, Quaternion facing, float radius, float height, float angle)
        {
            this.center = center;
            this.radius = radius;
            halfHeight = height * 0.5f;
            split = angle > 180f && angle < 360f;
            float halfAngle = split ? angle * 0.25f : angle * 0.5f;
            first = new Sector(facing * Quaternion.Euler(0f, split ? -halfAngle : 0f, 0f), halfAngle);
            second = split ? new Sector(facing * Quaternion.Euler(0f, halfAngle, 0f), halfAngle) : default;
        }

        internal bool Intersects(Collider collider)
        {
            if (!HurtShape.TryCreate(collider, center, out var hurt)) return false;
            return IntersectsConvex(hurt, first) || (split && IntersectsConvex(hurt, second));
        }

        internal bool IntersectsDonut(Collider collider, float innerRadius)
        {
            if (!HurtShape.TryCreate(collider, center, out var hurt)) return false;
            bool touchesOuter = hurt.IsBox ? IntersectsConvex(hurt, first)
                : hurt.RoundedSegmentTouchesCylinder(halfHeight, radius);
            // The clipped hurtbox is convex, so it spans all radii between its nearest and farthest points.
            return touchesOuter && (innerRadius <= 0f || hurt.ReachesRadiusInSlab(halfHeight, innerRadius));
        }

        private bool IntersectsConvex(HurtShape hurt, Sector sector)
        {
            // GJK searches for the origin in the difference between the two convex volumes.
            Span<Vector3> simplex = stackalloc Vector3[4];
            Vector3 closest = SupportDifference(hurt, sector, Vector3.right);
            simplex[0] = closest;
            int count = 1;
            for (int i = 0; i < 64; i++)
            {
                float distance = closest.magnitude;
                if (distance <= ContactTolerance) return true;

                Vector3 direction = -closest / distance;
                Vector3 point = SupportDifference(hurt, sector, direction);
                float reach = Vector3.Dot(point, direction);
                if (reach < -ContactTolerance) return false;
                if (distance + reach <= ContactTolerance * 0.01f) return false;

                simplex[count++] = point;
                closest = ReduceSimplex(simplex, ref count);
            }
            return closest.sqrMagnitude <= ContactTolerance * ContactTolerance;
        }

        private Vector3 SupportDifference(HurtShape hurt, Sector sector, Vector3 direction)
        {
            Vector3 point = sector.Support(direction) * radius;
            point.y = direction.y >= 0f ? halfHeight : -halfHeight;
            return point - hurt.Support(-direction);
        }

        private static Vector3 ReduceSimplex(Span<Vector3> points, ref int count)
        {
            if (count == 4 && ContainsOrigin(points)) return Vector3.zero;
            Vector3 closest = points[0];
            int mask = 1;
            for (int i = 0; i < count; i++)
            {
                Consider(points[i], 1 << i, ref closest, ref mask);
                for (int j = i + 1; j < count; j++)
                {
                    Vector3 edge = points[j] - points[i];
                    float t = -Vector3.Dot(points[i], edge) / edge.sqrMagnitude;
                    if (t > 0f && t < 1f)
                        Consider(points[i] + edge * t, (1 << i) | (1 << j), ref closest, ref mask);

                    for (int k = j + 1; k < count; k++)
                    {
                        if (!TryProjectTriangle(points[i], edge, points[k] - points[i], out var projected)) continue;
                        Consider(projected, (1 << i) | (1 << j) | (1 << k), ref closest, ref mask);
                    }
                }
            }
            int activeCount = 0;
            for (int i = 0; i < count; i++)
                if ((mask & (1 << i)) != 0) points[activeCount++] = points[i];
            count = activeCount;
            return closest;
        }

        private static void Consider(Vector3 candidate, int candidateMask, ref Vector3 closest, ref int mask)
        {
            if (candidate.sqrMagnitude >= closest.sqrMagnitude) return;
            closest = candidate;
            mask = candidateMask;
        }

        private static bool TryProjectTriangle(Vector3 a, Vector3 ab, Vector3 ac, out Vector3 point)
        {
            Vector3 normal = Vector3.Cross(ab, ac);
            float denominator = normal.sqrMagnitude;
            point = default;
            if (denominator <= 0f) return false;
            float b = Vector3.Dot(Vector3.Cross(-a, ac), normal) / denominator;
            float c = Vector3.Dot(Vector3.Cross(ab, -a), normal) / denominator;
            if (b <= 0f || c <= 0f || b + c >= 1f) return false;
            point = a + ab * b + ac * c;
            return true;
        }

        private static bool ContainsOrigin(Span<Vector3> points)
        {
            Vector3 ab = points[1] - points[0], ac = points[2] - points[0], ad = points[3] - points[0];
            Vector3 toOrigin = -points[0];
            Vector3 normal = Vector3.Cross(ac, ad);
            float determinant = Vector3.Dot(ab, normal);
            if (determinant == 0f) return false;
            float b = Vector3.Dot(toOrigin, normal) / determinant;
            float c = Vector3.Dot(ab, Vector3.Cross(toOrigin, ad)) / determinant;
            float d = Vector3.Dot(ab, Vector3.Cross(ac, toOrigin)) / determinant;
            return b >= 0f && c >= 0f && d >= 0f && b + c + d <= 1f;
        }

        private readonly struct Sector
        {
            private readonly Vector3 forward, leftEdge, rightEdge;
            private readonly float cosine;

            internal Sector(Quaternion facing, float halfAngle)
            {
                forward = facing * Vector3.forward;
                leftEdge = (facing * Quaternion.Euler(0f, -halfAngle, 0f)) * Vector3.forward;
                rightEdge = (facing * Quaternion.Euler(0f, halfAngle, 0f)) * Vector3.forward;
                cosine = halfAngle >= 180f ? -1f : Mathf.Cos(halfAngle * Mathf.Deg2Rad);
            }

            internal Vector3 Support(Vector3 direction)
            {
                direction.y = 0f;
                float length = direction.magnitude;
                if (length == 0f) return Vector3.zero;
                if (cosine <= -1f || Vector3.Dot(direction, forward) >= length * cosine)
                    return direction / length;

                float left = Vector3.Dot(direction, leftEdge);
                float right = Vector3.Dot(direction, rightEdge);
                if (left <= 0f && right <= 0f) return Vector3.zero;
                return left >= right ? leftEdge : rightEdge;
            }
        }

        private readonly struct HurtShape
        {
            private readonly Vector3 center, x, y, z;
            private readonly float radius;
            internal bool IsBox { get; }

            private HurtShape(Vector3 center, Vector3 x, Vector3 y, Vector3 z, float radius = 0f, bool isBox = false)
            {
                this.center = center;
                this.x = x;
                this.y = y;
                this.z = z;
                this.radius = radius;
                IsBox = isBox;
            }

            internal static bool TryCreate(Collider collider, Vector3 origin, out HurtShape shape)
            {
                Transform transform = collider.transform;
                Vector3 scale = transform.lossyScale;
                scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                switch (collider)
                {
                    case BoxCollider box:
                        Vector3 half = Vector3.Scale(box.size, scale) * 0.5f;
                        shape = new HurtShape(transform.TransformPoint(box.center) - origin,
                            transform.right * half.x, transform.up * half.y, transform.forward * half.z, isBox: true);
                        return true;
                    case SphereCollider sphere:
                        shape = new HurtShape(transform.TransformPoint(sphere.center) - origin, default, default, default,
                            sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)));
                        return true;
                    case CapsuleCollider capsule:
                        int axis = capsule.direction;
                        float radialScale = Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
                        float radius = capsule.radius * radialScale;
                        Vector3 localAxis = Vector3.zero;
                        localAxis[axis] = Mathf.Max(0f, capsule.height * scale[axis] * 0.5f - radius);
                        shape = new HurtShape(transform.TransformPoint(capsule.center) - origin,
                            transform.rotation * localAxis, default, default, radius);
                        return true;
                    default:
                        shape = default;
                        return false;
                }
            }

            internal Vector3 Support(Vector3 direction) =>
                center + Extreme(x, direction) + Extreme(y, direction) + Extreme(z, direction) + direction * radius;

            internal bool ReachesRadiusInSlab(float halfHeight, float innerRadius)
            {
                float required = Mathf.Max(0f, innerRadius - ContactTolerance * 0.5f);
                return IsBox ? BoxReachesRadius(halfHeight, required * required)
                    : RoundedSegmentReachesRadius(center - x, center + x, halfHeight, required);
            }

            internal bool RoundedSegmentTouchesCylinder(float halfHeight, float outerRadius)
            {
                float limit = radius + ContactTolerance;
                float limitSquared = limit * limit;
                if (CylinderDistanceSquared(center - x, halfHeight, outerRadius) <= limitSquared
                    || CylinderDistanceSquared(center + x, halfHeight, outerRadius) <= limitSquared) return true;
                if (x.sqrMagnitude == 0f) return false;

                // Distance to the convex outer cylinder is convex along the capsule's center segment.
                float lower = -1f, upper = 1f;
                for (int i = 0; i < 32; i++)
                {
                    float a = (2f * lower + upper) / 3f;
                    float b = (lower + 2f * upper) / 3f;
                    float distanceA = CylinderDistanceSquared(center + x * a, halfHeight, outerRadius);
                    float distanceB = CylinderDistanceSquared(center + x * b, halfHeight, outerRadius);
                    if (Mathf.Min(distanceA, distanceB) <= limitSquared) return true;
                    if (distanceA < distanceB) upper = b;
                    else lower = a;
                }
                return CylinderDistanceSquared(center + x * ((lower + upper) * 0.5f), halfHeight, outerRadius)
                    <= limitSquared;
            }

            private static float CylinderDistanceSquared(Vector3 point, float halfHeight, float outerRadius)
            {
                float radial = Mathf.Max(0f, Mathf.Sqrt(RadialSquared(point)) - outerRadius);
                float vertical = Mathf.Max(0f, Mathf.Abs(point.y) - halfHeight);
                return radial * radial + vertical * vertical;
            }

            private bool BoxReachesRadius(float halfHeight, float requiredSquared)
            {
                Span<Vector3> corners = stackalloc Vector3[8];
                for (int i = 0; i < corners.Length; i++)
                {
                    Vector3 corner = center + ((i & 1) == 0 ? -x : x)
                        + ((i & 2) == 0 ? -y : y) + ((i & 4) == 0 ? -z : z);
                    if (Mathf.Abs(corner.y) <= halfHeight && RadialSquared(corner) >= requiredSquared) return true;
                    corners[i] = corner;
                }
                // Clipping creates new vertices where the slab planes cross the box edges.
                for (int i = 0; i < corners.Length; i++)
                    for (int bit = 1; bit < corners.Length; bit <<= 1)
                    {
                        if ((i & bit) != 0) continue;
                        Vector3 a = corners[i], b = corners[i | bit];
                        if (a.y == b.y) continue;
                        if (EdgeReachesRadius(a, b, -halfHeight, requiredSquared)
                            || EdgeReachesRadius(a, b, halfHeight, requiredSquared)) return true;
                    }
                return false;
            }

            private static bool EdgeReachesRadius(Vector3 a, Vector3 b, float height, float requiredSquared)
            {
                float t = (height - a.y) / (b.y - a.y);
                return t > 0f && t < 1f && RadialSquared(Vector3.LerpUnclamped(a, b, t)) >= requiredSquared;
            }

            private bool RoundedSegmentReachesRadius(Vector3 a, Vector3 b, float halfHeight, float required)
            {
                float nearestGap = Mathf.Max(0f, Mathf.Max(Mathf.Min(a.y, b.y), -Mathf.Max(a.y, b.y)) - halfHeight);
                if (nearestGap > radius) return false;

                float aRadial = Mathf.Sqrt(RadialSquared(a)), bRadial = Mathf.Sqrt(RadialSquared(b));
                float section = SectionRadius(nearestGap);
                if (Mathf.Max(aRadial, bRadial) + section < required) return false;
                if (aRadial + SectionRadius(Mathf.Max(0f, Mathf.Abs(a.y) - halfHeight)) >= required
                    || bRadial + SectionRadius(Mathf.Max(0f, Mathf.Abs(b.y) - halfHeight)) >= required) return true;

                // The bound differs from the actual section by at most this interval's horizontal length.
                if (RadialSquared(b - a) <= ContactTolerance * ContactTolerance * 0.25f) return true;
                Vector3 midpoint = (a + b) * 0.5f;
                if (midpoint.Equals(a) || midpoint.Equals(b)) return false;
                return RoundedSegmentReachesRadius(a, midpoint, halfHeight, required)
                    || RoundedSegmentReachesRadius(midpoint, b, halfHeight, required);
            }

            private static float RadialSquared(Vector3 point) => point.x * point.x + point.z * point.z;

            private float SectionRadius(float gap) => gap > radius ? float.NegativeInfinity
                : Mathf.Sqrt((radius - gap) * (radius + gap));

            private static Vector3 Extreme(Vector3 axis, Vector3 direction) =>
                Vector3.Dot(axis, direction) >= 0f ? axis : -axis;
        }
    }
}
