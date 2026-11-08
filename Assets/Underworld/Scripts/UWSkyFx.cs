using System.Collections.Generic;
using UnityEngine;

namespace Underworld
{
    // Sky effects added at play time: small glowing motes drifting down from above the arena, and a handful of
    // soft light shafts that fade in and out on their own cycles. Everything made here is destroyed in OnDisable.
    // Materials come from Assets/Underworld/Resources, so nothing new has to be wired into the scene file.
    public class UWSkyFx : MonoBehaviour
    {
        public Vector3 arenaCenter = new(-2f, 0f, -1f);
        public Vector2 arenaSize = new(50f, 34f);
        public float moteRate = 22f;
        public int shaftCount = 7;
        public int seed = 17;

        readonly List<GameObject> made = new();

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            var moteMat = Resources.Load<Material>("UW_SkyMote");
            var shaftMat = Resources.Load<Material>("UW_LightShaft");
            if (moteMat) made.Add(MakeMotes(moteMat).gameObject);
            if (shaftMat) MakeShafts(shaftMat);
        }

        void OnDisable()
        {
            foreach (var go in made) if (go) Destroy(go);
            made.Clear();
        }

        ParticleSystem MakeMotes(Material m)
        {
            var go = new GameObject("UW_SkyMotes");
            go.transform.SetParent(transform, false);
            go.transform.position = arenaCenter + Vector3.up * 13f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false; ps.randomSeed = (uint)seed;
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.maxParticles = 450;
            main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 19f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.86f, 0.8f, 1f, 1f);
            var em = ps.emission; em.rateOverTime = moteRate;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(arenaSize.x, 1f, arenaSize.y);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            vel.y = new ParticleSystem.MinMaxCurve(-1.0f, -0.6f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            var nz = ps.noise; nz.enabled = true; nz.strength = 0.3f; nz.frequency = 0.35f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.8f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        void MakeShafts(Material m)
        {
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var rnd = new System.Random(seed);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            for (int i = 0; i < shaftCount; i++)
            {
                var go = new GameObject("UW_LightShaft");
                go.transform.SetParent(transform, false);
                var p = arenaCenter + new Vector3(R(-0.42f, 0.42f) * arenaSize.x, -0.2f, R(-0.4f, 0.4f) * arenaSize.y);
                go.transform.position = p;
                // lean slightly, all the same way, as if from one high light source
                go.transform.rotation = Quaternion.Euler(R(-14f, -9f), 0f, R(6f, 11f));
                go.transform.localScale = new Vector3(R(2.2f, 4.2f), R(14f, 18f), 1f);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                // quad mesh is centred at origin; the shader builds the shaft from the object origin upward, so enlarge bounds
                mr.localBounds = new Bounds(new Vector3(0, 0.5f, 0), new Vector3(1.5f, 1.2f, 1.5f));
                var mpb = new MaterialPropertyBlock(); mpb.SetFloat("_Phase", (float)i / shaftCount + R(0f, 0.12f)); mr.SetPropertyBlock(mpb);
                made.Add(go);
            }
        }
    }
}
