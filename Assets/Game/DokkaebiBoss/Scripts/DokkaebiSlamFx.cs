using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dokkaebi
{
    // Ground slam: telegraph circle, impact flash, shockwave, dust, debris, sparks, crack decal,
    // camera shake and a radial burst of dokkaebi-fire projectiles.
    // Runs on its own in Play mode. With manualTick = true a driver calls Tick(dt) (deterministic previews).
    public sealed class DokkaebiSlamFx : MonoBehaviour
    {
        [Header("Materials")]
        public Material glowMat;        // Dokkaebi/Additive + soft circle
        public Material sparkMat;       // Dokkaebi/Additive + spark streak
        public Material smokeMat;       // Dokkaebi/Smoke + puff
        public Material ringMat;        // Dokkaebi/GroundRing (additive)
        public Material dustRingMat;    // Dokkaebi/GroundRing (alpha)
        public Material telegraphMat;   // Dokkaebi/GroundRing (alpha)
        public Material crackMat;       // Dokkaebi/CrackDecal
        public Material debrisMat;      // URP Particles/Lit or Lit
        public Mesh debrisMesh;
        public Material craterMat;      // Dokkaebi/CraterDecal (preferred over crackMat)
        public Material stoneMat;       // rim chunks + debris
        public Mesh[] debrisMeshes;     // Kenney stones (CC0); falls back to debrisMesh
        public Mesh[] rimMeshes;        // flat stones pushed up around the crater
        public int rimStones = 9;

        [Header("Projectiles")]
        public int ringCount = 20;
        public float ringSpeed = 7f;
        public float secondRingDelay = 0.22f;
        public float secondRingSpeed = 4.6f;
        public float projectileRange = 14f;
        public float projectileRadius = 0.28f;
        public float projectileHeight = 0.55f;
        public Color projectileColor = new Color(0.12f, 0.55f, 1.5f, 1f);   // HDR dokkaebi fire
        public Color projectileCore = new Color(0.75f, 1.35f, 1.9f, 1f);

        [Header("Impact")]
        public float shockRadius = 4.2f;
        public float crackSize = 4.2f;
        public float decalLife = 4f;
        public float shakeTrauma = 0.75f;
        public DokkaebiCameraShake cameraShake;
        public Transform viewCamera;           // for billboards; defaults to Camera.main
        public Transform hitTarget;            // optional: projectile hit test
        public float hitTargetRadius = 0.4f;
        public event Action<Vector3> ProjectileHitTarget;
        public event Action<Vector3> Impacted;

        public bool manualTick;

        // ------------------------------------------------------------------
        abstract class Fx { public float age, life; public GameObject go; public abstract void Update(DokkaebiSlamFx o, float dt); }

        sealed class RingFx : Fx
        {
            public Material m; public float size, width0; public AnimationCurve grow;
            public override void Update(DokkaebiSlamFx o, float dt)
            {
                float k = Mathf.Clamp01(age / life);
                float r = 1 - Mathf.Pow(1 - k, 3);
                m.SetFloat("_Radius", Mathf.Lerp(0.05f, 0.97f, r));
                m.SetFloat("_Width", Mathf.Lerp(width0, width0 * 0.35f, k));
                m.SetFloat("_Fade", 1 - k * k);
            }
        }

        sealed class TelegraphFx : Fx
        {
            public Material m;
            public override void Update(DokkaebiSlamFx o, float dt)
            {
                float k = Mathf.Clamp01(age / life);
                m.SetFloat("_Radius", 0.97f);
                m.SetFloat("_Fill", Mathf.Lerp(0.05f, 0.9f, k));
                // inner fill closes in toward the edge as the hit approaches
                float pulse = 0.75f + 0.25f * Mathf.Sin(age * 30f);
                m.SetFloat("_Fade", Mathf.Clamp01(age * 6) * pulse);
            }
        }

        sealed class CrackFx : Fx
        {
            public Material m;
            public override void Update(DokkaebiSlamFx o, float dt)
            {
                m.SetFloat("_Reveal", Mathf.Lerp(0.1f, 1.2f, Mathf.Clamp01(age / 0.12f)));
                m.SetFloat("_Glow", Mathf.Clamp01(1 - age / 0.8f));
                m.SetFloat("_Fade", 1 - Mathf.Clamp01((age - (life - 1f)) / 1f));
            }
        }

        sealed class CraterFx : Fx
        {
            public Material m;
            public override void Update(DokkaebiSlamFx o, float dt)
            {
                m.SetFloat("_Reveal", Mathf.Lerp(0.15f, 1.5f, Mathf.Clamp01(age / 0.1f)));
                m.SetFloat("_Glow", Mathf.Clamp01(1 - age / 0.7f));
                m.SetFloat("_Fade", 1 - Mathf.Clamp01((age - (life - 1f)) / 1f));
            }
        }

        sealed class RimStoneFx : Fx
        {
            public Vector3 restPos; public float delay, sink;
            public override void Update(DokkaebiSlamFx o, float dt)
            {
                float k = Mathf.Clamp01((age - delay) / 0.07f);
                float up = 1 - Mathf.Pow(1 - k, 3);
                float overshoot = Mathf.Sin(Mathf.Clamp01((age - delay - 0.07f) / 0.15f) * Mathf.PI) * 0.06f;
                float down = Mathf.Clamp01((age - (life - 0.8f)) / 0.8f);
                go.transform.position = restPos + Vector3.up * (-sink * (1 - up) + overshoot - sink * down * 1.2f);
            }
        }

        sealed class FlashFx : Fx
        {
            public Light light; public Transform quad; public float size; public Material m;
            public override void Update(DokkaebiSlamFx o, float dt)
            {
                float k = Mathf.Clamp01(age / life);
                if (light) light.intensity = Mathf.Lerp(28f, 0f, Mathf.Pow(k, 0.5f));
                float s = Mathf.Lerp(size * 0.4f, size, Mathf.Sqrt(k));
                quad.localScale = Vector3.one * s;
                m.SetColor("_Color", o.projectileCore * (1 - k));
                o.Billboard(quad);
            }
        }

        sealed class Projectile
        {
            public GameObject go; public Transform core, glow, trail; public Vector3 pos, vel; public float age, maxAge; public bool alive;
        }

        readonly List<Fx> fx = new List<Fx>();
        readonly List<Projectile> projectiles = new List<Projectile>();
        readonly Stack<Projectile> pool = new Stack<Projectile>();
        readonly List<(float at, Vector3 p, float speed, float angleOffset)> pendingRings = new List<(float, Vector3, float, float)>();
        float clock;
        ParticleSystem dust, debris, sparks, embers;
        Mesh quadMesh, groundQuad;
        Material projGlowMat, projCoreMat, projTrailMat;

        public int LiveProjectiles { get { int n = 0; foreach (var p in projectiles) if (p.alive) n++; return n; } }

        // ------------------------------------------------------------------
        void Awake() { Init(); }

        bool inited;
        public void Init()
        {
            if (inited) return; inited = true;
            quadMesh = MakeQuad(false); groundQuad = MakeQuad(true);
            projGlowMat = new Material(glowMat) { name = "ProjGlow" }; projGlowMat.SetColor("_Color", projectileColor);
            projCoreMat = new Material(glowMat) { name = "ProjCore" }; projCoreMat.SetColor("_Color", projectileCore);
            projTrailMat = new Material(sparkMat) { name = "ProjTrail" }; projTrailMat.SetColor("_Color", projectileColor * 0.8f);
            dust = BuildDust(); debris = BuildDebris(); sparks = BuildSparks(); embers = BuildEmbers();
        }

        Transform Cam => viewCamera ? viewCamera : (Camera.main ? Camera.main.transform : null);

        void Update() { if (!manualTick) Tick(Time.deltaTime); }

        // ------------------------------------------------------------------
        public void Telegraph(Vector3 p, float duration)
        {
            Init();
            var m = new Material(telegraphMat);
            var go = GroundQuad("Telegraph", p + Vector3.up * 0.02f, shockRadius * 2f, m);
            fx.Add(new TelegraphFx { go = go, m = m, life = duration });
        }

        public void Impact(Vector3 p)
        {
            Init();
            p.y = transform.position.y;   // ground plane height = this object's y
            // clear the telegraph
            foreach (var f in fx) if (f is TelegraphFx) f.age = f.life;

            // flash: light + bright billboard
            var fgo = new GameObject("SlamFlash"); fgo.transform.SetParent(transform, false); fgo.transform.position = p + Vector3.up * 0.4f;
            var l = fgo.AddComponent<Light>(); l.type = LightType.Point; l.range = 7f; l.color = new Color(0.55f, 0.85f, 1f); l.intensity = 28f;
            var fq = new GameObject("FlashQuad"); fq.transform.SetParent(fgo.transform, false);
            var fm = new Material(glowMat); fq.AddComponent<MeshFilter>().sharedMesh = quadMesh; fq.AddComponent<MeshRenderer>().sharedMaterial = fm;
            fx.Add(new FlashFx { go = fgo, light = l, quad = fq.transform, size = 2.0f, m = fm, life = 0.22f });

            // shockwaves: bright fast ring + slower dusty ring
            var rm = new Material(ringMat);
            fx.Add(new RingFx { go = GroundQuad("Shockwave", p + Vector3.up * 0.04f, shockRadius * 2f, rm), m = rm, width0 = 0.16f, life = 0.38f });
            var dm = new Material(dustRingMat);
            fx.Add(new RingFx { go = GroundQuad("DustWave", p + Vector3.up * 0.03f, shockRadius * 2.6f, dm), m = dm, width0 = 0.22f, life = 0.75f });

            // crack decal, random rotation so repeats don't match
            if (craterMat)
            {
                var km = new Material(craterMat);
                var kgo = GroundQuad("Crater", p + Vector3.up * 0.012f, crackSize * 2f, km);
                kgo.transform.rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);
                fx.Add(new CraterFx { go = kgo, m = km, life = decalLife });
                SpawnRimStones(p);
            }
            else
            {
                var cm = new Material(crackMat);
                var cgo = GroundQuad("Crack", p + Vector3.up * 0.015f, crackSize * 2f, cm);
                cgo.transform.rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);
                fx.Add(new CrackFx { go = cgo, m = cm, life = decalLife });
            }

            Emit(dust, p + Vector3.up * 0.7f, 36); Emit(debris, p, 16); Emit(sparks, p, 46); Emit(embers, p, 18);

            if (cameraShake) cameraShake.AddTrauma(shakeTrauma);
            Impacted?.Invoke(p);

            float a0 = UnityEngine.Random.Range(0f, 360f / ringCount);
            pendingRings.Add((clock + 0.03f, p, ringSpeed, a0));
            pendingRings.Add((clock + secondRingDelay, p, secondRingSpeed, a0 + 180f / ringCount));
        }

        void SpawnRimStones(Vector3 p)
        {
            var meshes = rimMeshes != null && rimMeshes.Length > 0 ? rimMeshes : debrisMeshes;
            if (meshes == null || meshes.Length == 0 || !stoneMat) return;
            float rimR = crackSize * 0.34f;
            float a0 = UnityEngine.Random.Range(0f, 360f);
            for (int i = 0; i < rimStones; i++)
            {
                float a = (a0 + 360f * i / rimStones + UnityEngine.Random.Range(-12f, 12f)) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var go = new GameObject("RimStone"); go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = meshes[UnityEngine.Random.Range(0, meshes.Length)];
                go.AddComponent<MeshRenderer>().sharedMaterial = stoneMat;
                float s = UnityEngine.Random.Range(1.6f, 2.6f);
                go.transform.localScale = new Vector3(s, s * UnityEngine.Random.Range(0.9f, 1.4f), s);
                // tilted outward like a slab levered up by the blow
                go.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(-UnityEngine.Random.Range(25f, 55f), UnityEngine.Random.Range(-20f, 20f), UnityEngine.Random.Range(-15f, 15f));
                var rest = p + dir * (rimR * UnityEngine.Random.Range(0.85f, 1.15f)) + Vector3.down * 0.08f;
                go.transform.position = rest + Vector3.down * 0.5f;
                fx.Add(new RimStoneFx { go = go, restPos = rest, sink = 0.5f, delay = UnityEngine.Random.Range(0f, 0.05f), life = decalLife });
            }
        }

        void Emit(ParticleSystem ps, Vector3 p, int n)
        {
            ps.transform.position = p;
            ps.Emit(n);
        }

        // ------------------------------------------------------------------
        public void Tick(float dt)
        {
            clock += dt;
            for (int i = pendingRings.Count - 1; i >= 0; i--)
            {
                var r = pendingRings[i];
                if (clock < r.at) continue;
                pendingRings.RemoveAt(i);
                for (int k = 0; k < ringCount; k++)
                {
                    float a = (r.angleOffset + 360f * k / ringCount) * Mathf.Deg2Rad;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    Fire(r.p + d * 0.9f + Vector3.up * projectileHeight, d * r.speed);
                }
            }

            for (int i = fx.Count - 1; i >= 0; i--)
            {
                var f = fx[i];
                f.age += dt;
                if (f.age >= f.life) { DestroySafe(f.go); fx.RemoveAt(i); continue; }
                f.Update(this, dt);
            }

            var cam = Cam;
            foreach (var p in projectiles)
            {
                if (!p.alive) continue;
                p.age += dt;
                p.pos += p.vel * dt;
                p.go.transform.position = p.pos;
                // spawn pop: scale in fast
                float s = Mathf.Min(1f, p.age / 0.08f);
                float flicker = 1f + 0.12f * Mathf.Sin(p.age * 40f + p.pos.x);
                p.core.localScale = Vector3.one * projectileRadius * 0.9f * s;
                p.glow.localScale = Vector3.one * projectileRadius * 4.2f * s * flicker;
                Billboard(p.core); Billboard(p.glow);
                AxialBillboard(p.trail, p.vel, cam, projectileRadius * 1.6f * s, Mathf.Min(1.6f, p.vel.magnitude * 0.22f) * s);
                bool hit = hitTarget && (Flat(hitTarget.position) - Flat(p.pos)).sqrMagnitude < (hitTargetRadius + projectileRadius) * (hitTargetRadius + projectileRadius);
                if (hit) ProjectileHitTarget?.Invoke(p.pos);
                if (hit || p.age > p.maxAge) { p.alive = false; p.go.SetActive(false); pool.Push(p); }
            }

            if (manualTick)
            {
                dust.Simulate(dt, true, false, false); debris.Simulate(dt, true, false, false);
                sparks.Simulate(dt, true, false, false); embers.Simulate(dt, true, false, false);
            }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        void Fire(Vector3 pos, Vector3 vel)
        {
            Projectile p = pool.Count > 0 ? pool.Pop() : CreateProjectile();
            if (!projectiles.Contains(p)) projectiles.Add(p);
            p.pos = pos; p.vel = vel; p.age = 0; p.maxAge = projectileRange / Mathf.Max(0.1f, vel.magnitude); p.alive = true;
            p.go.transform.position = pos; p.go.SetActive(true);
        }

        Projectile CreateProjectile()
        {
            var go = new GameObject("SlamProjectile"); go.transform.SetParent(transform, false);
            Transform Part(string n, Material m)
            {
                var c = new GameObject(n); c.transform.SetParent(go.transform, false);
                c.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                var r = c.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                return c.transform;
            }
            var p = new Projectile { go = go };
            p.trail = Part("Trail", projTrailMat);
            p.glow = Part("Glow", projGlowMat);
            p.core = Part("Core", projCoreMat);
            return p;
        }

        public void Billboard(Transform t)
        {
            var cam = Cam; if (!cam) return;
            t.rotation = Quaternion.LookRotation(cam.forward, cam.up);
        }

        // Quad stretched behind the projectile along its velocity, facing the camera.
        static void AxialBillboard(Transform t, Vector3 vel, Transform cam, float width, float length)
        {
            if (!cam || vel.sqrMagnitude < 1e-4f) return;
            Vector3 axis = vel.normalized;
            Vector3 toCam = (cam.position - t.parent.position).normalized;
            Vector3 side = Vector3.Cross(axis, toCam).normalized;
            Vector3 normal = Vector3.Cross(side, axis);
            t.rotation = Quaternion.LookRotation(-normal, side);          // quad local Y = side, local X = axis
            t.localScale = new Vector3(length, width, 1);
            t.position = t.parent.position - axis * length * 0.45f;
        }

        // ------------------------------------------------------------------
        GameObject GroundQuad(string name, Vector3 p, float size, Material m)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            go.transform.position = p; go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = groundQuad;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static Mesh MakeQuad(bool ground)
        {
            var m = new Mesh { name = ground ? "DK_GroundQuad" : "DK_Quad" };
            if (ground) m.vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(.5f, 0, -.5f), new Vector3(.5f, 0, .5f), new Vector3(-.5f, 0, .5f) };
            else m.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = ground ? new[] { 0, 2, 1, 0, 3, 2 } : new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static void DestroySafe(UnityEngine.Object o) { if (!o) return; if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        // ------------------------------------------------------------------ particle builders
        ParticleSystem NewPS(string name, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.loop = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission; em.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.useAutoRandomSeed = false; ps.randomSeed = (uint)name.GetHashCode();
            ps.Play();
            return ps;
        }

        ParticleSystem BuildDust()
        {
            var ps = NewPS("Dust", smokeMat);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6.5f);
            m.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(1, 1, 1, 0.9f), new Color(0.85f, 0.82f, 0.78f, 0.75f));
            m.gravityModifier = -0.05f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.6f; sh.rotation = new Vector3(90, 0, 0); sh.radiusThickness = 1;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(0f); vel.y = new ParticleSystem.MinMaxCurve(0f); vel.z = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);   // local z = world up after shape rotation? keep small lift
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0.6f; lim.dampen = 0.12f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(0.3f, 1.1f), new Keyframe(1, 1.5f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                                              new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.08f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.sortMode = ParticleSystemSortMode.Distance;
            r.pivot = new Vector3(0, -0.45f, 0);   // lift billboards so they do not cut into the floor
            sh.position = new Vector3(0, 0, 0);
            return ps;
        }

        ParticleSystem BuildDebris()
        {
            var ps = NewPS("Debris", debrisMat);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
            m.startRotation3D = true;
            m.startRotationX = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2); m.startRotationY = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2); m.startRotationZ = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            m.gravityModifier = 2.2f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 38; sh.radius = 0.4f; sh.rotation = new Vector3(-90, 0, 0);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-8, 8); rot.y = new ParticleSystem.MinMaxCurve(-8, 8); rot.z = new ParticleSystem.MinMaxCurve(-8, 8);
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.75f, 1), new Keyframe(1, 0)));
            var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.Planes; c.bounce = 0.35f; c.dampen = 0.4f;
            var plane = new GameObject("DebrisFloor").transform; plane.SetParent(transform, false); c.SetPlane(0, plane);
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = debrisMesh;
            if (debrisMeshes != null && debrisMeshes.Length > 0) { r.SetMeshes(debrisMeshes); if (stoneMat) r.sharedMaterial = stoneMat; }
            r.alignment = ParticleSystemRenderSpace.World;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return ps;
        }

        ParticleSystem BuildSparks()
        {
            var ps = NewPS("Sparks", sparkMat);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(6f, 15f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            m.startColor = new ParticleSystem.MinMaxGradient(projectileCore, projectileColor);
            m.gravityModifier = 1.2f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 70; sh.radius = 0.3f; sh.rotation = new Vector3(-90, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                                              new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.5f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 2f;
            return ps;
        }

        ParticleSystem BuildEmbers()
        {
            var ps = NewPS("Embers", glowMat);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3.5f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            m.startColor = new ParticleSystem.MinMaxGradient(projectileColor * 0.9f, projectileCore * 0.7f);
            m.gravityModifier = -0.25f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 1.2f;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.8f; noise.frequency = 1.2f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                                              new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0, 1) });
            col.color = g;
            return ps;
        }
    }
}
