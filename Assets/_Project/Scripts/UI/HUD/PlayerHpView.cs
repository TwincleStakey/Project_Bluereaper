using BlueReaper.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlueReaper.UI
{
    public sealed class PlayerHpView : MonoBehaviour
    {
        private PlayerLife life;
        [SerializeField] private Image background;
        [SerializeField] private Graphic zeroHpGraphic;
        private Color normalColor;
        private Sprite circle;
        private Texture2D texture;

        internal void Bind(PlayerLife player)
        {
            life = player;
            if (background) normalColor = background.color;
        }

        internal void BuildFallback(HPBarUI bar)
        {
            if (!bar.FillGraphic || !TryGetComponent(out background)) return;
            var rect = (RectTransform)bar.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.sizeDelta = new Vector2(96, 96);
            rect.anchoredPosition = new Vector2(76, 76);
            texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var pixels = new Color32[64 * 64];
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f));
                pixels[y * 64 + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(32f - distance)));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            circle = Sprite.Create(texture, new Rect(0, 0, 64, 64), new Vector2(.5f, .5f), 64f);
            background.sprite = circle;
            var fill = bar.FillGraphic;
            fill.sprite = circle;
            fill.color = new Color(.8f, .12f, .18f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Vertical;
            fill.fillOrigin = 0;
            if (bar.NumberGraphic) bar.NumberGraphic.gameObject.SetActive(false);
            var shield = bar.ShieldLayout;
            if (shield)
            {
                shield.anchorMin = shield.anchorMax = new Vector2(.5f, 0);
                shield.sizeDelta = new Vector2(64, 6);
                shield.anchoredPosition = new Vector2(0, -7);
            }
            background.color = new Color(.24f, .09f, .13f);
            var cracked = HudWidgets.Text(rect, "Zero HP", 46, TextAlignmentOptions.Center);
            cracked.text = "/";
            cracked.color = new Color(.76f, .80f, .88f);
            zeroHpGraphic = cracked;
        }

        private void LateUpdate()
        {
            if (!life) return;
            bool zero = life.State != LifeState.Alive;
            if (background) background.color = zero ? Color.black : normalColor;
            if (zeroHpGraphic) zeroHpGraphic.gameObject.SetActive(zero);
        }

        private void OnDestroy()
        {
            if (circle) Destroy(circle);
            if (texture) Destroy(texture);
        }
    }
}
