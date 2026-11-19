using UnityEngine;

namespace BlueReaper.UI
{
    internal static class HudMotion
    {
        internal static float EaseOut(float progress)
        {
            float remaining = 1f - Mathf.Clamp01(progress);
            return 1f - remaining * remaining;
        }

        internal static float Progress(float started, float duration) =>
            duration <= 0f ? 1f : Mathf.Clamp01((Time.unscaledTime - started) / duration);

        internal static float Value(float from, float to, float progress) =>
            Mathf.LerpUnclamped(from, to, EaseOut(progress));
    }
}
