using System;
using System.Collections.Generic;
using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.HitBox
{
    public enum AreaHitShape { Circle, Rectangle, Sector, Donut }

    internal sealed class AreaHitQuery
    {
        private Collider[] candidates = new Collider[32];
        private readonly HashSet<object> seen = new();
        private readonly List<(IDamageable Target, Collider Collider)> hits = new();

        internal List<(IDamageable Target, Collider Collider)> Find(AreaHitShape shape, Vector3 center,
            Quaternion facing, Vector3 size, float angle, Unit source, LayerMask ownerLayers, float innerRadius = 0f)
        {
            hits.Clear();
            seen.Clear();
            Append(shape, center, facing, size, angle, source, ownerLayers, innerRadius);
            return hits;
        }

        // Curved shapes use size.x as the outer radius. Rectangles use the full width and depth.
        internal void Append(AreaHitShape shape, Vector3 center, Quaternion facing, Vector3 size,
            float angle, Unit source, LayerMask ownerLayers, float innerRadius)
        {
            bool isRectangle = shape == AreaHitShape.Rectangle;
            bool isDonut = shape == AreaHitShape.Donut;
            angle = shape is AreaHitShape.Circle or AreaHitShape.Donut ? 360f : Mathf.Clamp(angle, 0f, 360f);
            if (!isRectangle && !(angle > 0f)) return;
            if (isDonut && !(innerRadius >= 0f && innerRadius < size.x && !float.IsInfinity(size.x))) return;

            Vector3 halfExtents = isRectangle ? size * 0.5f
                : new Vector3(size.x, size.y * 0.5f, size.x) + Vector3.one * AreaShapeOverlap.ContactTolerance;
            int count;
            while ((count = Physics.OverlapBoxNonAlloc(center, halfExtents, candidates, facing,
                HurtboxTarget.Mask, QueryTriggerInteraction.Collide)) == candidates.Length)
                Array.Resize(ref candidates, candidates.Length * 2);

            var curvedArea = isRectangle ? default : new AreaShapeOverlap(center, facing, size.x, size.y, angle);
            for (int i = 0; i < count; i++)
            {
                Collider candidate = candidates[i];
                var target = HurtboxTarget.Find(candidate, ownerLayers, out var unit);
                if (target == null || unit == source) continue;
                var key = HurtboxTarget.Key(target, candidate);
                if (seen.Contains(key)) continue;
                bool overlaps = isRectangle || (isDonut ? curvedArea.IntersectsDonut(candidate, innerRadius)
                    : curvedArea.Intersects(candidate));
                if (!overlaps) continue;

                seen.Add(key);
                hits.Add((target, candidate));
            }
            Array.Clear(candidates, 0, count);
        }
    }
}
