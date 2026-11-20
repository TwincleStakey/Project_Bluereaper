using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlueReaper.UI
{
    internal static class HudWidgets
    {
        internal static bool IsSceneView(Component view) => view && view.gameObject.scene.IsValid();

        internal static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = size;
            return rect;
        }

        internal static Image Image(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        internal static void Stretch(RectTransform rect, Vector2 inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = inset;
            rect.offsetMax = -inset;
        }

        internal static TextMeshProUGUI Text(Transform parent, string name, int size, TextAlignmentOptions alignment)
        {
            var rect = Rect(parent, name, new Vector2(.5f, .5f), Vector2.zero);
            Stretch(rect, Vector2.zero);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = size;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.richText = false;
            return label;
        }
    }
}
