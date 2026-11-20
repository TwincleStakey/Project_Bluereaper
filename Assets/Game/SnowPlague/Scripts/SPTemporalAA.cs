// 11-14 (Kido: "식생들이 깜빡거려" while the camera moves). The snow field is full of sub-pixel twigs, stems and seed
// heads (kit dead shrubs, brush, weeds). SMAA (the RoomRun camera default) only smooths still edges, so every camera
// step re-rolled which twig pixels were covered: a field of popping pixels. While this field is live in Play, the game
// camera switches to URP temporal AA (it accumulates the twigs into stable partial coverage), and an extra volume turns
// film grain off here (the grain re-rolls every frame and read as more sparkle on top). Both are put back on disable.
// Measured with SPFlicker (editor, 1/3 px camera steps): vegetation was ~half of the frame-to-frame pop score.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SnowPlague
{
    [ExecuteAlways]
    public class SPTemporalAA : MonoBehaviour
    {
        public static bool Enabled = true;   // dev A/B
        [Range(0, 1)] public float sharpen = 0.45f;
        UniversalAdditionalCameraData cam;
        AntialiasingMode prev;
        Volume grainOff;

        void LateUpdate()
        {
            if (!Application.isPlaying) return;
            var c = Camera.main;
            var d = c ? c.GetComponent<UniversalAdditionalCameraData>() : null;
            if (d != cam) { Restore(); if (d && Enabled) Take(d); }
            else if (cam && !Enabled) Restore();
            if (!grainOff && Enabled)
            {
                grainOff = new GameObject("SP NoGrain (TAA)").AddComponent<Volume>();
                grainOff.transform.SetParent(transform, false);
                grainOff.isGlobal = true; grainOff.priority = 5000;
                var p = ScriptableObject.CreateInstance<VolumeProfile>(); p.name = "SP_NoGrain";
                p.Add<FilmGrain>(true).intensity.Override(0f);
                grainOff.sharedProfile = p;
            }
            if (grainOff) grainOff.enabled = Enabled;
        }

        void Take(UniversalAdditionalCameraData d)
        {
            cam = d; prev = d.antialiasing;
            d.antialiasing = AntialiasingMode.TemporalAntiAliasing;
            ref var s = ref d.taaSettings;
            s.quality = TemporalAAQuality.High;
            s.contrastAdaptiveSharpening = sharpen;   // TAA softens the ground scans a little; CAS gives the grit back
        }

        void Restore()
        {
            if (cam) cam.antialiasing = prev;
            cam = null;
        }

        void OnDisable()
        {
            Restore();
            if (grainOff) { if (grainOff.sharedProfile) Destroy(grainOff.sharedProfile); Destroy(grainOff.gameObject); grainOff = null; }
        }
    }
}
