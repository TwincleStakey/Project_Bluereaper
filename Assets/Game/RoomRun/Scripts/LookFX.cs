// Look test (Kido 11-11: "쉐이더는 다 넣어, 어느정도까지 유니티에서 룩 가능한지 보고싶어 ... 플레이어랑 적 탄막에 광원효과
// poe2같은것좀"). Everything here is additive to the stages and StageMood; nothing in a scene or asset changes.
//   Post (a global volume above StageMood): depth of field focused on the player so the far top of the screen softens,
//   a stronger wider bloom so glowing things bleed light, a touch of chromatic aberration at the edges.
//   Shots: every bullet gets a soft pool of light on the ground under it and a tapered streak along its path
//   (instanced additive cards, so thousands cost about the same as a few), on top of FieldLighting's real lights.
//   Flashes: short real point lights on player shots (muzzle), hits and kills, from a small recycled pool.
//   Player: no ground glow any more (11-13); the rim glow is PlayerGlowPass.
//   Readable cores (11-13, Kido: missiles got darkened with the dark fields): every shot also gets a crisp core sprite
//   drawn AFTER post-processing (BulletCorePass, RR_BulletCore), so no field's grade, exposure, vignette or grain can
//   dim or grey it. Player shots cyan and stretched along their path, enemy shots hot magenta-red and round. The halos
//   above stay in the scene, so they still bloom around the core.
// Off for A/B with the perf probe variant off:LookFX, or LookFX.On = false.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
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
        public Color enemyGlow = new Color(1.0f, 0.12f, 0.3f) * 0.3f;   // 11-13: magenta-red to match the readable core; halved, 100+ shots stained bright fields pink
        public Color playerGlow = new Color(0.15f, 0.7f, 1.0f) * 0.65f;
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
        // room for the 2,000+ bullet stress case; drawn 1,023 at a time
        const int N = 4096;
        readonly Matrix4x4[] auraM = new Matrix4x4[1], pe = new Matrix4x4[N], pp = new Matrix4x4[N], se = new Matrix4x4[N], sp = new Matrix4x4[N],
            he = new Matrix4x4[N], hp = new Matrix4x4[N], coreTmp = new Matrix4x4[N], auraTmp = new Matrix4x4[N];

        [Header("Readable cores (after post)")]
        public bool readableCores = true;
        public Color enemyCoreRim = new Color(1f, 0.12f, 0.38f), enemyCoreHot = new Color(1f, 0.92f, 0.96f);
        public Color playerCoreRim = new Color(0.15f, 0.85f, 1f), playerCoreHot = new Color(0.92f, 1f, 1f);
        public float enemyCoreSize = 0.9f, playerCoreWidth = 0.6f, playerCoreLength = 1.3f;
        Material overEnemy, overPlayer;
        static readonly Color EnemyHalo = new Color(1f, 0.1f, 0.3f) * 1.6f;
        BulletCorePass corePass;

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
                streakEnemy = M(new Color(1f, 0.12f, 0.3f) * 2.2f, 3, 1.1f, "trace_01_rotated"); streakPlayer = M(playerGlow * 3.8f, 3, 1.3f, "trace_04_rotated");
                haloEnemy = M(EnemyHalo, 2, 1.8f, "light_01"); haloPlayer = M(new Color(0.1f, 0.6f, 1f) * 1.6f, 2, 1.8f, "light_02");
                coreEnemy = M(new Color(1f, 0.45f, 0.65f) * 2.4f, 2, 1.5f, "light_03"); corePlayer = M(new Color(0.75f, 0.9f, 1f) * 3.2f, 2, 1.0f, "flare_01");
                aura = M(new Color(0.35f, 0.5f, 0.75f) * 0.35f, 0, 1.6f);
            }
            var osh = Resources.Load<Shader>("RR_BulletCore");
            if (osh)
            {
                Material O(Color rim, Color hot, float core, float rimR, float dark)
                {
                    var m = new Material(osh) { enableInstancing = true, hideFlags = HideFlags.DontSave };
                    m.SetColor("_Color", rim); m.SetColor("_CoreColor", hot); m.SetFloat("_Core", core); m.SetFloat("_Rim", rimR); m.SetFloat("_Dark", dark);
                    return m;
                }
                overEnemy = O(enemyCoreRim, enemyCoreHot, 0.28f, 0.5f, 0.3f);
                overPlayer = O(playerCoreRim, playerCoreHot, 0.3f, 0.55f, 0.25f);
                corePass = new BulletCorePass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
            }
            quad = new Mesh { name = "LookFX quad", hideFlags = HideFlags.DontSave };
            quad.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            quad.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            if (corePass != null) corePass.mesh = quad;

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
        struct RoomPost { public float saturation, bloomThreshold, bloomIntensity; public float flashScale; }   // flashScale: death flash intensity x (0 = 1)
        static readonly System.Collections.Generic.Dictionary<string, RoomPost> RoomPosts = new System.Collections.Generic.Dictionary<string, RoomPost>
        {
            { "snow", new RoomPost { saturation = -40f, bloomThreshold = 1.4f, bloomIntensity = 0.6f, flashScale = 0.35f } },   // 11-13 (Kido: snow too bright, glow blob round the player): 1.15 / 0.9 -> 1.4 / 0.6
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
                deathFlash = post.flashScale > 0f ? post.flashScale : 1f;
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
            if (corePass != null) corePass.Clear();
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
                    if (corePass != null && readableCores)
                    {
                        if (pl)
                        {
                            // the card's length along the shot's path as seen on screen
                            var sd = Vector3.ProjectOnPlane(dir, ct.forward);
                            var r = sd.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(sd, -ct.forward) : face;
                            corePass.Add(true, Matrix4x4.TRS(pos + lift * 1.2f, r, new Vector3(playerCoreWidth, 1f, playerCoreLength)));
                        }
                        else corePass.Add(false, Matrix4x4.TRS(pos + lift * 1.2f, face, new Vector3(enemyCoreSize, 1f, enemyCoreSize)));
                    }
                }
            // 11-13 QA: with 100+ enemy shots the additive ground pools and halos stacked into a magenta fog over the
            // floor. Their total light is held to about that of ~25 shots; the readable cores (after post) carry the shots.
            float crowd = Mathf.Clamp(25f / Mathf.Max(1, ne), 0.08f, 1f);
            poolEnemy.SetColor("_Color", enemyGlow * crowd);
            haloEnemy.SetColor("_Color", EnemyHalo * Mathf.Sqrt(crowd));
            Submit(cam, poolEnemy, pe, ne); Submit(cam, poolPlayer, pp, np);
            // no trails (Kido 11-11: "뒤에 올챙이 꼬리 달지말고")
            Submit(cam, haloEnemy, he, ne); Submit(cam, haloPlayer, hp, np);
            Submit(cam, coreEnemy, coreTmp, ne); Submit(cam, corePlayer, auraTmp, np);
            if (corePass != null && corePass.Count > 0)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null && data.scriptableRenderer != null)
                {
                    corePass.enemyMat = overEnemy; corePass.playerMat = overPlayer; corePass.cam = cam;
                    data.scriptableRenderer.EnqueuePass(corePass);
                }
            }
            // no ground aura under the player (11-13, Kido: the bright cool pool under the feet looked wrong and lit the
            // snow ice); the player's rim glow is PlayerGlowPass.
        }

        void Submit(Camera cam, Material m, Matrix4x4[] arr, int n)
        {
            if (n <= 0) return;
            var rp = new RenderParams(m) { camera = cam, layer = 0, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            for (int s = 0; s < n; s += 1023) Graphics.RenderMeshInstanced(rp, quad, 0, arr, Mathf.Min(1023, n - s), s);
        }

        // ---------------------------------------------------------------- readable cores, after post-processing
        // Drawn onto the graded image with the camera's own matrices and no depth test (a shot is never hidden behind a
        // tree or a wall), 1,023 instances per draw.
        sealed class BulletCorePass : ScriptableRenderPass
        {
            public Mesh mesh;
            public Material enemyMat, playerMat;
            public Camera cam;
            readonly List<Matrix4x4[]> enemy = new List<Matrix4x4[]>(), player = new List<Matrix4x4[]>();
            int ne, np;
            public int Count => ne + np;

            public void Clear() { ne = np = 0; }
            public void Add(bool isPlayer, Matrix4x4 m)
            {
                var list = isPlayer ? player : enemy;
                int n = isPlayer ? np : ne;
                int chunk = n / 1023;
                if (chunk >= list.Count) list.Add(new Matrix4x4[1023]);
                list[chunk][n % 1023] = m;
                if (isPlayer) np++; else ne++;
            }

            class PassData { public BulletCorePass p; public TextureHandle target; }

            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
            {
                if (!mesh || !cam || Count == 0) return;
                var res = frameData.Get<UniversalResourceData>();
                using (var builder = rg.AddRasterRenderPass<PassData>("Bullet Cores (after post)", out var data))
                {
                    data.p = this; data.target = res.activeColorTexture;
                    builder.SetRenderAttachment(data.target, 0, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                    {
                        var p = d.p;
                        // After post the target reports a bottom-left UV origin (Game view and capture RTs alike, checked
                        // 11-13) and needs the unflipped GPU projection; flipping it mirrored every core top to bottom.
                        bool flip = ctx.GetTextureUVOrigin(d.target) != TextureUVOrigin.BottomLeft;
                        ctx.cmd.SetViewProjectionMatrices(p.cam.worldToCameraMatrix, GL.GetGPUProjectionMatrix(p.cam.projectionMatrix, flip));
                        DrawChunks(ctx.cmd, p.mesh, p.enemyMat, p.enemy, p.ne);
                        DrawChunks(ctx.cmd, p.mesh, p.playerMat, p.player, p.np);
                    });
                }
            }

            static void DrawChunks(RasterCommandBuffer cmd, Mesh mesh, Material m, List<Matrix4x4[]> chunks, int n)
            {
                if (!m) return;
                for (int c = 0; c * 1023 < n; c++) cmd.DrawMeshInstanced(mesh, 0, m, 0, chunks[c], Mathf.Min(1023, n - c * 1023));
            }
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

        // 11-13 (Kido: white blob on snow when enemies die next to the player): per-field death flash scale (RoomPost.flashScale)
        float deathFlash = 1f;

        void OnBurst(Vector3 pos, Fx.Kind kind)
        {
            if (!On) return;
            switch (kind)
            {
                case Fx.Kind.Death: Spawn(pos + Vector3.up * 0.5f, killColor, 7f, 6f * deathFlash, 0.35f); break;
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
