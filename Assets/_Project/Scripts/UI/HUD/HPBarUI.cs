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

        private Health targetHealth = null;

        public void Init(Health target)
        {
            if (targetHealth) Unsubscribe();
            targetHealth = target;
            if (targetHealth && isActiveAndEnabled) Subscribe();
            OnHealthChanged(0f);
        }

        private void OnEnable()
        {
            if (targetHealth) Subscribe();
            OnHealthChanged(0f);
        }

        private void OnDisable()
        {
            if (targetHealth) Unsubscribe();
        }

        // 부활(구조)하면 HP가 0에서 다시 차므로 Revived도 받아야 바가 갱신된다
        private void Subscribe()
        {
            targetHealth.Damaged += OnHealthChanged;
            targetHealth.Revived += OnRevived;
        }

        private void Unsubscribe()
        {
            targetHealth.Damaged -= OnHealthChanged;
            targetHealth.Revived -= OnRevived;
        }

        private void OnRevived() => OnHealthChanged(0f);

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
    }
}
