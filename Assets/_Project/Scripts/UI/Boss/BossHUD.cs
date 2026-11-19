using System.Collections.Generic;
using BlueReaper.Core;
using BlueReaper.Network;
using TMPro;
using UnityEngine;

namespace BlueReaper.UI
{
    public class BossHUD : MonoBehaviour
    {
        [SerializeField] private HPBarUI hpBarUI;
        [SerializeField] private PostureBarUI postureBarUI;
        [SerializeField] private TextMeshProUGUI bossName;
        [SerializeField] private UnityEngine.UI.Image ghostFill;
        [SerializeField] private RectTransform phaseMarkerPrefab;
        private Health health;
        private Posture posture;
        private EnemyNetworkState netState;
        private BossHpGhostView ghost;
        private string displayedName;
        private readonly List<RectTransform> markers = new();

        public void Init(Enemy enemy)
        {
            if (!enemy || enemy.Rank != EnemyRank.Boss) return;
            Unbind();
            health = enemy.GetComponent<Health>();
            posture = enemy.GetComponent<Posture>();
            netState = enemy.GetComponent<EnemyNetworkState>();
            if (hpBarUI)
            {
                hpBarUI.Init(health);
                if (ghost == null && hpBarUI.FillGraphic) ghost = new BossHpGhostView(hpBarUI.FillGraphic, ghostFill);
            }
            if (postureBarUI) postureBarUI.Init(posture);
            if (!bossName && hpBarUI)
            {
                var anchor = (RectTransform)hpBarUI.transform;
                bossName = HudWidgets.Text(anchor, "Boss Name", 22, TextAlignmentOptions.Center);
                bossName.rectTransform.anchoredPosition = new Vector2(0, anchor.rect.height);
            }
            displayedName = enemy.name.Replace("(Clone)", "").Trim();
            RefreshName();
            if (health) health.DamageApplied += OnDamage;
            if (netState) netState.BossHit += OnBossHit;
            ghost?.Reset(Fill);

            SetPhaseMarkers(enemy.PhaseThresholds);
        }

        public void Show()
        {
            if (hpBarUI) hpBarUI.gameObject.SetActive(true);
            if (postureBarUI) postureBarUI.gameObject.SetActive(posture);
            if (bossName) bossName.gameObject.SetActive(true);
        }

        public void Hide()
        {
            Unbind();
            if (hpBarUI) hpBarUI.gameObject.SetActive(false);
            if (postureBarUI) postureBarUI.gameObject.SetActive(false);
            if (bossName) bossName.gameObject.SetActive(false);
            foreach (var marker in markers) marker.gameObject.SetActive(false);
        }

        private float Fill => health && health.MaxHealth > 0f ? health.Current / health.MaxHealth : 0f;

        private void Update()
        {
            ghost?.Tick(Fill);
            if (health) RefreshName();
        }

        private void SetPhaseMarkers(IReadOnlyList<uint> thresholds)
        {
            if (!hpBarUI || !hpBarUI.FillGraphic) return;
            int index = 0;
            var baseRect = hpBarUI.FillGraphic.rectTransform;
            foreach (uint threshold in thresholds)
            {
                if (threshold is 0 or >= 1000) continue;
                if (index == markers.Count)
                {
                    RectTransform marker;
                    if (phaseMarkerPrefab) marker = Instantiate(phaseMarkerPrefab, baseRect.parent, false);
                    else
                    {
                        marker = HudWidgets.Rect(baseRect.parent, "Boss Phase Threshold", Vector2.zero, new Vector2(2, 0));
                        HudWidgets.Image(marker, new Color(1, 1, 1, .65f));
                    }
                    markers.Add(marker);
                }

                var rect = markers[index++];
                float x = threshold / 1000f;
                rect.anchorMin = new Vector2(x, 0);
                rect.anchorMax = new Vector2(x, 1);
                rect.gameObject.SetActive(true);
            }
            for (; index < markers.Count; index++) markers[index].gameObject.SetActive(false);
        }

        private void RefreshName()
        {
            if (!bossName) return;
            var label = posture && posture.IsGroggy ? $"{displayedName}  ·  Groggy" : displayedName;
            if (bossName.text != label) bossName.text = label;
        }

        private void OnDamage(DamageAppliedInfo damage) => OnBossHit(damage.Before, damage.After);

        private void OnBossHit(float before, float after)
        {
            if (!health || health.MaxHealth <= 0f) return;
            ghost?.Push(before / health.MaxHealth, after / health.MaxHealth);
        }

        private void OnDisable() => Unbind();

        private void Unbind()
        {
            if (health) health.DamageApplied -= OnDamage;
            if (netState) netState.BossHit -= OnBossHit;
            ghost?.Reset(0f);
            health = null;
            posture = null;
            netState = null;
        }
    }
}
