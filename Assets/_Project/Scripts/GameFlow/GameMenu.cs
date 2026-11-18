using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BlueReaper.GameFlow
{
    public sealed class GameMenu : MonoBehaviour
    {
        static readonly Color Background = new(0.035f, 0.055f, 0.095f);
        static readonly Color Accent = new(0.29f, 0.76f, 0.92f);
        static readonly Color Ink = new(0.06f, 0.1f, 0.16f);
        static readonly Color White = new(0.92f, 0.95f, 0.98f);

        Font font;
        Canvas canvas;
        Camera backgroundCamera;
        EventSystem events;
        RectTransform column;

        void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            backgroundCamera = new GameObject("Menu Camera", typeof(Camera)).GetComponent<Camera>();
            backgroundCamera.transform.SetParent(transform);
            backgroundCamera.depth = -100;
            backgroundCamera.cullingMask = 0;
            backgroundCamera.clearFlags = CameraClearFlags.SolidColor;
            backgroundCamera.backgroundColor = Background;

            var input = new GameObject("Menu Input", typeof(EventSystem), typeof(InputSystemUIInputModule));
            input.transform.SetParent(transform);
            events = input.GetComponent<EventSystem>();

            var overlay = new GameObject("Game Menu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            overlay.transform.SetParent(transform);
            canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = overlay.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        }

        public void Show(string title, string message = null, bool inWorld = false)
        {
            Hide();
            backgroundCamera.enabled = !inWorld;
            canvas.gameObject.SetActive(true);
            events.gameObject.SetActive(true);

            var panel = new GameObject("Menu Panel", typeof(Image));
            var rect = (RectTransform)panel.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = inWorld ? Color.clear : Background;
            panelImage.raycastTarget = !inWorld;

            var group = new GameObject("Menu Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            column = (RectTransform)group.transform;
            column.SetParent(rect, false);
            column.sizeDelta = new Vector2(inWorld ? 300 : 400, 0);
            if (inWorld)
            {
                column.anchorMin = column.anchorMax = Vector2.right;
                column.pivot = Vector2.right;
                column.anchoredPosition = new Vector2(-30, 32);
                group.AddComponent<Image>().color = new Color(Background.r, Background.g, Background.b, 0.92f);
            }
            var layout = group.GetComponent<VerticalLayoutGroup>();
            layout.spacing = inWorld ? 8 : 12;
            if (inWorld) layout.padding = new RectOffset(16, 16, 8, 12);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            group.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            AddText(title, inWorld ? 30 : 44, Accent, inWorld ? 48 : 66).fontStyle = FontStyle.Bold;
            if (!string.IsNullOrWhiteSpace(message)) Label(message);
        }

        public void Hide()
        {
            if (column)
            {
                var panel = column.parent.gameObject;
                panel.SetActive(false);
                Destroy(panel);
                column = null;
            }
            events.SetSelectedGameObject(null);
            events.gameObject.SetActive(false);
            canvas.gameObject.SetActive(false);
            backgroundCamera.enabled = false;
        }

        public void Label(string text) => AddText(text, 22, White, 34);

        public void Button(string text, Action action)
        {
            var row = new GameObject(text, typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(column, false);
            row.GetComponent<LayoutElement>().preferredHeight = 48;
            row.GetComponent<Image>().color = White;
            var button = row.GetComponent<Button>();
            button.targetGraphic = row.GetComponent<Image>();
            button.interactable = action != null;
            var colors = button.colors;
            colors.highlightedColor = Accent;
            colors.selectedColor = Accent;
            button.colors = colors;
            if (action != null) button.onClick.AddListener(() => action());

            var label = CreateText(text, 22, Ink, row.transform);
            label.name = "Label";
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.sizeDelta = Vector2.zero;
            if (button.interactable && !events.currentSelectedGameObject)
                events.SetSelectedGameObject(row);
        }

        public void Input(string placeholder, string value, Action<string> changed, int length)
        {
            var row = DefaultControls.CreateInputField(new DefaultControls.Resources());
            row.name = placeholder;
            row.transform.SetParent(column, false);
            row.AddComponent<LayoutElement>().preferredHeight = 42f;
            var field = row.GetComponent<InputField>();
            field.textComponent.font = font;
            field.textComponent.color = Ink;
            ((Text)field.placeholder).font = font;
            ((Text)field.placeholder).text = placeholder;
            field.characterLimit = length;
            field.SetTextWithoutNotify(value);
            field.onValueChanged.AddListener(text => changed(text));
        }

        public void Volume()
        {
            Text value = AddText($"Master Volume {AudioListener.volume:P0}", 22, White, 34);
            var row = DefaultControls.CreateSlider(new DefaultControls.Resources());
            row.name = "Master Volume";
            row.transform.SetParent(column, false);
            row.AddComponent<LayoutElement>().preferredHeight = 40;
            var slider = row.GetComponent<Slider>();
            row.transform.Find("Background").GetComponent<Image>().color = Ink;
            slider.fillRect.GetComponent<Image>().color = Accent;
            slider.handleRect.GetComponent<Image>().color = White;
            slider.SetValueWithoutNotify(AudioListener.volume);
            slider.onValueChanged.AddListener(volume =>
            {
                AudioListener.volume = volume;
                value.text = $"Master Volume {volume:P0}";
            });
            events.SetSelectedGameObject(row);
        }

        Text AddText(string text, int size, Color color, float height)
        {
            var label = CreateText(text, size, color, column);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return label;
        }

        Text CreateText(string text, int size, Color color, Transform parent)
        {
            var label = new GameObject(text, typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(parent, false);
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            return label;
        }
    }
}
