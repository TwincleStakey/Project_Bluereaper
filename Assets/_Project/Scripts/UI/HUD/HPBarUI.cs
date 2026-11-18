using BlueReaper.Core;
using System;
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

        private Health targetHealth = null;
        private Shield targetShield = null;

        public void Init(Health target)
        {
            if (targetHealth) targetHealth.Damaged -= OnHealthChanged;
            targetHealth = target;
            if (targetHealth && isActiveAndEnabled) targetHealth.Damaged += OnHealthChanged;
            OnHealthChanged(0f);

            if (targetShield) targetShield.ShieldChanged -= OnShieldChanged;
            targetShield = target.gameObject.GetComponent<Shield>();
            if (targetShield && isActiveAndEnabled) targetShield.ShieldChanged += OnShieldChanged;
                
            shieldFillImage.gameObject.SetActive(targetShield != null);

            OnShieldChanged(0f);
        }

        private void OnEnable()
        {
            if (targetHealth) targetHealth.Damaged += OnHealthChanged;
            OnHealthChanged(0f);

            if (targetShield) targetShield.ShieldChanged += OnShieldChanged;
            OnShieldChanged(0f);
        }

        private void OnDisable()
        {
            if (targetHealth) targetHealth.Damaged -= OnHealthChanged;
            if (targetShield) targetShield.ShieldChanged -= OnShieldChanged;
        }

        private void OnHealthChanged(float damage)
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
                shieldFillImage.fillAmount = targetShield.MaxShield > 0f ? targetShield.CurrentShield / targetShield.MaxShield : 0f;
            }
        }

    }
}
