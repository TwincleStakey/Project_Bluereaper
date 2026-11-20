// Snowfall (tr24-26): three camera-following layers in place of the single old FallingSnow emitter.
//   near: few large soft flakes between the camera and the ground (blurred look: low alpha)
//   mid : the main body of the snowfall
//   far : many small flakes just above the ground the camera looks at
// Texture: the bundle's own soft T_Snow_Distant dot for every layer ("FREE Cinematic Weather VFX Bundle", Lotus Studio,
// Assets/RainSnowCloudEffect; files not moved or edited). Its T_Snowflake_Main is a 6-arm star and is not used; no
// code-made flake textures (Kido). Each layer is one world-space emitter with SPFollowCamera (emitter kept on the view
// line at a fixed height), slow fall, drift along the scene wind (+x +z, as the drifts in BuildMasks), 1500 particles.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using SnowPlague;

public static partial class SPBuilder
{
    const string SnowPackDir = "Assets/RainSnowCloudEffect";

    static Material SnowfallMat(string name, Texture2D tex, Color col)
        => Mat(name, "Universal Render Pipeline/Particles/Unlit", m =>
        {
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", col);
            m.SetFloat("_Surface", 1f); m.SetFloat("_ZWrite", 0f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SoftParticlesEnabled", 0f); m.DisableKeyword("_SOFTPARTICLES_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent; m.SetOverrideTag("RenderType", "Transparent");
        });

    static void BuildSnowfall(Transform fx)
    {
        // tr26 (Kido: no code-made snow): every layer uses the bundle's own soft T_Snow_Distant dot. Emitters higher and
        // lifetimes cut so the flakes die just above the ground (tr25 spent half their life under it), slight grey-blue
        // tint so they read against the white snow too.
        var flake = AssetDatabase.LoadAssetAtPath<Texture2D>(SnowPackDir + "/Textures/T_Snow_Distant.png");   // bundle file as imported (settings untouched)
        var puff = AssetDatabase.LoadAssetAtPath<Texture2D>(SnowPackDir + "/Textures/T_Snow_Mist.png");   // tr27: big soft puff for the blurred near flakes
        if (!puff) puff = flake;
        if (!flake) { Log("SNOW no " + SnowPackDir + "/Textures/T_Snow_Distant.png - snowfall skipped"); return; }
        var wind = new Vector2(0.45f, 0.35f).normalized;   // = drift direction in BuildMasks

        //            name    ahead height  box (x, y, z)                 count life   size            fall          alpha  wind
        Layer("SnowNear", 8f,  12.5f, new Vector3(9f, 2f, 7f),    150, 5.5f, 0.28f, 0.45f, 0.8f,  1.1f,  0.4f,  0.55f, false,   // tr28: T_Snow_Distant scaled up (T_Snow_Mist puffs read as dark smudges on the snow)
              SnowfallMat("SP_SnowFall_Near", flake, new Color(0.9f, 0.92f, 0.96f, 1f)));
        Layer("SnowMid",  14f, 10.5f, new Vector3(18f, 3f, 14f),  500, 10f,  0.16f, 0.26f, 0.6f,  0.85f, 1f,    0.5f,  false,
              SnowfallMat("SP_SnowFall_Mid", flake, new Color(0.76f, 0.8f, 0.88f, 1f)));
        Layer("SnowFar",  21f, 6.5f,  new Vector3(28f, 3f, 22f),  850, 8f,   0.12f, 0.18f, 0.45f, 0.65f, 0.95f, 0.4f,  false,
              SnowfallMat("SP_SnowFall_Far", flake, new Color(0.78f, 0.82f, 0.9f, 1f)));

        void Layer(string name, float ahead, float height, Vector3 box, int count, float life, float s0, float s1,
                   float fall0, float fall1, float alpha, float windK, bool atlas, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(fx, false); go.transform.position = new Vector3(0, height, 0);
            var follow = go.AddComponent<SPFollowCamera>(); follow.ahead = ahead; follow.height = height;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.prewarm = true; main.duration = life;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.8f, life); main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(s0, s1);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, alpha), new Color(0.9f, 0.92f, 0.96f, alpha * 0.75f));
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = count;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission; em.rateOverTime = count / (life * 0.9f);
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(wind.x * windK * 0.6f, wind.x * windK * 1.3f);
            vel.y = new ParticleSystem.MinMaxCurve(-fall1, -fall0);
            vel.z = new ParticleSystem.MinMaxCurve(wind.y * windK * 0.6f, wind.y * windK * 1.3f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.25f; noise.frequency = 0.3f; noise.scrollSpeed = 0.15f; noise.quality = ParticleSystemNoiseQuality.Low;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                                              new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(1, 0.85f), new GradientAlphaKey(0, 1) });
            col.color = g;
            if (atlas)
            {
                var ts = ps.textureSheetAnimation; ts.enabled = true; ts.mode = ParticleSystemAnimationMode.Grid; ts.numTilesX = 2; ts.numTilesY = 2;
                ts.animation = ParticleSystemAnimationType.WholeSheet; ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                ts.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f); ts.cycleCount = 1;
            }
            var pr = go.GetComponent<ParticleSystemRenderer>(); pr.sharedMaterial = mat;
            pr.shadowCastingMode = ShadowCastingMode.Off; pr.receiveShadows = false;
            pr.renderMode = ParticleSystemRenderMode.Billboard; pr.maxParticleSize = 0.03f;   // never screen-filling
            ps.Play();
        }
        Log("SNOW layers: near 150 / mid 500 / far 850 (T_Snow_Distant), 1500 max");
    }

    // shots: put each layer where SPFollowCamera would at runtime for this camera, then fill it
    public static void SnowfallFollow(Camera cam)
    {
        foreach (var f in Object.FindObjectsByType<SPFollowCamera>(FindObjectsSortMode.None))
        {
            var p = cam.transform.position + cam.transform.forward * f.ahead;
            f.transform.position = new Vector3(p.x, f.height, p.z);
            var ps = f.GetComponent<ParticleSystem>(); if (ps) ps.Simulate(10f, true, true);
        }
    }
}
