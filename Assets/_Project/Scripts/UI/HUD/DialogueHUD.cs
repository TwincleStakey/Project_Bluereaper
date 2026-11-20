using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlueReaper.UI
{
    internal sealed class DialogueHUD
    {
        private const float Lifetime = 4.5f;
        private const float Transition = .18f;
        private const float RowHeight = 52f;
        private readonly Row[] rows = new Row[3];
        private readonly float[] rowPositions = new float[3];
        private readonly List<Row> visible = new(3);

        private sealed class Row
        {
            internal RectTransform Rect;
            internal CanvasGroup Group;
            internal TMP_Text Speaker, Line;
            internal Image Portrait;
            internal float Until, Started, FromY, ToY, FromAlpha, ToAlpha;
        }

        internal DialogueHUD(Transform parent, TMP_FontAsset font, StageHudLayout layout)
        {
            var root = layout?.DialogueRoot;
            if (!HudWidgets.IsSceneView(root))
            {
                root = HudWidgets.Rect(parent, "Stage Dialogue", new Vector2(.5f, 0), new Vector2(620, 158));
                root.anchoredPosition = new Vector2(0, 168);
                root.gameObject.AddComponent<RectMask2D>();
            }
            var authored = layout?.DialogueRows;
            for (int i = 0; i < rows.Length; i++)
            {
                var binding = authored != null && i < authored.Length ? authored[i] : default;
                rowPositions[i] = 25f + i * RowHeight;
                if (binding.IsReady)
                {
                    rowPositions[i] = binding.Root.anchoredPosition.y;
                    rows[i] = new Row { Rect = binding.Root, Group = binding.Fade, Speaker = binding.Speaker,
                        Line = binding.Line, Portrait = binding.Portrait };
                    binding.Root.gameObject.SetActive(false);
                    continue;
                }
                if (HudWidgets.IsSceneView(binding.Root)) binding.Root.gameObject.SetActive(false);

                var rect = HudWidgets.Rect(root, "Dialogue Row", new Vector2(.5f, 0), new Vector2(610, 46));
                HudWidgets.Image(rect, new Color(0, 0, 0, .5f));
                var group = rect.gameObject.AddComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                group.interactable = false;
                var speaker = HudWidgets.Text(rect, "Speaker", 20, TextAlignmentOptions.Center);
                speaker.rectTransform.anchorMax = new Vector2(0, 1);
                speaker.rectTransform.offsetMin = new Vector2(0, 0);
                speaker.rectTransform.offsetMax = new Vector2(66, 0);
                var portraitRect = HudWidgets.Rect(rect, "Portrait", new Vector2(0, .5f), new Vector2(40, 40));
                portraitRect.anchoredPosition = new Vector2(32, 0);
                var portrait = HudWidgets.Image(portraitRect, Color.white);
                portrait.preserveAspect = true;
                var line = HudWidgets.Text(rect, "Line", 20, TextAlignmentOptions.MidlineLeft);
                line.rectTransform.offsetMin = new Vector2(72, 4);
                line.rectTransform.offsetMax = new Vector2(-8, -4);
                if (font) line.font = font;
                line.enableAutoSizing = true;
                line.fontSizeMin = 11;
                line.fontSizeMax = 20;
                line.overflowMode = TextOverflowModes.Ellipsis;
                rows[i] = new Row { Rect = rect, Group = group, Speaker = speaker, Line = line, Portrait = portrait };
                rect.gameObject.SetActive(false);
            }
        }

        internal void Show(int slot, Sprite portrait, string text)
        {
            if (visible.Count == rows.Length)
                Remove(visible[0]);
            var row = System.Array.Find(rows, candidate => !candidate.Rect.gameObject.activeSelf);
            row.Speaker.text = slot == 0 ? "" : $"{slot}P";
            row.Speaker.color = PartyHUD.SlotColor(slot);
            row.Speaker.enabled = !portrait;
            row.Portrait.sprite = portrait;
            row.Portrait.enabled = portrait;
            row.Line.text = text;
            row.Until = Time.unscaledTime + Lifetime;
            row.Rect.anchoredPosition = new Vector2(row.Rect.anchoredPosition.x, rowPositions[0] - 21f);
            row.Group.alpha = 0f;
            row.Rect.gameObject.SetActive(true);
            visible.Add(row);
            Arrange();
        }

        internal void Tick()
        {
            bool changed = false;
            for (int i = visible.Count - 1; i >= 0; i--)
            {
                Row row = visible[i];
                float remaining = row.Until - Time.unscaledTime;
                if (remaining <= 0f)
                {
                    Remove(row);
                    changed = true;
                    continue;
                }

                float progress = HudMotion.Progress(row.Started, Transition);
                row.Rect.anchoredPosition = new Vector2(row.Rect.anchoredPosition.x, HudMotion.Value(row.FromY, row.ToY, progress));
                row.Group.alpha = HudMotion.Value(row.FromAlpha, row.ToAlpha, progress)
                    * Mathf.Clamp01(remaining / Transition);
            }
            if (changed) Arrange();
        }

        internal void Clear()
        {
            foreach (var row in rows) row.Rect.gameObject.SetActive(false);
            visible.Clear();
        }

        private void Remove(Row row)
        {
            row.Rect.gameObject.SetActive(false);
            visible.Remove(row);
        }

        private void Arrange()
        {
            for (int i = 0; i < visible.Count; i++)
            {
                Row row = visible[i];
                int age = visible.Count - 1 - i;
                row.Started = Time.unscaledTime;
                row.FromY = row.Rect.anchoredPosition.y;
                row.ToY = rowPositions[age];
                row.FromAlpha = row.Group.alpha;
                row.ToAlpha = age == 0 ? 1f : age == 1 ? .75f : .45f;
            }
        }
    }
}
