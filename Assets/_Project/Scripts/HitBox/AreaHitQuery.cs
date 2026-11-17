using System;
using System.Collections.Generic;
using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.HitBox
{
    public enum AreaHitShape { Circle, Rectangle, Sector }

    internal sealed class AreaHitQuery
    {
        private Collider[] candidates = new Collider[32];
        private readonly HashSet<IDamageable> seen = new();
        private readonly List<(IDamageable Target, Collider Collider)> hits = new();

        // Circle and sector use size.x as radius. Rectangle uses the full width and depth.
        internal List<(IDamageable Target, Collider Collider)> Find(AreaHitShape shape, Vector3 center,
            Quaternion facing, Vector3 size, float angle, Unit source, LayerMask ownerLayers)
        {
            hits.Clear();
            seen.Clear();
            bool isRectangle = shape == AreaHitShape.Rectangle;
            angle = shape == AreaHitShape.Circle ? 360f : Mathf.Clamp(angle, 0f, 360f);
            if (!isRectangle && !(angle > 0f)) return hits;

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
                if (target == null || unit == source || seen.Contains(target)) continue;
                if (!isRectangle && !curvedArea.Intersects(candidate)) continue;

                seen.Add(target);
                hits.Add((target, candidate));
            }
            Array.Clear(candidates, 0, count);
            return hits;
        }
    }
}
