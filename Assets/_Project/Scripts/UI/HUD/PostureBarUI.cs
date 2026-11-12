using BlueReaper.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PostureBarUI : MonoBehaviour
{
    [SerializeField]
    private Image fillImage = null;
    [SerializeField]
    private TextMeshProUGUI postureText = null;

    private Posture targetPosture = null;

    public void Init(Posture target)
    {
        if (targetPosture) targetPosture.RemoveListener(OnPostureChanged);
        targetPosture = target;
        if (targetPosture && isActiveAndEnabled) targetPosture.AddListener(OnPostureChanged);
        OnPostureChanged(0f);
    }

    private void OnEnable()
    {
        if (targetPosture) targetPosture.AddListener(OnPostureChanged);
        OnPostureChanged(0f);
    }

    private void OnDisable()
    {
        if (targetPosture) targetPosture.RemoveListener(OnPostureChanged);
    }

    private void OnPostureChanged(float current)
    {
        if (!targetPosture) return;
        if (fillImage)
        {
            fillImage.fillAmount = targetPosture.MaxPosture > 0f ? targetPosture.CurrentPosture / targetPosture.MaxPosture : 0f;
        }

        if (postureText)
        {
            postureText.text = $"{targetPosture.CurrentPosture:0}/{targetPosture.MaxPosture:0}";
        }
    }
}