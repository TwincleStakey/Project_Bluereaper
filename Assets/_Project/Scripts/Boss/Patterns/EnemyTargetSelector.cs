using System.Collections.Generic;
using BlueReaper.Core;
using BlueReaper.Data;
using UnityEngine;

namespace BlueReaper.Boss
{
    public static class EnemyTargetSelector
    {
        public static bool IsAlive(Transform target) =>
            target && target.gameObject.activeInHierarchy
            && target.TryGetComponent<Health>(out var health) && health.isActiveAndEnabled && !health.IsDead;

        public static void Select(Enemy enemy, IReadOnlyList<Transform> candidates, TargetRule rule, List<Transform> results)
        {
            results.Clear();
            if (candidates == null) return;

            var mode = rule?.SelectionMode ?? TargetSelectionMode.Top;
            int count = Mathf.Max(1, rule?.Count ?? 1);
            var filters = rule?.Filters;
            var sortBy = mode == TargetSelectionMode.Random ? null : rule?.SortBy;
            foreach (var target in candidates)
            {
                if (!IsAlive(target) || target == enemy.transform || results.Contains(target) || !Passes(enemy, target, filters)) continue;
                int index = results.Count;
                while (index > 0 && Compare(enemy, target, results[index - 1], sortBy) < 0) index--;
                results.Insert(index, target);
                if (mode == TargetSelectionMode.Top)
                {
                    if (!(sortBy is { Length: > 0 }) && results.Count == count) return;
                    if (results.Count > count) results.RemoveAt(count);
                }
            }

            if (mode != TargetSelectionMode.Random) return;
            count = Mathf.Min(count, results.Count);
            for (int i = 0; i < count; i++)
            {
                int pick = Random.Range(i, results.Count);
                (results[i], results[pick]) = (results[pick], results[i]);
            }
            results.RemoveRange(count, results.Count - count);
        }

        private static bool Passes(Enemy enemy, Transform target, TargetFilter[] filters)
        {
            if (filters == null) return true;
            foreach (var filter in filters)
            {
                if (filter == null) continue;
                float value = Value(enemy, target, filter.Metric);
                if (!(value >= filter.Minimum && value <= filter.Maximum)) return false;
            }
            return true;
        }

        private static int Compare(Enemy enemy, Transform left, Transform right, TargetSort[] order)
        {
            if (order == null) return 0;
            foreach (var sort in order)
            {
                float a = Value(enemy, left, sort.Metric);
                float b = Value(enemy, right, sort.Metric);
                int comparison = sort.Descending ? b.CompareTo(a) : a.CompareTo(b);
                if (comparison != 0) return comparison;
            }
            return 0;
        }

        private static float Value(Enemy enemy, Transform target, TargetMetric metric)
        {
            switch (metric)
            {
                case TargetMetric.Aggro:
                    return enemy.Aggro ? enemy.Aggro.GetAggro(target.GetComponent<Unit>()) : 0f;
                case TargetMetric.CurrentHealth:
                    return target.GetComponent<Health>().Current;
                case TargetMetric.HealthRatio:
                    var health = target.GetComponent<Health>();
                    return health.MaxHealth > 0f ? health.Current / health.MaxHealth : 0f;
                case TargetMetric.Distance:
                    Vector3 offset = target.position - enemy.transform.position;
                    offset.y = 0f;
                    return offset.magnitude;
                default:
                    return float.NaN;
            }
        }
    }
}
