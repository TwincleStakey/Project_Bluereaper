using System.Collections.Generic;
using UnityEngine;

namespace BlueReaper.HitBox
{
    internal sealed class HitWindow
    {
        private readonly HashSet<object> hit = new();
        private int currentSlice = -1;

        internal static int Slice(float elapsed, float duration, bool repeat, float interval)
        {
            if (!(elapsed >= 0f && elapsed < duration) || !float.IsFinite(duration)) return -1;
            if (!repeat) return 0;
            // Keep authored decimal boundaries in their intended slice after float division.
            return interval > 0f && float.IsFinite(interval) ? Mathf.FloorToInt(elapsed / interval + 0.000001f) : -1;
        }

        internal bool Claim(object target, int slice)
        {
            if (slice < 0 || slice < currentSlice) return false;
            if (slice != currentSlice)
            {
                hit.Clear();
                currentSlice = slice;
            }
            return hit.Add(target);
        }

        internal void Reset()
        {
            hit.Clear();
            currentSlice = -1;
        }
    }
}
