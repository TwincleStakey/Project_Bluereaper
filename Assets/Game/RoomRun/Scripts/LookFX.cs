// Look test (Kido 11-11: "쉐이더는 다 넣어, 어느정도까지 유니티에서 룩 가능한지 보고싶어 ... 플레이어랑 적 탄막에 광원효과
// poe2같은것좀"). Everything here is additive to the stages and StageMood; nothing in a scene or asset changes.
//   Post (a global volume above StageMood): depth of field focused on the player so the far top of the screen softens,
//   a stronger wider bloom so glowing things bleed light, a touch of chromatic aberration at the edges.
//   Shots: every bullet gets a soft pool of light on the ground under it and a tapered streak along its path
//   (instanced additive cards, so thousands cost about the same as a few), on top of FieldLighting's real lights.
//   Flashes: short real point lights on player shots (muzzle), hits and kills, from a small recycled pool.
//   Player: a faint cool glow on the ground around the feet.
// Off for A/B with the perf probe variant off:LookFX, or LookFX.On = false.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoomRun
{
    public sealed class LookFX : MonoBehaviour
    {
        public static bool On = true;

        [Header("Post")]
        public float dofAperture = 4.5f, dofFocalLength = 70f;
        public float bloomIntensity = 1.3f, bloomThreshold = 0.8f, bloomScatter = 0.75f;
        public float chromatic = 0.14f;

        [Header("Shot light")]
        // strongly saturated: StageMood's grade takes 45-66% of the colour out after this
        public Color enemyGlow = new Color(1.0f, 0.25f, 0.04f) * 0.6f;
        public Color playerGlow = new Color(0.25f, 0.6f, 1.0f) * 0.65f;
        public float poolRadius = 1.6f, streakLength = 1.5f, playerStreakLength = 2.4f, streakWidth = 0.32f;
        public float muzzleHeight = 1.05f;   // shots fly this high above the ground (EnemyController.MuzzleHeight)

        [Header("Flashes")]
        public Color muzzleColor = new Color(0.6f, 0.82f, 1f), hitColor = new Color(1f, 0.6f, 0.3f), killColor = new Color(1f, 0.78f, 0.55f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("LookFX");
            DontDestroyOnLoad(go);
            go.AddComponent<LookFX>();
        }

        Volume vol;
        DepthOfField dof;
        Material poolEnemy, poolPlayer, streakEnemy, streakPlayer, aura;
        // 11-11 second pass (Kido: "탄막을 좀 무료 vfx받아봐"): bullets drawn as sprites from the Kenney Particle Pack (CC0,
        // Resources/VFX): a white-hot core, a coloured halo that bleeds into the bloom, and a light trail; the round
        // meshes are hidden while this is on.
        Material haloEnemy, haloPlayer, coreEnemy, corePlayer;
        Mesh quad;
        const int N = 1023;
        readonly Matrix4x4[] auraM = new Matrix4x4[1], pe = new Matrix4x4[N], pp = new Matrix4x4[N], se = new Matrix4x4[N], sp = new Matrix4x4[N],
            he = new Matrix4x4[N], hp = new Matrix4x4[N], coreTmp = new Matrix4x4[N], auraTmp = new Matrix4x4[N];

        [Header("Bullet sprites")]
        public float haloSize = 0.95f, playerHaloSize = 0.9f, coreSize = 0.32f;
        [Tooltip("Saturation with LookFX on (StageMood alone takes it to -45..-66, which also greys the shots).")]
        public float saturation = -18f;

        void Awake()
        {
            var sh = Resources.Load<Shader>("RR_AddGlow");
            if (sh)
            {
                Material M(Color c, int mode, float soft, string tex = null)
                {
                    var m = new Material(sh) { enableInstancing = true, hideFlags = HideFlags.DontSave };
                    m.SetColor("_Color", c); m.SetFloat("_Streak", mode); m.SetFloat("_Soft", soft);
                    if (tex != null) { var t = Resources.Load<Texture2D>("VFX/" + tex); if (t) m.SetTexture("_MainTex", t); }
                    return m;
                }
                poolEnemy = M(enemyGlow, 0, 2.2f); poolPlayer = M(playerGlow, 0, 2.2f);
                streakEnemy = M(new Color(1f, 0.2f, 0.03f) * 2.2f, 3, 1.1f, "trace_01_rotated"); streakPlayer = M(playerGlow * 3.8f, 3, 1.3f, "trace_04_rotated");
                haloEnemy = M(new Color(1f, 0.16f, 0.02f) * 1.6f, 2, 1.8f, "light_01"); haloPlayer = M(new Color(0.15f, 0.45f, 1f) * 1.6f, 2, 1.8f, "light_02");
                coreEnemy = M(new Color(1f, 0.6f, 0.3f) * 2.4f, 2, 1.5f, "light_03"); corePlayer = M(new Color(0.75f, 0.9f, 1f) * 3.2f, 2, 1.0f, "flare_01");
                aura = M(new Color(0.35f, 0.5f, 0.75f) * 0.35f, 0, 1.6f);
            }
            quad = new Mesh { name = "LookFX quad", hideFlags = HideFlags.DontSave };
            quad.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            quad.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);

            vol = gameObject.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 1100;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.name = "LookFX (runtime)";
            vol.sharedProfile = p;
            dof = p.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.aperture.Override(dofAperture); dof.focalLength.Override(dofFocalLength); dof.focusDistance.Override(30f);
            bloom = p.Add<Bloom>(true);
            bloom.intensity.Override(bloomIntensity); bloom.threshold.Override(bloomThreshold); bloom.scatter.Override(bloomScatter);
            bloom.tint.Override(new Color(1f, 0.92f, 0.85f));
            var ca = p.Add<ChromaticAberration>(true);
            ca.intensity.Override(chromatic);
            // only saturation: StageMood's exposure, contrast and filter stay
            adj = p.Add<ColorAdjustments>(true);
            adj.postExposure.overrideState = adj.contrast.overrideState = adj.colorFilter.overrideState = adj.hueShift.overrideState = false;
            adj.saturation.Override(saturation);
        }

        // Per-field exposure (absolute EV) set here, above every other volume. 11-11 (Kido: "저승은 좀 만 poe2수준으로
        // 올려봐 너무 어두워"): StageMood's underworld exposure had no visible effect (same frame brightness at +0.5 and
        // +1.8 EV), so the underworld is lifted here instead.
        static readonly System.Collections.Generic.Dictionary<string, float> Exposure = new System.Collections.Generic.Dictionary<string, float> { { "underworld", 2.3f } };
        ColorAdjustments adj;
        Bloom bloom;

        // Per-field saturation / bloom. Snow (snow thread, 11-11): -18 made the snow too colourful and a 0.8 threshold
        // smeared bloom over the bright snow, so it keeps more grey and only real light sources bloom.
        struct RoomPost { public float saturation, bloomThreshold, bloomIntensity; }
        static readonly System.Collections.Generic.Dictionary<string, RoomPost> RoomPosts = new System.Collections.Generic.Dictionary<string, RoomPost>
        {
            { "snow", new RoomPost { saturation = -30f, bloomThreshold = 1.15f, bloomIntensity = 0.9f } },
        };

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += Draw;
            BulletSystem.Fired += OnFired;
            Fx.Burst_ += OnBurst;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Draw;
            BulletSystem.Fired -= OnFired;
            Fx.Burst_ -= OnBurst;
            if (vol) vol.weight = 0f;
            BulletSystem.HideMeshes = false;
            foreach (var f in flashes) if (f.l) f.l.enabled = false;
        }

        void LateUpdate()
        {
            var run = RunDirector.Instance;
            bool live = On && run && run.Rig && run.Rig.Cam;
            if (vol) vol.weight = live ? 1f : 0f;
            BulletSystem.HideMeshes = live && haloEnemy;
            if (adj)
            {
                bool has = live && run.Room != null && Exposure.TryGetValue(run.Room.id, out var ev);
                adj.postExposure.overrideState = has;
                if (has) adj.postExposure.value = Exposure[run.Room.id];
                bool custom = live && run.Room != null && RoomPosts.TryGetValue(run.Room.id, out var rp);
                var post = custom ? RoomPosts[run.Room.id] : new RoomPost { saturation = saturation, bloomThreshold = bloomThreshold, bloomIntensity = bloomIntensity };
                adj.saturation.value = post.saturation;
                if (bloom) { bloom.threshold.value = post.bloomThreshold; bloom.intensity.value = post.bloomIntensity; }
            }
            if (live && run.Player)
            {
                // focus on the player; the far top of the screen is the part that goes soft
                float d = Vector3.Distance(run.Rig.Cam.transform.position, run.Player.transform.position + Vector3.up * 0.8f);
                dof.focusDistance.Override(d);
                dof.aperture.Override(dofAperture); dof.focalLength.Override(dofFocalLength * Mathf.Clamp(d / 36f, 0.35f, 1.2f));
            }
            TickFlashes();
        }

        // ---------------------------------------------------------------- shot light cards
        void Draw(ScriptableRenderContext ctx, Camera cam)
        {
            var run = RunDirector.Instance;
            if (!On || !run || !poolEnemy || cam != run.Rig.Cam) return;
            var bs = run.Bullets;
            int ne = 0, np = 0;
            // sprites face the camera: the card's normal (+Y) toward it, its +Z along the screen's up
            var ct = cam.transform;
            var face = Quaternion.LookRotation(ct.up, -ct.forward);
            var lift = -ct.forward * 0.3f;   // a little toward the camera so the halo is not cut by the ground or the shooter
            if (bs)
                for (int i = 0; i < bs.LiveCount; i++)
                {
                    bs.GetLive(i, out var pos, out var dir, out var team);
                    bool pl = team == Team.Player;
                    var ground = pos; ground.y -= muzzleHeight - 0.05f;
                    var pool = Matrix4x4.TRS(ground, Quaternion.identity, new Vector3(poolRadius * 2f, 1f, poolRadius * 2f) * (pl ? 0.8f : 1f));
                    float len = pl ? playerStreakLength : streakLength;
                    var rot = Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir : Vector3.forward);
                    var streak = Matrix4x4.TRS(pos - dir * (len * 0.45f), rot, new Vector3(streakWidth * 2.2f, 1f, len));
                    float hs = pl ? playerHaloSize : haloSize;
                    var halo = Matrix4x4.TRS(pos + lift, face, new Vector3(hs, 1f, hs));
                    var core = Matrix4x4.TRS(pos + lift * 1.2f, face, new Vector3(coreSize, 1f, coreSize) * (pl ? 1.4f : 1f));
                    if (pl) { if (np >= N) continue; pp[np] = pool; sp[np] = streak; hp[np] = halo; auraTmp[np] = core; np++; }
                    else { if (ne >= N) continue; pe[ne] = pool; se[ne] = streak; he[ne] = halo; coreTmp[ne] = core; ne++; }
                }
            Submit(cam, poolEnemy, pe, ne); Submit(cam, poolPlayer, pp, np);
            // no trails (Kido 11-11: "뒤에 올챙이 꼬리 달지말고")
            Submit(cam, haloEnemy, he, ne); Submit(cam, haloPlayer, hp, np);
            Submit(cam, coreEnemy, coreTmp, ne); Submit(cam, corePlayer, auraTmp, np);
            if (run.Player)
            {
                auraM[0] = Matrix4x4.TRS(run.Player.transform.position + Vector3.up * 0.05f, Quaternion.identity, new Vector3(5f, 1f, 5f));
                Submit(cam, aura, auraM, 1);
            }
        }

        void Submit(Camera cam, Material m, Matrix4x4[] arr, int n)
        {
            if (n <= 0) return;
            var rp = new RenderParams(m) { camera = cam, layer = 0, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderMeshInstanced(rp, quad, 0, arr, n);
        }

        // ---------------------------------------------------------------- flashes
        struct Flash { public Light l; public float t, life, peak; }
        readonly List<Flash> flashes = new List<Flash>();
        const int MaxFlashes = 16;
        float nextMuzzle;

        void OnFired(Vector3 pos, Vector3 dir, Team team)
        {
            if (!On || team != Team.Player || Time.time < nextMuzzle) return;
            nextMuzzle = Time.time + 0.06f;
            Spawn(pos + dir * 0.4f, muzzleColor, 4f, 3.5f, 0.07f);
        }

        void OnBurst(Vector3 pos, Fx.Kind kind)
        {
            if (!On) return;
            switch (kind)
            {
                case Fx.Kind.Death: Spawn(pos + Vector3.up * 0.5f, killColor, 7f, 6f, 0.35f); break;
                case Fx.Kind.PlayerSpark: Spawn(pos, muzzleColor, 2.5f, 2.5f, 0.08f); break;
                default: Spawn(pos, hitColor, 2.5f, 2.5f, 0.08f); break;
            }
        }

        void Spawn(Vector3 pos, Color c, float range, float intensity, float life)
        {
            int slot = -1; float oldest = -1f;
            for (int i = 0; i < flashes.Count; i++)
            {
                var f = flashes[i];
                if (!f.l) continue;
                if (!f.l.enabled) { slot = i; break; }
                if (f.t > oldest) { oldest = f.t; slot = i; }
            }
            if (flashes.Count < MaxFlashes && (slot < 0 || flashes[slot].l.enabled))
            {
                var go = new GameObject("LookFX flash");
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point; l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
                flashes.Add(new Flash { l = l });
                slot = flashes.Count - 1;
            }
            if (slot < 0) return;
            var fl = flashes[slot];
            fl.l.transform.position = pos; fl.l.color = c; fl.l.range = range; fl.l.intensity = intensity; fl.l.enabled = true;
            fl.t = 0f; fl.life = life; fl.peak = intensity;
            flashes[slot] = fl;
        }

        void TickFlashes()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < flashes.Count; i++)
            {
                var f = flashes[i];
                if (!f.l || !f.l.enabled) continue;
                f.t += dt;
                float k = 1f - f.t / f.life;
                if (k <= 0f) f.l.enabled = false;
                else f.l.intensity = f.peak * k * k;
                flashes[i] = f;
            }
        }
    }
}
