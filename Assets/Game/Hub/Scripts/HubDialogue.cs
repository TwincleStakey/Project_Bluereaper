// 거점 shared NPC dialogue box (11-20, Kido: "서생하고 대장장이 말걸면 대사 읽히게해").
// HubDialogue.Show(owner, "서생", lines, onClosed): bottom-centre box with the speaker name and a typewriter reveal.
// The interact key (HubInput) finishes the current line, then goes to the next; after the last line the box closes and
// onClosed runs. Close(owner) shuts it from outside (player walked away). Made on demand, one per scene, uGUI drawn
// through the main camera (Screen Space - Camera) so it also shows in captured frames, like the RoomRun Hud.
// Dev: DevAutoAdvance > 0 turns pages by itself (scripted capture walks can't press keys).
using System;
using UnityEngine;
using UnityEngine.UI;

namespace Hub
{
    public sealed class HubDialogue : MonoBehaviour
    {
        public static bool IsOpen => inst && inst.open;
        public static object Owner => inst ? inst.owner : null;
        public static float DevAutoAdvance;
        public static float CharsPerSecond = 28f;

        static HubDialogue inst;
        Canvas canvas; Text nameText, bodyText, hintText; GameObject panel;
        string[] lines; int index; float shown; bool open; int openFrame; object owner; Action onClosed; float fullAt;

        public static void Show(object owner, string speaker, string[] lines, Action onClosed = null)
        {
            if (lines == null || lines.Length == 0) return;
            Ensure();
            if (inst.open && inst.owner != owner) inst.Finish(false);
            inst.owner = owner; inst.lines = lines; inst.index = 0; inst.shown = 0; inst.onClosed = onClosed;
            inst.open = true; inst.openFrame = Time.frameCount; inst.fullAt = -1f;
            inst.nameText.text = speaker;
            inst.panel.SetActive(true);
            inst.Refresh();
        }

        public static void Close(object owner)
        {
            if (inst && inst.open && inst.owner == owner) inst.Finish(true);
        }

        static void Ensure()
        {
            if (inst) return;
            var go = new GameObject("HubDialogue");
            inst = go.AddComponent<HubDialogue>();
            inst.Build();
        }

        void Build()
        {
            var font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 40);
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.planeDistance = 1f;
            canvas.sortingOrder = 50;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var pr = panel.GetComponent<RectTransform>(); pr.SetParent(transform, false);
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0f); pr.pivot = new Vector2(0.5f, 0f);
            pr.anchoredPosition = new Vector2(0, 56); pr.sizeDelta = new Vector2(1240, 250);
            panel.GetComponent<Image>().color = new Color(0.03f, 0.035f, 0.045f, 0.86f);
            // thin warm frame line on top
            var line = new GameObject("Rule", typeof(RectTransform), typeof(Image));
            var lr = line.GetComponent<RectTransform>(); lr.SetParent(pr, false);
            lr.anchorMin = new Vector2(0, 1); lr.anchorMax = new Vector2(1, 1); lr.pivot = new Vector2(0.5f, 1);
            lr.sizeDelta = new Vector2(0, 3); lr.anchoredPosition = Vector2.zero;
            line.GetComponent<Image>().color = new Color(0.86f, 0.70f, 0.42f, 0.9f);

            nameText = Label("Name", pr, font, 40, new Color(0.95f, 0.78f, 0.45f), TextAnchor.UpperLeft, new Vector2(40, -22), new Vector2(1160, 52));
            nameText.fontStyle = FontStyle.Bold;
            bodyText = Label("Body", pr, font, 38, new Color(0.94f, 0.93f, 0.90f), TextAnchor.UpperLeft, new Vector2(40, -84), new Vector2(1160, 150));
            bodyText.lineSpacing = 1.15f;
            hintText = Label("Hint", pr, font, 26, new Color(1f, 1f, 1f, 0.55f), TextAnchor.LowerRight, new Vector2(40, -200), new Vector2(1160, 36));
            var sh = bodyText.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.8f); sh.effectDistance = new Vector2(2, -2);
            panel.SetActive(false);
        }

        static Text Label(string name, RectTransform parent, Font font, int size, Color color, TextAnchor anchor, Vector2 pos, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var r = go.GetComponent<RectTransform>(); r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = pos; r.sizeDelta = sizeDelta;
            var t = go.GetComponent<Text>(); t.font = font; t.fontSize = size; t.color = color; t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        void Update()
        {
            if (!open) return;
            if (!canvas.worldCamera) canvas.worldCamera = Camera.main;
            var full = lines[index];
            if (shown < full.Length)
            {
                shown = Mathf.Min(full.Length, shown + Time.unscaledDeltaTime * CharsPerSecond);
                if (shown >= full.Length) fullAt = Time.unscaledTime;
            }
            bool press = Time.frameCount != openFrame && HubInput.InteractPressed();
            if (DevAutoAdvance > 0f && shown >= full.Length && Time.unscaledTime - fullAt > DevAutoAdvance) press = true;
            if (press)
            {
                if (shown < full.Length) { shown = full.Length; fullAt = Time.unscaledTime; }
                else if (index + 1 < lines.Length) { index++; shown = 0; fullAt = -1f; }
                else { Finish(true); return; }
            }
            Refresh();
        }

        void Refresh()
        {
            var full = lines[index];
            bodyText.text = full.Substring(0, Mathf.Min(full.Length, Mathf.FloorToInt(shown)));
            bool done = shown >= full.Length;
            hintText.text = done ? (index + 1 < lines.Length ? $"{HubInput.InteractLabel}  ▼" : $"{HubInput.InteractLabel}  닫기") : "";
        }

        void Finish(bool callBack)
        {
            open = false; panel.SetActive(false);
            var cb = onClosed; onClosed = null; owner = null;
            if (callBack) cb?.Invoke();
        }
    }
}
