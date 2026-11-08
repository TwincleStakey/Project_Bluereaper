using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Underworld
{
    // Stage brightness (-1 dark .. 0 default .. +1 bright). Lifts exposure, moonlight and ambient together so
    // shadows open up without washing out the glows. In play mode: [ and ] step it, and the value is saved.
    // Other tools in the scene (for example the LookStudy dev panel that RoomRun adds to every room) reset
    // exposure, colour grading and the sun angle, so the stage values are re-asserted whenever they drift.
    [ExecuteAlways]
    public class UWBrightness : MonoBehaviour
    {
        [Range(-1f, 1f)] public float brightness = 0f;
        public Volume volume;
        public Light moon;
        public float baseExposure = 1.62f, baseMoon = 1.7f;
        public Color ambientBase = new(0.46f, 0.43f, 0.55f), ambientUp = new(0.31f, 0.28f, 0.38f);
        const string Key = "UW_Brightness";
        float applied = float.NaN;
        Quaternion moonRot; bool haveMoonRot;

        // In play mode work on the volume's runtime copy so the profile asset is never modified.
        public static VolumeProfile ActiveProfile(Volume v)
        {
            if (!v) return null;
            return Application.isPlaying ? v.profile : v.sharedProfile;
        }

        void OnEnable()
        {
            if (moon && !haveMoonRot) { moonRot = moon.transform.rotation; haveMoonRot = true; }
            if (Application.isPlaying) brightness = Mathf.Max(PlayerPrefs.GetFloat(Key, brightness), -0.3f);
            Apply();
        }

        void OnValidate() => Apply();

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (Application.isPlaying && Keyboard.current != null)
            {
                if (Keyboard.current.rightBracketKey.wasPressedThisFrame) Set(brightness + 0.1f);
                if (Keyboard.current.leftBracketKey.wasPressedThisFrame) Set(brightness - 0.1f);
            }
#endif
            if (!Mathf.Approximately(applied, brightness)) Apply();
        }

        void LateUpdate()
        {
            if (!Application.isPlaying) return;
            bool drift = moon && haveMoonRot && Quaternion.Angle(moon.transform.rotation, moonRot) > 0.5f;
            var prof = ActiveProfile(volume);
            if (prof && prof.TryGet(out ColorAdjustments ca) && Mathf.Abs(ca.postExposure.value - Exposure) > 0.001f) drift = true;
            if (drift) Apply();
            // scene loading can rebuild the ambient probe from the scene's lighting settings; keep ours
            else RenderSettings.ambientProbe = Probe();
        }

        float Boost { get { var e = GetComponent<UWEnvironment>(); return e ? e.PlayBoost : 0f; } }
        float Exposure => baseExposure + brightness * 0.7f + Boost * 0.6f;

        SphericalHarmonicsL2 Probe()
        {
            float k = Mathf.Max(0.1f, 1f + brightness * 0.9f) * (1f + Boost * 0.8f);
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(ambientBase * k);
            sh.AddDirectionalLight(Vector3.up, ambientUp * k, 1f);
            return sh;
        }

        public void Set(float b)
        {
            brightness = Mathf.Clamp(Mathf.Round(b * 10f) / 10f, -1f, 1f);
            if (Application.isPlaying) PlayerPrefs.SetFloat(Key, brightness);
            Apply();
        }

        public void Apply()
        {
            applied = brightness;
            float b = brightness;
            var prof = ActiveProfile(volume);
            if (prof && prof.TryGet(out ColorAdjustments ca)) ca.postExposure.Override(Exposure);
            if (moon)
            {
                moon.intensity = baseMoon * (1f + b * 0.6f) * (1f + Boost * 0.4f);
                if (haveMoonRot) moon.transform.rotation = moonRot;
            }
            RenderSettings.ambientProbe = Probe();
            var env = GetComponent<UWEnvironment>();
            if (env && Application.isPlaying) env.Apply();
        }
    }
}
