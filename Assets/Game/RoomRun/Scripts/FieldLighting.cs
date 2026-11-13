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
        public static readonly HashSet<string> DarkRooms = new HashSet<string> { "grass", "valley", "village", "palace", "underworld", "abandoned", "reeds", "bamboo", "snow" };   // every field is dark since 11-11 (StageMood)
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
        }

        void LateUpdate()
        {
            if (!run) return;
            // every room visit loads its stage scene (even the same room again), which resets the ambient
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (scene != sceneSeen && !run.Transitioning && run.Room != null) { sceneSeen = scene; Enter(DarkRooms.Contains(run.Room.id)); }
            if (!Active) return;
            UpdatePlayers();
            UpdateBullets();
        }

        // per-stage light scale (11-13, Kido: snow too bright, glow blob round the player): the white snow returns these
        // lights at full strength, so on snow the player key/foot lights and the bullet lights run at half
        float stageScale = 1f;

        void Enter(bool dark)
        {
            Active = dark;
            stageScale = run.Room != null && run.Room.id == "snow" ? 0.5f : 1f;
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

        void UpdatePlayers()
        {
            // one pool per player (co-op ready: every PlayerController gets its own)
            // looked up twice a second, not every frame
            if (Time.unscaledTime >= nextPlayerScan || players == null)
            {
                players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None); nextPlayerScan = Time.unscaledTime + 0.5f;
                playerRenderers.Clear();
                foreach (var pc in players)
                    if (pc) foreach (var r in pc.GetComponentsInChildren<Renderer>())
                        if (!(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer)) playerRenderers.Add(r);
            }
            while (playerLights.Count < players.Length) playerLights.Add((MakeLight("PlayerLightPool", transform), MakeLight("PlayerFootLight", transform)));
            for (int i = 0; i < playerLights.Count; i++)
            {
                var (main, foot) = playerLights[i];
                bool on = i < players.Length && players[i];
                main.enabled = foot.enabled = on;
                if (!on) continue;
                var p = players[i].transform.position;
                main.transform.position = p + Vector3.up * playerHeight;
                main.color = playerLight; main.range = playerRange; main.intensity = playerIntensity * stageScale;
                foot.transform.position = p + Vector3.up * 0.9f;
                foot.color = playerLight; foot.range = footRange; foot.intensity = footIntensity * stageScale;
            }
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

        void OnEnable() { RenderPipelineManager.beginCameraRendering += InjectGlow; }
        void OnDisable() { Active = false; EnemyRim = null; RenderPipelineManager.beginCameraRendering -= InjectGlow; }

        // ---------------------------------------------------------------- player glow (11-13)
        void InjectGlow(ScriptableRenderContext ctx, Camera cam)
        {
            if (!Active || !playerRimMat || playerRenderers.Count == 0 || cam.cameraType != CameraType.Game) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            glowPass.mat = playerRimMat; glowPass.renderers = playerRenderers;
            data.scriptableRenderer.EnqueuePass(glowPass);
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
