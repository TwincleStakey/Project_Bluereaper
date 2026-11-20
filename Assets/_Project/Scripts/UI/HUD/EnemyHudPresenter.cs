using System.Collections.Generic;
using BlueReaper.Core;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace BlueReaper.UI
{
    internal sealed class EnemyHudPresenter
    {
        private const float VisibleSeconds = 3f;
        private readonly RectTransform root;
        private readonly Canvas canvas;
        private readonly EnemyHealthBarView barPrefab;
        private readonly HashSet<Enemy> elites = new();
        private readonly List<(RectTransform Rect, Image Fill)> bars = new();
        private Enemy lastHit;
        private float expiresAt;

        internal EnemyHudPresenter(Canvas canvas, StageHudLayout layout)
        {
            this.canvas = canvas;
            barPrefab = layout?.EnemyHealthPrefab;
            root = layout?.EnemyHealthRoot;
            if (!HudWidgets.IsSceneView(root))
            {
                root = HudWidgets.Rect(canvas.transform, "Enemy HP Follow", new Vector2(.5f, .5f), Vector2.zero);
                HudWidgets.Stretch(root, Vector2.zero);
            }
            foreach (var enemy in Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                SetEnemy(enemy, true);
        }

        internal void SetEnemy(Enemy enemy, bool spawned)
        {
            if (enemy.Rank == EnemyRank.Elite)
            {
                if (spawned) elites.Add(enemy);
                else elites.Remove(enemy);
            }
            if (!spawned && enemy == lastHit) lastHit = null;
        }

        internal void MarkHit(Enemy enemy)
        {
            lastHit = enemy;
            expiresAt = Time.unscaledTime + VisibleSeconds;
        }

        internal void Clear()
        {
            lastHit = null;
            foreach (var bar in bars) bar.Rect.gameObject.SetActive(false);
        }

        internal void Tick(Camera camera, bool visible)
        {
            int used = 0;
            if (visible && camera)
            {
                if (Time.unscaledTime >= expiresAt || !lastHit || !lastHit.isActiveAndEnabled)
                    lastHit = null;
                if (lastHit && Draw(lastHit, camera, used)) used++;
                foreach (var elite in elites)
                    if (Draw(elite, camera, used)) used++;
            }
            for (int i = used; i < bars.Count; i++) bars[i].Rect.gameObject.SetActive(false);
        }

        private bool Draw(Enemy enemy, Camera camera, int index)
        {
            if (!enemy || !enemy.isActiveAndEnabled || !enemy.TryGetComponent(out Health health) || health.IsDead) return false;
            Vector3 point = camera.WorldToScreenPoint(enemy.HudAnchorPosition);
            if (point.z <= 0f || !camera.pixelRect.Contains(point)) return false;
            var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, point, uiCamera, out var position)) return false;
            if (index == bars.Count) bars.Add(CreateBar());
            var view = bars[index];
            view.Rect.localPosition = position;
            float fill = Mathf.Clamp01(health.Current / health.MaxHealth);
            if (view.Fill.type == Image.Type.Filled) view.Fill.fillAmount = fill;
            else view.Fill.rectTransform.anchorMax = new Vector2(fill, 1);
            view.Rect.gameObject.SetActive(true);
            return true;
        }

        private (RectTransform Rect, Image Fill) CreateBar()
        {
            if (barPrefab && barPrefab.IsReady)
            {
                var view = Object.Instantiate(barPrefab, root, false);
                return ((RectTransform)view.transform, view.Fill);
            }

            var rect = HudWidgets.Rect(root, "Enemy HP", new Vector2(.5f, .5f), new Vector2(60, 8));
            HudWidgets.Image(rect, new Color(.26f, .05f, .07f, .85f));
            var fill = HudWidgets.Rect(rect, "Fill", new Vector2(.5f, .5f), Vector2.zero);
            HudWidgets.Stretch(fill, Vector2.zero);
            return (rect, HudWidgets.Image(fill, new Color(.83f, .18f, .18f)));
        }
    }
}
