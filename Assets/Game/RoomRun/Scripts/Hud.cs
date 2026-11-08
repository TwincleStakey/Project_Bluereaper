// Runtime-built HUD: health pips, ammo with reload gauge, room name, room map, banner and fade.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RoomRun
{
    public sealed class Hud : MonoBehaviour
    {
        RunDirector run;
        Font font;
        Canvas canvas;
        RectTransform mapRoot, reloadFill;
        Text ammo, roomName, bannerTitle, bannerSub;
        GameObject reloadBar, banner;
        Image fade;
        float nameLeft;

        static readonly Color MapCurrent = new Color(0.62f, 0.91f, 1f);
        static readonly Color MapCleared = new Color(0.72f, 0.74f, 0.72f, 0.9f);
        static readonly Color MapOpen = new Color(0.95f, 0.45f, 0.2f, 0.95f);
        static readonly Color MapUnknown = new Color(1f, 1f, 1f, 0.16f);

        public void Init(RunDirector director)
        {
            run = director;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 32);

            canvas = gameObject.AddComponent<Canvas>();
            // Screen Space Camera so the HUD shows up in captured frames too.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.planeDistance = 1f;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // Top-left belongs to the environment dev panel, so only ammo sits bottom-left.
            ammo = Label("Ammo", transform, 40, TextAnchor.UpperLeft, new Vector2(0, 0), new Vector2(44, 110), new Vector2(380, 56));

            reloadBar = new GameObject("ReloadBar", typeof(RectTransform), typeof(Image));
            var barRect = reloadBar.GetComponent<RectTransform>();
            barRect.SetParent(transform, false);
            Anchor(barRect, new Vector2(0, 0), new Vector2(44, 48), new Vector2(240, 12));
            reloadBar.GetComponent<Image>().color = new Color(1, 1, 1, 0.15f);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            reloadFill = fill.GetComponent<RectTransform>();
            reloadFill.SetParent(barRect, false);
            reloadFill.anchorMin = new Vector2(0, 0);
            reloadFill.anchorMax = new Vector2(0, 1);
            reloadFill.pivot = new Vector2(0, 0.5f);
            reloadFill.anchoredPosition = Vector2.zero;
            fill.GetComponent<Image>().color = MapCurrent;

            roomName = Label("RoomName", transform, 54, TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(-500, -48), new Vector2(1000, 80));
            mapRoot = Rect("Map", transform, new Vector2(1, 1), new Vector2(-150, -120), Vector2.zero);

            banner = new GameObject("Banner", typeof(RectTransform));
            var bannerRect = banner.GetComponent<RectTransform>();
            bannerRect.SetParent(transform, false);
            bannerRect.anchorMin = bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
            bannerRect.sizeDelta = Vector2.zero;
            bannerTitle = Label("Title", bannerRect, 84, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(-800, 10), new Vector2(1600, 130));
            bannerSub = Label("Sub", bannerRect, 38, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(-800, -130), new Vector2(1600, 60));
            banner.SetActive(false);

            var fadeGo = new GameObject("Fade", typeof(RectTransform), typeof(Image));
            var fadeRect = fadeGo.GetComponent<RectTransform>();
            fadeRect.SetParent(transform, false);
            fadeRect.anchorMin = Vector2.zero;
            fadeRect.anchorMax = Vector2.one;
            fadeRect.offsetMin = fadeRect.offsetMax = Vector2.zero;
            fade = fadeGo.GetComponent<Image>();
            fade.color = Color.black;
            fade.raycastTarget = false;
        }

        RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var g = new GameObject(name, typeof(RectTransform));
            var r = g.GetComponent<RectTransform>();
            r.SetParent(parent, false);
            Anchor(r, anchor, pos, size);
            return r;
        }

        static void Anchor(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = new Vector2(0, 1);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        Text Label(string name, Transform parent, int size, TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 box)
        {
            var g = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow));
            var r = g.GetComponent<RectTransform>();
            r.SetParent(parent, false);
            Anchor(r, anchor, pos, box);
            var t = g.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.color = new Color(0.93f, 0.92f, 0.86f);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var s = g.GetComponent<Shadow>();
            s.effectColor = new Color(0, 0, 0, 0.8f);
            s.effectDistance = new Vector2(2, -2);
            return t;
        }

        Image Box(string name, RectTransform parent, Vector2 pos, Vector2 size, Color color)
        {
            var g = new GameObject(name, typeof(RectTransform), typeof(Image));
            var r = g.GetComponent<RectTransform>();
            r.SetParent(parent, false);
            Anchor(r, new Vector2(0, 1), pos, size);
            var i = g.GetComponent<Image>();
            i.color = color;
            i.raycastTarget = false;
            return i;
        }

        void Update()
        {
            if (run.Rig && canvas.worldCamera != run.Rig.Cam) canvas.worldCamera = run.Rig.Cam;
            var p = run.Player;
            if (!p || p.Health == null) return;

            bool combat = run.config.combat;
            ammo.text = !combat ? "" : p.Reloading ? "장전 중" : $"{p.Ammo} / {p.magazine}";
            reloadBar.SetActive(combat && p.Reloading);
            if (p.Reloading) reloadFill.sizeDelta = new Vector2(240f * p.ReloadProgress, 0);

            if (nameLeft > 0f)
            {
                nameLeft -= Time.deltaTime;
                var c = roomName.color;
                c.a = Mathf.Clamp01(nameLeft);
                roomName.color = c;
            }
        }

        public void ShowRoomName(string name)
        {
            roomName.text = name;
            nameLeft = 3f;
            var c = roomName.color;
            c.a = 1f;
            roomName.color = c;
        }

        public void ShowBanner(string title, string sub)
        {
            bannerTitle.text = title;
            bannerSub.text = sub;
            banner.SetActive(true);
        }

        public void HideBanner() => banner.SetActive(false);

        public IEnumerator Fade(float to, float time)
        {
            var c = fade.color;
            float from = c.a;
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                c.a = Mathf.Lerp(from, to, t / time);
                fade.color = c;
                yield return null;
            }
            c.a = to;
            fade.color = c;
        }

        // Shows visited rooms and the rooms their doors lead to, nothing further.
        public void RefreshMap()
        {
            for (int i = mapRoot.childCount - 1; i >= 0; i--) Destroy(mapRoot.GetChild(i).gameObject);
            const float cell = 30f, gap = 8f;

            var known = new HashSet<string>(run.Visited);
            foreach (var id in run.Visited)
                foreach (var d in run.config.Room(id).doors) known.Add(d.targetRoomId);

            foreach (var id in known)
            {
                var r = run.config.Room(id);
                if (r == null) continue;
                var pos = new Vector2(r.mapCell.x * (cell + gap), r.mapCell.y * (cell + gap));
                Color c;
                if (run.Room == r) c = MapCurrent;
                else if (!run.Visited.Contains(id)) c = MapUnknown;
                else if (r.IsCombat && !run.Cleared.Contains(id)) c = MapOpen;
                else c = MapCleared;
                Box("Cell_" + id, mapRoot, pos, new Vector2(cell, cell), c);
            }
        }
    }
}
