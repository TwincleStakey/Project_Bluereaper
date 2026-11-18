using System;
using UnityEngine;

namespace BlueReaper.Data
{
    public enum ActionTargetMode { Inherit, Select, None }
    public enum TargetMetric { Aggro, CurrentHealth, HealthRatio, Distance }
    public enum TargetSelectionMode { Top, Random, All }

    [Serializable]
    public sealed class TargetFilter
    {
        public TargetMetric Metric;
        public float Minimum;
        public float Maximum = float.MaxValue;
    }

    [Serializable]
    public struct TargetSort
    {
        public TargetMetric Metric;
        public bool Descending;
    }

    [Serializable]
    public sealed class TargetRule
    {
        public TargetFilter[] Filters = Array.Empty<TargetFilter>();
        public TargetSort[] SortBy = Array.Empty<TargetSort>();
        public TargetSelectionMode SelectionMode;
        [Min(1)] public int Count = 1;
    }
}
