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

            private HurtShape(Vector3 center, Vector3 x, Vector3 y, Vector3 z, float radius = 0f)
            {
                this.center = center;
                this.x = x;
                this.y = y;
                this.z = z;
                this.radius = radius;
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
                            transform.right * half.x, transform.up * half.y, transform.forward * half.z);
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

            private static Vector3 Extreme(Vector3 axis, Vector3 direction) =>
                Vector3.Dot(axis, direction) >= 0f ? axis : -axis;
        }
    }
}
