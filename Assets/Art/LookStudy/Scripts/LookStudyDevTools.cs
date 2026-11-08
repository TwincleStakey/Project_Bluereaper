// In-game developer panel for the environment art test (top-left, Play mode and builds).
// It adds itself to any scene with a LookStudyWorld, so the scene file needs no changes.
// Everything it edits is restored when it is disabled; shared material assets stay clean.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public class LookStudyDevTools : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (FindAnyObjectByType<LookStudyWorld>() && !FindAnyObjectByType<LookStudyDevTools>())
            new GameObject("LookStudyDevTools").AddComponent<LookStudyDevTools>();
    }

    struct Light0 { public string name; public Vector3 euler, sun, shadow, top, hor, glow, cloud, fog; public float stars, fogDensity, cloudShade; }
    struct Grade { public string name; public float saturation, contrast, exposure; public Color filter; public string dark, light, a, b, c, flower; }
    struct View { public string name; public Vector3 pos, look; public float fov; }

    static Vector3 L(float r, float g, float b) => LookStudyWorld.Lin(r, g, b);

    static readonly Light0[] Lights =
    {
        new Light0{name="영상 톤", euler=new Vector3(42,45,0), sun=new Vector3(1f,1f,0.95f), shadow=new Vector3(0.28f,0.40f,0.72f), top=L(0.30f,0.55f,0.84f), hor=L(0.80f,0.90f,0.95f), glow=L(1f,0.92f,0.72f), cloud=L(0.98f,0.97f,0.95f), fog=L(0.74f,0.84f,0.92f), fogDensity=0.0006f, cloudShade=0.12f},
        new Light0{name="한낮", euler=new Vector3(68,20,0), sun=new Vector3(1.14f,1.1f,0.98f), shadow=new Vector3(0.42f,0.5f,0.66f), top=L(0.22f,0.5f,0.9f), hor=L(0.72f,0.88f,0.98f), glow=L(1f,0.95f,0.8f), cloud=L(1f,1f,0.98f), fog=L(0.74f,0.86f,0.95f), fogDensity=0.0005f, cloudShade=0.1f},
        new Light0{name="아침", euler=new Vector3(22,70,0), sun=new Vector3(1.12f,0.98f,0.78f), shadow=new Vector3(0.42f,0.46f,0.7f), top=L(0.42f,0.58f,0.82f), hor=L(1f,0.82f,0.66f), glow=L(1f,0.78f,0.5f), cloud=L(1f,0.88f,0.78f), fog=L(0.95f,0.84f,0.74f), fogDensity=0.0012f, cloudShade=0.2f},
        new Light0{name="노을", euler=new Vector3(14,300,0), sun=new Vector3(1.2f,0.8f,0.58f), shadow=new Vector3(0.48f,0.36f,0.6f), top=L(0.32f,0.33f,0.62f), hor=L(1f,0.62f,0.42f), glow=L(1f,0.56f,0.28f), cloud=L(0.98f,0.46f,0.36f), fog=L(0.86f,0.60f,0.56f), fogDensity=0.0016f, cloudShade=0.15f},
        new Light0{name="밤", euler=new Vector3(52,40,0), sun=new Vector3(0.3f,0.38f,0.62f), shadow=new Vector3(0.13f,0.17f,0.34f), top=L(0.035f,0.055f,0.2f), hor=L(0.11f,0.15f,0.36f), glow=L(0.22f,0.28f,0.45f), cloud=L(0.1f,0.13f,0.3f), fog=L(0.08f,0.11f,0.26f), stars=1f, fogDensity=0.001f, cloudShade=0f},
        new Light0{name="흐림", euler=new Vector3(60,330,0), sun=new Vector3(0.78f,0.82f,0.84f), shadow=new Vector3(0.58f,0.63f,0.68f), top=L(0.6f,0.66f,0.72f), hor=L(0.78f,0.82f,0.84f), glow=L(0.8f,0.82f,0.82f), cloud=L(0.84f,0.86f,0.88f), fog=L(0.76f,0.8f,0.82f), fogDensity=0.003f, cloudShade=0.35f},
    };

    static readonly Grade[] Grades =
    {
        new Grade{name="기본", saturation=4, contrast=3, exposure=0, filter=Color.white},
        new Grade{name="쨍하게", saturation=26, contrast=14, exposure=0.12f, filter=Color.white},
        new Grade{name="올리브", saturation=-4, contrast=6, exposure=0, filter=Color.white, dark="#1f3b2c", light="#a8a65a", a="#1f3b2c", b="#2b4a30", c="#3f6a3c", flower="#b9a45f"},
        new Grade{name="가을", saturation=8, contrast=8, exposure=0, filter=new Color(1f,0.95f,0.86f), dark="#6b4a1e", light="#e2b04a", a="#4a3318", b="#5a3f1c", c="#6e4c20", flower="#d8562c"},
        new Grade{name="서늘하게", saturation=-6, contrast=8, exposure=-0.05f, filter=new Color(0.85f,0.94f,1.08f)},
        new Grade{name="수묵", saturation=-92, contrast=22, exposure=0.05f, filter=new Color(1f,0.98f,0.92f)},
    };

    static readonly int[] PixelSizes = { 0, 480, 320, 200 };
    static readonly string[] PixelNames = { "끔", "작게", "중간", "크게" };

    LookStudyWorld world;
    Camera cam;
    Light sun;
    Bloom bloom; ColorAdjustments color; Vignette vignette;
    Canvas canvas; GameObject panel; Text fpsText;
    Font font;
    RawImage screen; RenderTexture lowRes; Camera blackCam; Material pixelMat;
    int lightIndex, gradeIndex, pixelIndex;
    bool posterize;
    float fog = -1f, cloudShade = -1f, bloomValue = 0.35f, windMul = 1f, sunTilt, satOffset;
    readonly List<(Button b, System.Func<bool> on)> toggles = new List<(Button, System.Func<bool>)>();
    readonly Dictionary<string, Button> byName = new Dictionary<string, Button>();
    readonly List<Slider> sliders = new List<Slider>();
    readonly List<(Slider s, float def)> sliderDefaults = new List<(Slider, float)>();
    readonly Dictionary<Material, Dictionary<string, Color>> savedColors = new Dictionary<Material, Dictionary<string, Color>>();
    float savedBend = -1f;
    Vector3 camPos0; Quaternion camRot0; float camFov0;
    float fpsT; int fpsN;
    float vd;

    void Start()
    {
        world = FindAnyObjectByType<LookStudyWorld>();
        cam = Camera.main;
        sun = RenderSettings.sun ? RenderSettings.sun : FindAnyObjectByType<Light>();
        if (cam) { camPos0 = cam.transform.position; camRot0 = cam.transform.rotation; camFov0 = cam.fieldOfView; }
        var vol = FindAnyObjectByType<Volume>();
        if (vol)
        {
            var p = vol.profile; // runtime clone, the asset is untouched
            p.TryGet(out bloom); p.TryGet(out color); p.TryGet(out vignette);
            if (bloom) bloomValue = bloom.intensity.value;
        }
        var sh = Resources.Load<Shader>("LookStudyPixelOut");
        if (sh) pixelMat = new Material(sh);
        font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, 14);
        BuildUi();
        ApplyAll();
