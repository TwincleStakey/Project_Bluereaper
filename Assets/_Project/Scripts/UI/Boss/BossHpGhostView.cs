using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlueReaper.UI
{
    internal sealed class BossHpGhostView
    {
        private const float Hold = .25f;
        private const float Move = .22f;
        private const float MaxDuration = 1f;
        private readonly Queue<(float Target, float Ready)> pending = new();
        private readonly Image image;
        private float deadline, started, duration, from, to, latest;
        private bool moving;

        internal BossHpGhostView(Image main, Image authored)
        {
            if (HudWidgets.IsSceneView(authored))
            {
                image = authored;
                Reset(1f);
                return;
            }

            var rect = HudWidgets.Rect(main.transform.parent, "Boss HP Ghost", new Vector2(.5f, .5f), Vector2.zero);
            rect.anchorMin = main.rectTransform.anchorMin;
            rect.anchorMax = main.rectTransform.anchorMax;
            rect.pivot = main.rectTransform.pivot;
            rect.offsetMin = main.rectTransform.offsetMin;
            rect.offsetMax = main.rectTransform.offsetMax;
            rect.SetSiblingIndex(main.transform.GetSiblingIndex());
            image = HudWidgets.Image(rect, new Color(1f, .82f, .42f));
            image.sprite = main.sprite;
            image.type = Image.Type.Filled;
            image.fillMethod = main.fillMethod;
            image.fillOrigin = main.fillOrigin;
            Reset(1f);
        }

        internal void Push(float before, float after)
        {
            if (after >= before) return;
            float now = Time.unscaledTime;
            if (!moving && pending.Count == 0)
            {
                deadline = now + MaxDuration;
                image.fillAmount = before;
                image.enabled = true;
            }

            latest = after;
            pending.Enqueue((after, now + Hold));
        }

        internal void Tick(float current)
        {
            if (!image.enabled) return;
            float now = Time.unscaledTime;
            if (now >= deadline)
            {
                Reset(latest);
                return;
            }
            if (moving)
            {
                float progress = HudMotion.Progress(started, duration);
                image.fillAmount = Mathf.Max(current, HudMotion.Value(from, to, progress));
                if (progress < 1f) return;
                moving = false;
            }
            if (pending.Count == 0)
            {
                Reset(latest);
                return;
            }

            float remaining = deadline - now;
            bool combine = pending.Count >= 5 || pending.Count * Move > remaining;
            if (!combine && pending.Peek().Ready > now) return;

            var next = pending.Dequeue();
            if (combine)
                for (int i = 1; i < 3 && pending.Count > 0; i++) next = pending.Dequeue();
            from = image.fillAmount;
            to = next.Target;
            started = now;
            duration = combine ? Mathf.Min(.12f, remaining) : Mathf.Min(Move, remaining);
            moving = true;
        }

        internal void Reset(float current)
        {
            pending.Clear();
            moving = false;
            image.fillAmount = current;
            image.enabled = false;
        }
    }
}
