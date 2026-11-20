using BlueReaper.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlueReaper.UI
{
    //health 컴포넌트의 Current/Max를 읽어서 fillImage.fillAmount를 갱신하고, hpText.text를 갱신, 각 hud는 사용할 hpbarui를 선택해서 Init을 호출해야함

    public class HPBarUI : MonoBehaviour
    {
        [SerializeField]
        private Image fillImage = null;
        [SerializeField]
        private TextMeshProUGUI hpText = null;
        [SerializeField]
        private Image shieldFillImage = null;

        internal Image FillGraphic => fillImage;
        internal RectTransform ShieldLayout => fallbackShieldRoot ? fallbackShieldRoot
            : shieldFillImage ? shieldFillImage.rectTransform : null;
        internal TextMeshProUGUI NumberGraphic => hpText;

        private RectTransform fallbackShieldRoot;
        private Health targetHealth = null;
        private Shield targetShield = null;

        internal static HPBarUI CreateFallback(Transform parent)
        {
            var root = HudWidgets.Rect(parent, "Player HP", Vector2.zero, new Vector2(96, 96));
            HudWidgets.Image(root, Color.black);
            var hp = HudWidgets.Rect(root, "Gauge", new Vector2(.5f, .5f), Vector2.zero);
            HudWidgets.Stretch(hp, Vector2.zero);
            var shield = HudWidgets.Rect(root, "Shield", new Vector2(.5f, 0), new Vector2(64, 6));
            var bar = root.gameObject.AddComponent<HPBarUI>();
            bar.fillImage = HudWidgets.Image(hp, Color.red);
            bar.fallbackShieldRoot = shield;
            var shieldFill = HudWidgets.Rect(shield, "Shield Fill", new Vector2(.5f, .5f), Vector2.zero);
            HudWidgets.Stretch(shieldFill, Vector2.zero);
            bar.shieldFillImage = HudWidgets.Image(shieldFill, new Color(.45f, .8f, 1f));
            return bar;
        }

        public void Init(Health target)
        {
            if (targetHealth)
                targetHealth.Changed -= OnHealthChanged;
            targetHealth = target;
            if (targetHealth && isActiveAndEnabled) SubscribeToHealth();
            OnHealthChanged();

            if (targetShield) targetShield.ShieldChanged -= OnShieldChanged;
            targetShield = target ? target.GetComponent<Shield>() : null;
            if (targetShield && isActiveAndEnabled) targetShield.ShieldChanged += OnShieldChanged;
                
            if (shieldFillImage) shieldFillImage.gameObject.SetActive(targetShield != null);

            OnShieldChanged(0f);
        }

        private void OnEnable()
        {
            if (targetHealth) SubscribeToHealth();
            OnHealthChanged();

            if (targetShield) targetShield.ShieldChanged += OnShieldChanged;
            OnShieldChanged(0f);
        }

        private void OnDisable()
        {
            if (targetHealth)
                targetHealth.Changed -= OnHealthChanged;
            if (targetShield) targetShield.ShieldChanged -= OnShieldChanged;
        }

        private void SubscribeToHealth()
        {
            targetHealth.Changed += OnHealthChanged;
        }

        private void OnHealthChanged()
        {
            if (!targetHealth) return;
            if (fillImage)
            {
                fillImage.fillAmount = targetHealth.MaxHealth > 0f ? targetHealth.Current / targetHealth.MaxHealth : 0f;
            }

            if(hpText)
            {
                hpText.text = $"{targetHealth.Current:0}/{targetHealth.MaxHealth:0}";
            }
        }

        private void OnShieldChanged(float obj)
        {
            if (!targetShield) return;
            if(shieldFillImage)
            {
                float fill = targetShield.MaxShield > 0f ? targetShield.CurrentShield / targetShield.MaxShield : 0f;
                if (fallbackShieldRoot) shieldFillImage.rectTransform.anchorMax = new Vector2(fill, 1f);
                else shieldFillImage.fillAmount = fill;
            }
        }

    }
}
