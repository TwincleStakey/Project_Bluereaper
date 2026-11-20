// Dark-field lighting (Kido 11-10, after Path of Exile 2 refs): the field stays toned down, but
//   - each player carries a soft pool of light (~7 m, smooth falloff, a bit brighter at the feet, no shadows);
//   - bullets glow and a small pool of real point lights rides the bullets nearest the player (player shots cool
//     blue-white, enemy shots in their warm colour), so they light the floor and the enemies they pass;
//   - enemies get a rim (RoomRun/Rim) that catches those lights, so they read against the dark.
// On by default in the dark fields (DarkRooms); the other fields are left as they are. Forward+ (PC renderer) has no
// per-object light limit, so the light pool is capped here instead (MaxBulletLights).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace RoomRun
{
    public sealed class FieldLighting : MonoBehaviour
    {
        public static readonly HashSet<string> DarkRooms = new HashSet<string> { "grass", "valley", "village", "palace", "underworld", "abandoned", "reeds", "bamboo", "snow", "crypt", "d4field", "bossseal" };   // every field is dark since 11-11 (StageMood)
        public const int MaxBulletLights = 12;

        [Header("Player light pool")]
        public Color playerLight = new Color(0.95f, 0.93f, 0.88f);   // near neutral: the field grades push it warm already
        // 11-13 (Kido: "발밑이 밝으니깐 너무 이상한대", "캐릭터 자체 주위에 약간 글로우"): no visible pool or disc on the ground any
        // more, only a faint wide fill (was 7.5 m / 2.2 / 2.4 m); the player reads by its own rim glow (Player glow below)
        public float playerRange = 10f, playerIntensity = 0.35f, playerHeight = 4f;
        [Tooltip("A small tight light near the feet so the ground right around the player is the brightest spot.")]
        public float footRange = 3f, footIntensity = 0f;   // 11-13: off (was 1.2: the bright spot under the feet)
        [Header("Bullet lights")]
        public Color playerShot = new Color(0.55f, 0.82f, 1.0f);
        public Color enemyShot = new Color(1.0f, 0.15f, 0.35f);   // 11-13: magenta like the enemy bullets (LookFX cores); the old orange stacked into a yellow blob
        public float shotRange = 4.5f, shotIntensity = 3.2f;
        [Header("Player glow")]
        [Tooltip("A soft cool rim on the player's own body (hat, cloak, weapon), drawn additively over the player renderers.")]
        public Color playerGlowColor = new Color(0.42f, 0.56f, 0.85f);
        public float playerGlowPower = 2.6f, playerGlowBase = 0.5f, playerGlowCatch = 0.25f;
        [Header("Field")]
        [Tooltip("Ambient light in a dark field is scaled by this, so the light pools carry the scene.")]
        [Range(0.3f, 1f)] public float ambientScale = 0.75f;

        // Read by EnemyController when an enemy is made: the extra rim material slot, or null when the field is not dark.
        public static Material EnemyRim { get; private set; }
        public static bool Active { get; private set; }

        readonly List<Light> poolLights = new List<Light>();
        readonly List<(Light main, Light foot)> playerLights = new List<(Light, Light)>();
        readonly List<(float d, Vector3 p, Team t)> candidates = new List<(float, Vector3, Team)>();
        Material rimMat, playerRimMat;
        PlayerGlowPass glowPass;
        readonly List<Renderer> playerRenderers = new List<Renderer>();
        int sceneSeen = -1;
        PlayerController[] players; float nextPlayerScan;

        RunDirector run;

        void Awake()
        {
            run = GetComponent<RunDirector>();
            var sh = Shader.Find("RoomRun/Rim");
            if (sh) rimMat = new Material(sh) { name = "RR_Rim (runtime)" };
            if (sh)
            {
                playerRimMat = new Material(sh) { name = "RR_PlayerGlow (runtime)" };
                playerRimMat.SetColor("_Color", playerGlowColor); playerRimMat.SetFloat("_Power", playerGlowPower);
                playerRimMat.SetFloat("_Base", playerGlowBase); playerRimMat.SetFloat("_Catch", playerGlowCatch);
            }
            glowPass = new PlayerGlowPass { renderPassEvent = RenderPassEvent.AfterRenderingTransparents };
            var osh = Shader.Find("Hidden/RoomRun/HeroOccOutline");
            if (osh) overVeil.outlineMat = new Material(osh) { name = "RR_HeroOccOutline (runtime)" };
            ApplyOutlineLook();
            OpenOccLog();
        }

        // 11-16 outline v2 look (screen-space line, see RR_HeroOccOutline.shader). Warm neutral, brighter than the thinned
        // v1 (line .5 on a dotted fresnel band) but far below the v1 blob (.7 line + .16 fill over a wide band).
        public static Color OutlineColor = new Color(0.96f, 0.88f, 0.74f, 0.72f);   // a = line strength
        public static float OutlinePx = 2f;      // line width in pixels at 1080p
        public static float OutlineFill = 0.05f;
        public static bool OutlineV1;            // shots token occv=1: the old GSOccluded fresnel pass, for A/B
        // 11-18 always-on outer line (Kido: the rim light striped the AI cloth texture, "다른 방법"): a thin cool-pale ring
        // just outside the visible silhouette, every frame (shots token heroline=<0..1>, 0 = off). The hero material's rim
        // and top key were turned down at the same time (GSHeroRemote.ClothRim / ClothTopKey).
        public static float HeroOutline = 1f;
        public static Color HeroOutlineColor = new Color(0.78f, 0.88f, 1f, 0.55f);   // a = line strength
        static readonly int HeroOutlineId = Shader.PropertyToID("_HeroOutlineAll"), HeroOutlineColId = Shader.PropertyToID("_HeroOutlineAllColor");
        void ApplyOutlineLook()
        {
            Shader.SetGlobalFloat(HeroOutlineId, HeroOutline);
            Shader.SetGlobalColor(HeroOutlineColId, HeroOutlineColor);
            var m = overVeil.outlineMat; if (!m) return;
            m.SetColor("_OutlineColor", OutlineColor); m.SetFloat("_OutlinePx", OutlinePx); m.SetFloat("_OutlineFill", OutlineFill);
        }

        void LateUpdate()
        {
            if (!run) return;
            // every room visit loads its stage scene (even the same room again), which resets the ambient
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (scene != sceneSeen && !run.Transitioning && run.Room != null) { sceneSeen = scene; Enter(DarkRooms.Contains(run.Room.id)); }
            UpdateOcclusion();
            ScanPlayers();   // 11-18: every room (the always-on hero outline needs the renderer list outside the dark rooms too)
            if (!Active) return;
            UpdatePlayers();
            UpdateBullets();
        }

        // per-stage light scale (11-13, Kido: snow too bright, glow blob round the player): the white snow returns these
        // lights at full strength, so on snow the player key/foot lights and the bullet lights run at half
        float stageScale = 1f, playerScale = 1f;

        void Enter(bool dark)
        {
            Active = dark;
            stageScale = run.Room != null && run.Room.id == "snow" ? 0.5f : 1f;
            playerScale = run.Room != null && (run.Room.id == "crypt" || run.Room.id == "d4field" || run.Room.id == "bossseal") ? 0f : 1f;   // DarkCrypt (11-15): no light pool under the player
            EnemyRim = dark ? rimMat : null;
            // the stage scene set its own ambient when it loaded; dim it once for this visit
            if (dark)
            {
                RenderSettings.ambientSkyColor *= ambientScale;
                RenderSettings.ambientEquatorColor *= ambientScale;
                RenderSettings.ambientGroundColor *= ambientScale;
            }
            foreach (var l in poolLights) if (l) l.enabled = false;
            foreach (var (m, f) in playerLights) { if (m) m.enabled = dark; if (f) f.enabled = dark; }
        }

        Light MakeLight(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            return l;
        }

        void ScanPlayers()
        {
            // looked up twice a second, not every frame
            if (Time.unscaledTime >= nextPlayerScan || players == null)
            {
                players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None); nextPlayerScan = Time.unscaledTime + 0.5f;
                playerRenderers.Clear(); ownRimRenderers.Clear();
                foreach (var pc in players)
                    if (pc) foreach (var r in pc.GetComponentsInChildren<Renderer>())
                    {
                        if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                        if (OwnRim(r)) ownRimRenderers.Add(r); else playerRenderers.Add(r);
                    }
            }
        }

        void UpdatePlayers()
        {
            // one pool per player (co-op ready: every PlayerController gets its own)
            ScanPlayers();
            while (playerLights.Count < players.Length) playerLights.Add((MakeLight("PlayerLightPool", transform), MakeLight("PlayerFootLight", transform)));
            for (int i = 0; i < playerLights.Count; i++)
            {
                var (main, foot) = playerLights[i];
                bool on = i < players.Length && players[i];
                main.enabled = foot.enabled = on && playerScale > 0f;
                if (!on) continue;
                var p = players[i].transform.position;
                main.transform.position = p + Vector3.up * playerHeight;
                main.color = playerLight; main.range = playerRange; main.intensity = playerIntensity * stageScale * playerScale;
                foot.transform.position = p + Vector3.up * 0.9f;
                foot.color = playerLight; foot.range = footRange; foot.intensity = footIntensity * stageScale * playerScale;
            }
        }

        // 11-15: GS_Hero (GSHero/Readable) has its own neutral rim; the cool additive glow over its near-black textures turned
        // the whole player navy/purple from above
        static bool OwnRim(Renderer r)
        {
            var m = r.sharedMaterial;
            return m && m.shader && m.shader.name == "GSHero/Readable";
        }

        void UpdateBullets()
        {
            var focus = run.Player ? run.Player.transform.position : Vector3.zero;
            candidates.Clear();
            var bs = run.Bullets;
            if (bs)
                for (int i = 0; i < bs.LiveCount; i++)
                {
                    bs.GetLive(i, out var p, out var team);
                    candidates.Add(((p - focus).sqrMagnitude, p, team));
                }
            candidates.Sort((a, b) => a.d.CompareTo(b.d));
            int n = Mathf.Min(candidates.Count, MaxBulletLights);
            while (poolLights.Count < n) poolLights.Add(MakeLight("BulletLight", transform));
            for (int i = 0; i < poolLights.Count; i++)
            {
                var l = poolLights[i];
                if (i >= n) { l.enabled = false; continue; }
                var c = candidates[i];
                l.enabled = true;
                l.transform.position = c.p + Vector3.up * 0.6f;
                l.color = c.t == Team.Player ? playerShot : enemyShot;
                l.range = shotRange; l.intensity = shotIntensity * stageScale;
            }
        }

        // ---------------------------------------------------------------- occluded hero outline (11-15 visibility pass)
        // GSHero/Readable has a "GSOccluded" pass (thin warm outline + faint fill where the hero is behind something). It is
        // only wanted behind buildings / big props, never through the reeds (they hide the player on purpose), so it is
        // gated here: three rays camera -> hero (shin, chest, hat) against colliders. Reeds, grass and other instanced foliage
        // have no colliders; enemies/the player (Actor layer) and invisible walls (Wall layer) are outside the mask, and a
        // hit only counts when something visible (a renderer) belongs to the collider. Global _GSOccluded fades 0..1.
        [Header("Occluded hero outline")]
        public bool occludedOutline = true;
        [Tooltip("Also show the outline when enemies are on top of the hero (Kido 11-15: on). Off = buildings/big props only.")]
        public static bool OutlineThroughEnemies = OutlineThroughEnemiesDefault;
        public const bool OutlineThroughEnemiesDefault = true;
        public static bool ForceOutline;   // dev check of the outline pass itself (shots token occe=2)
        static readonly int OccludedId = Shader.PropertyToID("_GSOccluded");
        static readonly float[] OccHeights = { 0.45f, 1.0f, 1.55f };
        readonly RaycastHit[] occHits = new RaycastHit[16];
        float occluded;
        public float Occluded => occluded;

        void UpdateOcclusion()
        {
            bool hidden = false;
            var cam = run.Rig ? run.Rig.Cam : null;
            if (occludedOutline && cam && run.Player && !run.Transitioning)
            {
                var pt = run.Player.transform;
                Vector3 from = cam.transform.position;
                foreach (float h in OccHeights)
                {
                    Vector3 d = pt.position + Vector3.up * h - from;
                    float dist = d.magnitude;
                    if (dist < 0.5f) continue;
                    int n = Physics.RaycastNonAlloc(from, d / dist, occHits, dist - 0.35f, Ground.GroundMask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n && !hidden; i++) if (hidden = Blocks(occHits[i].collider, pt)) occWhy = "ray:" + occHits[i].collider.name;
                    if (hidden) break;
                }
                // big props without colliders (the stage's foreground silhouettes, roofs, trees): ray vs their bounds
                if (!hidden)
                {
                    if (Time.unscaledTime >= nextBigScan) ScanBigRenderers(pt);
                    foreach (float h in OccHeights)
                    {
                        Vector3 d = pt.position + Vector3.up * h - from;
                        float dist = d.magnitude;
                        var ray = new Ray(from, d / dist);
                        foreach (var r in bigRenderers)
                            if (r && r.enabled && r.isVisible && r.bounds.IntersectRay(ray, out float t) && t < dist - 0.8f && BoundsBlock(r.bounds, ray, t, dist)) { hidden = true; occWhy = "big:" + r.name; break; }
                        if (hidden) break;
                    }
                }
                if (!hidden && OutlineThroughEnemies && run.Enemies != null)
                {
                    var hp = pt.position;
                    foreach (var e in run.Enemies)
                        if (e && (e.transform.position - hp).sqrMagnitude < 1.2f * 1.2f) { hidden = true; occWhy = "enemy"; break; }
                }
            }
            if (ForceOutline) { hidden = true; occWhy = "force"; }
            occluded = Mathf.MoveTowards(occluded, hidden ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            Shader.SetGlobalFloat(OccludedId, occluded);
            if (Time.unscaledTime >= nextParticleLightScan) MuteParticleLights();
            if (occLog != null) LogOcclusion(hidden);
        }

        // 11-16 swing tint (hero turned pale ice-blue for every swing): the slash VFX pack (GS_SlashWater_Flat) has a particle
        // Lights module (ratio 1, a 2 m / intensity 3 point light per particle, coloured by the icy-tinted particles).
        // GreatswordCombat switches the pack's template Light component off, but the Lights module keeps spawning lights from
        // the template anyway, so every arc lit the hero. Rule here: a Lights module whose template Light is disabled is
        // switched off as well (scanned once a second, also catches pools built after a room change).
        public static bool MuteDisabledParticleLights = true;
        float nextParticleLightScan;
        void MuteParticleLights()
        {
            nextParticleLightScan = Time.unscaledTime + 1f;
            if (!MuteDisabledParticleLights) return;
            foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var lm = ps.lights;
                if (lm.enabled && (!lm.light || !lm.light.enabled)) lm.enabled = false;
            }
        }

        // dev log (editor only): ArtSource/fl_occ_log.on exists -> ArtSource/fl_occ_log.csv gets one row per frame while
        // playing: gate result, which test fired, what it hit. "noplfix" in the .on file = particle-light mute off (before run).
        System.IO.StreamWriter occLog;
        string occWhy = "";
        void OpenOccLog()
        {
            if (!Application.isEditor) return;
            string on = System.IO.Path.Combine(Application.dataPath, "..", "ArtSource", "fl_occ_log.on");
            if (!System.IO.File.Exists(on)) return;
            MuteDisabledParticleLights = !System.IO.File.ReadAllText(on).Contains("noplfix");
            occLog = new System.IO.StreamWriter(System.IO.Path.Combine(Application.dataPath, "..", "ArtSource", "fl_occ_log.csv"), false);
            occLog.WriteLine("frame,time,hidden,occluded,why,particleLights,nearLights");
        }
        void LogOcclusion(bool hidden)
        {
            int pl = 0;
            foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) if (ps.lights.enabled && ps.particleCount > 0) pl += ps.particleCount;
            int near = 0;
            if (run.Player)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.enabled && l.type != LightType.Directional && (l.transform.position - run.Player.transform.position).sqrMagnitude < 9f) near++;
            occLog.WriteLine($"{Time.frameCount},{Time.time:0.000},{(hidden ? 1 : 0)},{occluded:0.00},{occWhy},{pl},{near}");
            occWhy = "";
        }

        // renderers at least 2.5 m tall (buildings, roofs, trees, big rocks) in the stage, rescanned every 2 s. Instanced
        // foliage (grass, reeds, bamboo buffers) has no Renderer, enemies/player/particles are left out.
        readonly List<Renderer> bigRenderers = new List<Renderer>();
        float nextBigScan;
        void ScanBigRenderers(Transform player)
        {
            nextBigScan = Time.unscaledTime + 2f;
            bigRenderers.Clear();
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r || r.transform.IsChildOf(player) || r.GetComponentInParent<EnemyController>() || !SolidOccluder(r)) continue;
                var s = r.bounds.size;
                // > 30 m across = ground / cliff chunks, never an occluder test (their boxes cover the whole arena)
                if (s.y >= 2.5f && Mathf.Max(s.x, s.z) >= 1.0f && Mathf.Max(s.x, s.z) <= 30f) bigRenderers.Add(r);
            }
        }
        // the ray enters the box high up (above ~2 m over the hero's feet level at that point) and the hit is the part of
        // the box that is really solid: the entry point must be inside the lower 85% of the box (roof tips and crowns
        // overhang, their AABB corners are mostly air) - keeps the AABB test from firing on every tree near the hero
        static bool BoundsBlock(Bounds b, Ray ray, float t, float dist)
        {
            Vector3 p = ray.GetPoint(t + 0.3f);
            return p.y <= b.min.y + b.size.y * 0.85f && Mathf.Abs(p.x - b.center.x) <= b.extents.x * 0.8f && Mathf.Abs(p.z - b.center.z) <= b.extents.z * 0.8f;
        }

        static bool Blocks(Collider c, Transform player)
        {
            if (!c || c.transform.IsChildOf(player) || c is TerrainCollider || c is CharacterController) return false;
            if (c.GetComponentInParent<EnemyController>()) return false;
            var r = c.GetComponent<Renderer>();
            if (!r) r = c.GetComponentInParent<Renderer>();
            if (!r) r = c.GetComponentInChildren<Renderer>();
            return r && r.enabled && SolidOccluder(r);
        }

        // 11-16: only opaque world geometry gates the outline. VFX (particles, trails, lines, anything transparent/additive:
        // the pooled slash arcs, the blade trail, projectiles, cut debris) never does, whatever its size - matched by
        // renderer type, render queue and shader name, not by bounds, since VFX scale may change.
        static bool SolidOccluder(Renderer r)
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r is BillboardRenderer) return false;
            var m = r.sharedMaterial;
            if (!m || !m.shader) return false;
            if (m.renderQueue > 2500) return false;   // transparent/additive (alpha-test cut-outs 2450-2500 = leaves, thatch stay)
            string sn = m.shader.name;
            return !(sn.Contains("Slash") || sn.Contains("Trail") || sn.Contains("Particle") || sn.Contains("Sprite") || sn.Contains("VFX"));
        }

        void OnEnable() { RenderPipelineManager.beginCameraRendering += InjectGlow; }
        void OnDestroy() { if (occLog != null) { occLog.Close(); occLog = null; } }
        void OnDisable() { Active = false; EnemyRim = null; RenderPipelineManager.beginCameraRendering -= InjectGlow; Shader.SetGlobalFloat(OccludedId, 0f); }

        // ---------------------------------------------------------------- player glow (11-13)
        void InjectGlow(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam.cameraType != CameraType.Game) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            if (ownRimRenderers.Count > 0)
            {
                overVeil.renderers = ownRimRenderers;
                overVeil.occluded = occluded > 0.001f;
                overVeil.v1 = OutlineV1;
                ApplyOutlineLook();
                data.scriptableRenderer.EnqueuePass(overVeil);
            }
            if (!Active || !playerRimMat || playerRenderers.Count == 0) return;
            glowPass.mat = playerRimMat; glowPass.renderers = playerRenderers;
            data.scriptableRenderer.EnqueuePass(glowPass);
        }

        // 11-15 v3 (Kido: GS_Hero "뿌옇고 잘 안 보인다"): LookFX's field veil (a height-fog pass after the opaques, from 8 m off the
        // camera) greyed the player as much as the ground around it. The GSHero/Readable renderers are drawn once more right
        // after it with their own material (depth equal, same skinning), so the player keeps its own colour and contrast.
        readonly List<Renderer> ownRimRenderers = new List<Renderer>();
        readonly PlayerOverVeilPass overVeil = new PlayerOverVeilPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        sealed class PlayerOverVeilPass : ScriptableRenderPass
        {
            public List<Renderer> renderers;
            public bool occluded, v1;
            public Material outlineMat;
            class PassData { public List<Renderer> rs; public bool occ; }
            class MaskData { public List<Renderer> rs; public Material mat; }
            class CompData { public TextureHandle mask; public Material mat; public Vector4 size; }
            static readonly int MaskSizeId = Shader.PropertyToID("_MaskSize");
            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>();
                if (renderers == null) return;
                bool v2 = (occluded || HeroOutline > 0f) && !v1 && outlineMat;
                using (var b = rg.AddRasterRenderPass<PassData>("RR Player Over Veil", out var d))
                {
                    // v1 GSOccluded fresnel pass only for A/B (shots token occv=1) or when the v2 shader is missing
                    d.rs = renderers; d.occ = occluded && !v2;
                    b.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.Write);
                    b.SetRenderAttachmentDepth(res.activeDepthTexture, AccessFlags.ReadWrite);
                    b.AllowPassCulling(false);
                    b.AllowGlobalStateModification(true);
                    b.SetRenderFunc((PassData p, RasterGraphContext c) =>
                    {
                        // 11-15: the hidden parts of the hero as a thin outline (GSOccluded pass, ZTest Greater), only while
                        // FieldLighting's occlusion rays hit a building/big prop. Drawn FIRST: it also lands on the hero's own
                        // back parts (behind its front), and the redraw below (depth equal) paints the visible hero over that.
                        // (A stencil bit did not work here: the bit read as set everywhere in this pass.)
                        if (p.occ)
                            foreach (var r in p.rs)
                            {
                                if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                                var ms = r.sharedMaterials;
                                for (int s = 0; s < ms.Length; s++)
                                {
                                    if (!ms[s]) continue;
                                    int pi = ms[s].FindPass("GSOccluded");
                                    if (pi >= 0) c.cmd.DrawRenderer(r, ms[s], s, pi);
                                }
                            }
                        foreach (var r in p.rs)
                        {
                            if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                            var ms = r.sharedMaterials;
                            for (int s = 0; s < ms.Length; s++) if (ms[s]) c.cmd.DrawRenderer(r, ms[s], s, 0);
                        }
                    });
                }
                if (!v2) return;

                // 11-16 v2 (v1 read as a dotted fresnel ghost on the low-poly normals): screen-space outline. Mask R = whole
                // hero silhouette, G = its visible part (depth test against the scene depth, which already holds the hero
                // itself, so self-overlap counts as visible). One fullscreen pass then draws a ~2 px (1080p) ring just
                // outside the silhouette next to the hidden parts (RR_HeroOccOutline.shader).
                var dd = rg.GetTextureDesc(res.activeDepthTexture);
                var desc = rg.GetTextureDesc(res.activeColorTexture);
                desc.name = "RR_HeroOccMask";
                desc.format = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8_UNorm;
                desc.msaaSamples = dd.msaaSamples;
                desc.clearBuffer = true; desc.clearColor = Color.clear;
                desc.filterMode = FilterMode.Bilinear; desc.wrapMode = TextureWrapMode.Clamp;
                var mask = rg.CreateTexture(desc);
                using (var b = rg.AddRasterRenderPass<MaskData>("RR Hero Occ Mask", out var d))
                {
                    d.rs = renderers; d.mat = outlineMat;
                    b.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    b.SetRenderAttachmentDepth(res.activeDepthTexture, AccessFlags.Read);
                    b.AllowPassCulling(false);
                    b.SetRenderFunc((MaskData p, RasterGraphContext c) =>
                    {
                        foreach (var r in p.rs)
                        {
                            if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                            int subs = r.sharedMaterials.Length;
                            for (int s = 0; s < subs; s++) { c.cmd.DrawRenderer(r, p.mat, s, 0); c.cmd.DrawRenderer(r, p.mat, s, 1); }
                        }
                    });
                }
                using (var b = rg.AddRasterRenderPass<CompData>("RR Hero Occ Outline", out var d))
                {
                    d.mask = mask; d.mat = outlineMat; d.size = new Vector4(desc.width, desc.height, 0, 0);
                    b.UseTexture(mask, AccessFlags.Read);
                    b.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.Write);
                    b.AllowPassCulling(false);
                    b.AllowGlobalStateModification(true);
                    b.SetRenderFunc((CompData p, RasterGraphContext c) =>
                    {
                        c.cmd.SetGlobalVector(MaskSizeId, p.size);
                        Blitter.BlitTexture(c.cmd, p.mask, new Vector4(1, 1, 0, 0), p.mat, 2);
                    });
                }
            }
        }

        // the player's renderers once more with the additive rim material, depth-tested against the scene (no depth write)
        sealed class PlayerGlowPass : ScriptableRenderPass
        {
            public Material mat;
            public List<Renderer> renderers;
            class PassData { public Material mat; public List<Renderer> rs; }

            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>();
                if (mat == null || renderers == null) return;
                using (var b = rg.AddRasterRenderPass<PassData>("RR Player Glow", out var d))
                {
                    d.mat = mat; d.rs = renderers;
                    b.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.Write);
                    b.SetRenderAttachmentDepth(res.activeDepthTexture, AccessFlags.Read);
                    b.AllowPassCulling(false);
                    b.SetRenderFunc((PassData p, RasterGraphContext c) =>
                    {
                        foreach (var r in p.rs)
                        {
                            if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                            int subs = r.sharedMaterials.Length;
                            for (int s = 0; s < subs; s++) c.cmd.DrawRenderer(r, p.mat, s, 0);
                        }
                    });
                }
            }
        }
    }
}