#if !UNITY_EDITOR
        var args = System.Environment.GetCommandLineArgs();
        int t = System.Array.IndexOf(args, "-lsdevtest");
        if (t >= 0 && t + 1 < args.Length) StartCoroutine(SelfTest(args[t + 1]));
#endif
    }

    void OnDisable()
    {
        SetPixel(0);
        foreach (var kv in savedColors)
            if (kv.Key) foreach (var c in kv.Value) kv.Key.SetColor(c.Key, c.Value);
        if (savedBend >= 0f && world && world.fGrass) world.fGrass.SetFloat("_Bend", savedBend);
        if (pixelMat) Destroy(pixelMat);
    }

    void Update()
    {
        fpsT += Time.unscaledDeltaTime; fpsN++;
        if (fpsT >= 0.5f && fpsText)
        {
            fpsText.text = $"{fpsN / fpsT:0} FPS   {fpsT / fpsN * 1000f:0.0} ms";
            fpsT = 0; fpsN = 0;
        }
    }

    // ---------- effects

    void ApplyAll()
    {
        var l = Lights[lightIndex];
        if (sun) sun.transform.rotation = Quaternion.Euler(Mathf.Clamp(l.euler.x + sunTilt, 4f, 88f), l.euler.y, 0);
        Shader.SetGlobalVector("_LSSunCol", l.sun);
        Shader.SetGlobalVector("_LSShadowMul", l.shadow);
        Shader.SetGlobalVector("_LSSkyTop", l.top);
        Shader.SetGlobalVector("_LSSkyHor", l.hor);
        Shader.SetGlobalVector("_LSSkyGlow", l.glow);
        Shader.SetGlobalVector("_LSCloudCol", l.cloud);
        Shader.SetGlobalVector("_LSFogCol", l.fog);
        Shader.SetGlobalFloat("_LSStars", l.stars);
        Shader.SetGlobalFloat("_LSFogDensity", fog >= 0f ? fog : l.fogDensity);
        Shader.SetGlobalFloat("_LSCloudShade", cloudShade >= 0f ? cloudShade : l.cloudShade);

        var g = Grades[gradeIndex];
        if (color)
        {
            color.saturation.Override(Mathf.Clamp(g.saturation + satOffset, -100f, 100f));
            color.contrast.Override(g.contrast);
            color.postExposure.Override(g.exposure);
            color.colorFilter.Override(g.filter);
        }
        if (bloom) bloom.intensity.Override(bloomValue);
        ApplyPalette(g);
        if (world && world.fGrass)
        {
            if (savedBend < 0f) savedBend = world.fGrass.GetFloat("_Bend");
            world.fGrass.SetFloat("_Bend", savedBend * windMul);
        }
        if (pixelMat) pixelMat.SetFloat("_Levels", posterize ? 14f : 0f);
        RefreshToggles();
    }

    void SetColor(Material m, string prop, string hexOverride)
    {
        if (!m) return;
        if (!savedColors.TryGetValue(m, out var d)) savedColors[m] = d = new Dictionary<string, Color>();
        if (!d.ContainsKey(prop)) d[prop] = m.GetColor(prop);
        if (hexOverride != null && ColorUtility.TryParseHtmlString(hexOverride, out var c)) m.SetColor(prop, c);
        else m.SetColor(prop, d[prop]);
    }

    void ApplyPalette(Grade g)
    {
        if (!world) return;
        SetColor(world.fGrass, "_Dark", g.dark); SetColor(world.fGrass, "_Light", g.light);
        SetColor(world.ground, "_ColA", g.a); SetColor(world.ground, "_ColB", g.b); SetColor(world.ground, "_ColC", g.c);
        SetColor(world.fYellow, "_Light", g.flower);
    }

    void SetPixel(int i)
    {
        pixelIndex = i;
        int h = PixelSizes[i];
        if (!cam) return;
        if (h == 0)
        {
            cam.targetTexture = null;
            if (screen) screen.gameObject.SetActive(false);
            if (blackCam) blackCam.gameObject.SetActive(false);
            if (lowRes) { lowRes.Release(); Destroy(lowRes); lowRes = null; }
            return;
        }
        int w = Mathf.RoundToInt(h * (float)Screen.width / Screen.height);
        if (lowRes) { cam.targetTexture = null; lowRes.Release(); Destroy(lowRes); }
        lowRes = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
        cam.targetTexture = lowRes;
        if (!blackCam)
        {
            blackCam = new GameObject("LS Screen Camera").AddComponent<Camera>();
            blackCam.cullingMask = 0; blackCam.clearFlags = CameraClearFlags.SolidColor; blackCam.backgroundColor = Color.black; blackCam.depth = cam.depth - 1;
            blackCam.transform.SetParent(transform);
        }
        blackCam.gameObject.SetActive(true);
        screen.texture = lowRes;
        screen.gameObject.SetActive(true);
    }

    void SetView(View v)
    {
        if (!cam) return;
        cam.transform.position = v.pos; cam.transform.LookAt(v.look); cam.fieldOfView = v.fov;
    }

    void ResetAll()
    {
        lightIndex = 0; gradeIndex = 0; posterize = false; fog = -1f; cloudShade = -1f; windMul = 1f; sunTilt = 0f; satOffset = 0f;
        SetPixel(0);
        foreach (var (s, def) in sliderDefaults) s.SetValueWithoutNotify(def);
        var b = FindAnyObjectByType<Volume>();
        bloomValue = 0.35f;
        if (cam) { cam.transform.SetPositionAndRotation(camPos0, camRot0); cam.fieldOfView = camFov0; }
        ApplyAll();
    }

    // ---------- UI

    void BuildUi()
    {
        if (!FindAnyObjectByType<EventSystem>())
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.transform.SetParent(transform);
        }
        var cgo = new GameObject("LS DevTools Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.transform.SetParent(transform);
        canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
        scaler.matchWidthOrHeight = 1f;

        var sgo = new GameObject("LS Pixel Screen", typeof(RawImage));
        sgo.transform.SetParent(cgo.transform, false);
        screen = sgo.GetComponent<RawImage>();
        screen.raycastTarget = false;
        if (pixelMat) screen.material = pixelMat;
        var srt = screen.rectTransform; srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one; srt.offsetMin = srt.offsetMax = Vector2.zero;
        sgo.SetActive(false);

        var toggleBtn = MakeButton(cgo.transform, "개발자 도구", null);
        var trt = (RectTransform)toggleBtn.transform;
        trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0, 1);
        trt.anchoredPosition = new Vector2(14, -14); trt.sizeDelta = new Vector2(130, 36);
        byName["개발자 도구"] = toggleBtn;
        toggleBtn.onClick.AddListener(() => panel.SetActive(!panel.activeSelf));

        var fgo = new GameObject("FPS", typeof(Text));
        fgo.transform.SetParent(cgo.transform, false);
        fpsText = fgo.GetComponent<Text>();
        fpsText.font = font; fpsText.fontSize = 16; fpsText.color = Color.white; fpsText.raycastTarget = false;
        var frt = fpsText.rectTransform; frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0, 1);
        frt.anchoredPosition = new Vector2(156, -22); frt.sizeDelta = new Vector2(220, 24);
        fgo.AddComponent<Shadow>();

        panel = new GameObject("Panel", typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel.transform.SetParent(cgo.transform, false);
        panel.GetComponent<Image>().color = new Color(0.07f, 0.09f, 0.1f, 0.88f);
        var prt = (RectTransform)panel.transform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0, 1);
        prt.anchoredPosition = new Vector2(14, -56); prt.sizeDelta = new Vector2(372, 10);
        var lay = panel.GetComponent<VerticalLayoutGroup>();
        lay.padding = new RectOffset(12, 12, 10, 12); lay.spacing = 5;
        lay.childControlWidth = true; lay.childControlHeight = true; lay.childForceExpandHeight = false; lay.childForceExpandWidth = true;
        panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Section("빛");
        var r = NewRow();
        for (int i = 0; i < Lights.Length; i++) { int k = i; if (i == 3) r = NewRow(); Toggle(r, Lights[i].name, () => lightIndex == k, () => { lightIndex = k; ApplyAll(); }); }
        Section("색감");
        r = NewRow();
        for (int i = 0; i < Grades.Length; i++) { int k = i; if (i == 3) r = NewRow(); Toggle(r, Grades[i].name, () => gradeIndex == k, () => { gradeIndex = k; ApplyAll(); }); }
        Section("픽셀 이펙트");
        r = NewRow();
        for (int i = 0; i < PixelSizes.Length; i++) { int k = i; Toggle(r, PixelNames[i], () => pixelIndex == k, () => { SetPixel(k); ApplyAll(); }); }
        r = NewRow();
        Toggle(r, "색 단계 줄이기", () => posterize, () => { posterize = !posterize; ApplyAll(); });

        Section("조절");
        MakeSlider("해 높이", -25f, 25f, 0f, v => { sunTilt = v; ApplyAll(); });
        MakeSlider("채도", -60f, 60f, 0f, v => { satOffset = v; ApplyAll(); });
        MakeSlider("블룸", 0f, 2f, bloomValue, v => { bloomValue = v; ApplyAll(); });
        MakeSlider("안개", 0f, 0.012f, Lights[0].fogDensity, v => { fog = v; ApplyAll(); });
        MakeSlider("구름 그늘", 0f, 1f, Lights[0].cloudShade, v => { cloudShade = v; ApplyAll(); });
        MakeSlider("바람", 0f, 4f, 1f, v => { windMul = v; ApplyAll(); });

        Section("카메라");
        r = NewRow();
        var c = new Vector3(LookStudyWorld.VividX, LookStudyWorld.VividH(LookStudyWorld.VividX, 0f), 0f);
        Plain(r, "위에서", () => SetView(new View { pos = c + new Vector3(-1.5f, 19.5f, -10.5f), look = c + new Vector3(-1.5f, 0, 1.2f), fov = 38 }));
        Plain(r, "낮게", () => SetView(new View { pos = c + new Vector3(-3f, 3.2f, -8f), look = c + new Vector3(1, 0.8f, 6), fov = 45 }));
        Plain(r, "가까이", () => SetView(new View { pos = c + new Vector3(2f, 6.5f, -5f), look = c + new Vector3(3f, 0.3f, -0.6f), fov = 38 }));
        r = NewRow();
        Plain(r, "전부 초기화", ResetAll);
    }

    void Section(string title)
    {
        var go = new GameObject(title, typeof(Text), typeof(LayoutElement));
        go.transform.SetParent(panel.transform, false);
        var t = go.GetComponent<Text>();
        t.text = title; t.font = font; t.fontSize = 15; t.fontStyle = FontStyle.Bold; t.color = new Color(0.72f, 0.86f, 0.74f); t.raycastTarget = false;
        go.GetComponent<LayoutElement>().minHeight = 22;
    }

    Transform NewRow()
    {
        var go = new GameObject("Row", typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(panel.transform, false);
        var h = go.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 4; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true; h.childForceExpandHeight = true;
        go.GetComponent<LayoutElement>().minHeight = 30;
        return go.transform;
    }

    static readonly Color Idle = new Color(0.2f, 0.24f, 0.25f), Active = new Color(0.24f, 0.56f, 0.34f);

    Button MakeButton(Transform parent, string label, System.Action onClick)
    {
        var go = new GameObject(label, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = Idle;
        var b = go.GetComponent<Button>();
        var cs = b.colors; cs.highlightedColor = new Color(1.25f, 1.25f, 1.25f); cs.pressedColor = new Color(0.8f, 0.8f, 0.8f); cs.colorMultiplier = 1f; b.colors = cs;
        if (onClick != null) b.onClick.AddListener(() => onClick());
        var tgo = new GameObject("Text", typeof(Text));
        tgo.transform.SetParent(go.transform, false);
        var t = tgo.GetComponent<Text>();
        t.text = label; t.font = font; t.fontSize = 15; t.alignment = TextAnchor.MiddleCenter; t.color = Color.white; t.raycastTarget = false;
        var rt = t.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        byName[label] = b;
        return b;
    }

    void Plain(Transform row, string label, System.Action onClick) => MakeButton(row, label, onClick);

    void Toggle(Transform row, string label, System.Func<bool> on, System.Action onClick)
    {
        var b = MakeButton(row, label, onClick);
        toggles.Add((b, on));
    }

    void RefreshToggles()
    {
        foreach (var (b, on) in toggles)
            b.GetComponent<Image>().color = on() ? Active : Idle;
    }

    void MakeSlider(string label, float min, float max, float def, System.Action<float> onChange)
    {
        var row = NewRow();
        var lgo = new GameObject("Label", typeof(Text), typeof(LayoutElement));
        lgo.transform.SetParent(row, false);
        var t = lgo.GetComponent<Text>();
        t.text = label; t.font = font; t.fontSize = 14; t.color = Color.white; t.alignment = TextAnchor.MiddleLeft; t.raycastTarget = false;
        var le = lgo.GetComponent<LayoutElement>(); le.preferredWidth = 84; le.flexibleWidth = 0;

        var sgo = new GameObject("Slider", typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
        sgo.transform.SetParent(row, false);
        sgo.GetComponent<LayoutElement>().flexibleWidth = 1;
        var bg = new GameObject("Back", typeof(Image));
        bg.transform.SetParent(sgo.transform, false);
        bg.GetComponent<Image>().color = Idle;
        var brt = (RectTransform)bg.transform; brt.anchorMin = new Vector2(0, 0.4f); brt.anchorMax = new Vector2(1, 0.6f); brt.offsetMin = brt.offsetMax = Vector2.zero;
        var area = new GameObject("Handle Area", typeof(RectTransform));
        area.transform.SetParent(sgo.transform, false);
        var art = (RectTransform)area.transform; art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one; art.offsetMin = new Vector2(8, 0); art.offsetMax = new Vector2(-8, 0);
        var hgo = new GameObject("Handle", typeof(Image));
        hgo.transform.SetParent(area.transform, false);
        hgo.GetComponent<Image>().color = new Color(0.78f, 0.9f, 0.8f);
        ((RectTransform)hgo.transform).sizeDelta = new Vector2(14, 0);
        var s = sgo.GetComponent<Slider>();
        s.handleRect = (RectTransform)hgo.transform; s.targetGraphic = hgo.GetComponent<Image>();
        s.minValue = min; s.maxValue = max; s.value = def;
        s.onValueChanged.AddListener(v => onChange(v));
        sliders.Add(s);
        sliderDefaults.Add((s, def));
    }

    // ---------- self test for builds: real pointer clicks through the Input System, then every preset

#if !UNITY_EDITOR
    IEnumerator SelfTest(string dir)
    {
        Directory.CreateDirectory(dir);
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        Application.runInBackground = true;
        var log = new List<string>();
        yield return new WaitForSeconds(4f);
        yield return Shot(dir, "00_default", log);

        var mouse = Mouse.current;
        log.Add("mouse=" + (mouse != null));
        // Real click on "노을": confirms the panel receives input.
        yield return Click(byName["노을"], mouse);
        log.Add("click 노을 -> lightIndex=" + lightIndex);
        yield return Shot(dir, "01_click_dusk", log);
        yield return Click(byName["개발자 도구"], mouse);
        log.Add("click toggle -> panelActive=" + panel.activeSelf);
        yield return Click(byName["개발자 도구"], mouse);
        log.Add("click toggle -> panelActive=" + panel.activeSelf);

        int n = 2;
        foreach (var name in new[] { "한낮", "아침", "밤", "흐림", "영상 톤" }) { byName[name].onClick.Invoke(); yield return Shot(dir, $"{n++:00}_light_{name}", log); }
        foreach (var name in new[] { "쨍하게", "올리브", "가을", "서늘하게", "수묵", "기본" }) { byName[name].onClick.Invoke(); yield return Shot(dir, $"{n++:00}_grade_{name}", log); }
        foreach (var name in new[] { "작게", "중간", "크게" }) { byName[name].onClick.Invoke(); yield return Shot(dir, $"{n++:00}_pixel_{name}", log); }
        byName["색 단계 줄이기"].onClick.Invoke(); yield return Shot(dir, $"{n++:00}_pixel_posterize", log);
        byName["끔"].onClick.Invoke(); byName["색 단계 줄이기"].onClick.Invoke();
        foreach (var name in new[] { "낮게", "가까이" }) { byName[name].onClick.Invoke(); yield return Shot(dir, $"{n++:00}_view_{name}", log); }
        byName["전부 초기화"].onClick.Invoke();
        yield return Shot(dir, $"{n++:00}_reset", log);
        log.Add($"end light={lightIndex} grade={gradeIndex} pixel={pixelIndex}");
        File.WriteAllLines(Path.Combine(dir, "log.txt"), log);
        Application.Quit();
    }

    IEnumerator Click(Button b, Mouse mouse)
    {
        var rt = (RectTransform)b.transform;
        Vector2 p = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        InputSystem.QueueStateEvent(mouse, new MouseState { position = p });
        yield return null; yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = p }.WithButton(MouseButton.Left, true));
        yield return null; yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = p }.WithButton(MouseButton.Left, false));
        yield return null; yield return null;
    }

    IEnumerator Shot(string dir, string name, List<string> log)
    {
        yield return null;
        yield return new WaitForSeconds(0.5f);
        float t = 0; int f = 0;
        while (t < 1f) { t += Time.unscaledDeltaTime; f++; yield return null; }
        log.Add($"{name}: {f / t:0} fps");
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
        yield return null; yield return null;
    }
#endif
}
