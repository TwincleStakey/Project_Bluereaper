using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Underworld
{
    // Environment mood control.
    // deathTone 0 = saturated violet underworld, 1 = toned-down grey "land of the dead".
    // haze 0..1 = thicker, milkier fog (denser scene fog, brighter lifted blacks, stronger fog sheets).
    [ExecuteAlways]
    public class UWEnvironment : MonoBehaviour
    {
        [Range(0f, 1f)] public float deathTone = 0f;
        [Range(0f, 1f)] public float haze = 0f;
        public Volume volume;
        // Gameplay readability (play mode only): opens shadows, thins fog and vignette so floor and props read
        // from the RoomRun follow camera. 0 = pure look-dev grade, 1 = fully opened.
        [Range(0f, 1f)] public float playReadable = 0.5f;
        // In play:  toggles original grade <-> readability, - / = step readability by 0.1. Saved between runs.
        const string ReadKey = "UW_Readable", ReadOnKey = "UW_ReadableOn";
        public bool readableOn = true;
        float lastBoost = float.NaN;
        public float PlayBoost => Application.isPlaying && readableOn ? playReadable : 0f;

        [Serializable]
        public struct FogMat { public Material mat; public Color violet; public Color grey; }
        public FogMat[] fogMaterials = Array.Empty<FogMat>();

        public Color fogViolet = new(0.145f, 0.118f, 0.2f), fogGrey = new(0.26f, 0.25f, 0.29f);
        public float fogDensity = 0.011f;

        Vector2 applied = new(float.NaN, float.NaN);

        void OnEnable()
        {
            // falling sky motes + occasional light shafts, created at play time only
            if (Application.isPlaying && !GetComponent<UWSkyFx>()) gameObject.AddComponent<UWSkyFx>();
            if (Application.isPlaying)
            {
                playReadable = PlayerPrefs.GetFloat(ReadKey, playReadable);
                readableOn = PlayerPrefs.GetInt(ReadOnKey, 1) == 1;
            }
            Apply();
        }
        void OnValidate() => Apply();
        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (Application.isPlaying && kb != null)
            {
                if (kb.backslashKey.wasPressedThisFrame) { readableOn = !readableOn; PlayerPrefs.SetInt(ReadOnKey, readableOn ? 1 : 0); }
                if (kb.equalsKey.wasPressedThisFrame) { playReadable = Mathf.Clamp01(Mathf.Round(playReadable * 10f + 1f) / 10f); readableOn = true; PlayerPrefs.SetFloat(ReadKey, playReadable); PlayerPrefs.SetInt(ReadOnKey, 1); }
                if (kb.minusKey.wasPressedThisFrame) { playReadable = Mathf.Clamp01(Mathf.Round(playReadable * 10f - 1f) / 10f); PlayerPrefs.SetFloat(ReadKey, playReadable); }
            }
#endif
            if (applied.x != deathTone || applied.y != haze || lastBoost != PlayBoost)
            {
                lastBoost = PlayBoost;
                Apply();
                var br = GetComponent<UWBrightness>();
                if (br) br.Apply();
            }
        }

        // Other scene tools (the RoomRun/LookStudy dev panel) overwrite the grade; put the stage grade back.
        void LateUpdate()
        {
            if (!Application.isPlaying) return;
            var prof = UWBrightness.ActiveProfile(volume);
            if (prof && prof.TryGet(out ColorAdjustments ca) && Mathf.Abs(ca.saturation.value - Mathf.Lerp(-16f, -62f, deathTone)) > 0.01f) Apply();
        }

        static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        public void Set(float tone, float hz) { deathTone = Mathf.Clamp01(tone); haze = Mathf.Clamp01(hz); Apply(); }

        public void Apply()
        {
            applied = new Vector2(deathTone, haze);
            float t = deathTone, h = haze, r = PlayBoost;
            var prof = UWBrightness.ActiveProfile(volume);
            if (prof)
            {
                if (prof.TryGet(out ColorAdjustments ca))
                {
                    ca.saturation.Override(Mathf.Lerp(-16f, -62f, t));
                    ca.contrast.Override(Mathf.Lerp(Mathf.Lerp(16f, 4f, Mathf.Max(t * 0.6f, h)), 2f, r * 0.7f));
                    ca.colorFilter.Override(Color.Lerp(C("#F2EAFF"), C("#ECEAF0"), t));
                    ca.hueShift.Override(Mathf.Lerp(-3f, 0f, t));
                }
                if (prof.TryGet(out SplitToning st))
                {
                    st.shadows.Override(Color.Lerp(C("#2A1450"), C("#2C2A34"), t));
                    st.highlights.Override(Color.Lerp(C("#B8789A"), C("#B0A8B4"), t));
                }
                if (prof.TryGet(out WhiteBalance wb)) wb.tint.Override(Mathf.Lerp(4f, 0f, t));
                if (prof.TryGet(out Bloom bl)) { bl.tint.Override(Color.Lerp(C("#D8C0FF"), C("#ECE8F2"), t)); bl.scatter.Override(Mathf.Lerp(0.72f, 0.85f, h)); }
                if (!prof.TryGet(out LiftGammaGain lgg)) { lgg = prof.Add<LiftGammaGain>(true); }
                // lifted blacks read as milky haze
                lgg.lift.Override(new Vector4(1f, 1f, 1f, h * 0.05f + t * 0.015f + r * 0.06f));
                lgg.gamma.Override(new Vector4(1f, 1f, 1f, h * 0.02f + r * 0.12f));
                if (prof.TryGet(out Vignette vg)) { vg.color.Override(Color.Lerp(C("#0B0414"), C("#141318"), t)); vg.intensity.Override(Mathf.Lerp(0.38f, 0.1f, r)); }
                if (prof.TryGet(out FilmGrain fg)) fg.intensity.Override(Mathf.Lerp(0.15f, 0.04f, r));
            }
            RenderSettings.fogColor = Color.Lerp(Color.Lerp(fogViolet, fogGrey, t), Color.Lerp(fogViolet, fogGrey, t) * 1.1f, h);
            RenderSettings.fogDensity = (fogDensity + h * 0.012f) * (1f - 0.55f * r);
            foreach (var f in fogMaterials)
            {
                if (!f.mat) continue;
                var c = Color.Lerp(f.violet, f.grey, t);
                c.a = Mathf.Clamp01(f.violet.a * (1f + h * 0.8f) * (1f - 0.4f * r));
                f.mat.SetColor("_Col", c);
            }
        }
    }
}
