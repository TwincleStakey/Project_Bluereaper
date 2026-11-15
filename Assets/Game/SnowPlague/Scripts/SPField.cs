// Snow plague field driver. Sets the shader globals every SnowPlague shader reads (snow, rim, see-through, flicker,
// plus the ReedField wind/fog globals that SPCommon.hlsl pulls in), keeps the up-to-3 player screen positions for the
// foreground dither hole, flickers the stone lanterns (+-10%), and adds two passes from script to each game/scene
// camera (so the project's URP renderer asset is not changed, same as InkFilter):
//   - full-screen height fog after opaques (bullets and VFX drawn later stay crisp)
//   - the occluded-player silhouette (player renderers again with ZTest Greater, after the opaque foreground)
// Monsters cast no realtime shadows here: they get an instanced blob under the feet and, near a lit lantern, a quad
// stretched away from it (SnowPlague/BlobShadow).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SnowPlague
{
    [ExecuteAlways]
    public class SPField : MonoBehaviour
    {
        [Header("Arena")]
        public Vector2 arena = new Vector2(112, 88);

        [Header("Snow")]
        [Range(0, 1)] public float snowAmount = 0.75f;
        public Vector4 snowParams = new Vector4(0.35f, 0.35f, 0.45f, 0.25f);

        [Header("Wind (ReedField globals)")]
        public float windAngle = 35f;
        [Range(0, 2)] public float windStrength = 0.45f;
        public float gustSpeed = 0.6f, gustSize = 14f, gustStrength = 0.5f, flutter = 0.3f;

        [Header("Rim")]
        [ColorUsage(false, true)] public Color rimWarm = new Color(1.6f, 0.62f, 0.22f);
        [Range(0, 1)] public float rimWarmStrength = 0.25f;
        [ColorUsage(false, true)] public Color rimCold = new Color(0.45f, 0.55f, 0.7f);
        [Range(0, 1)] public float rimColdStrength = 0.12f;

        [Header("See-through (foreground dither hole)")]
        public bool seeThrough = true;
        [Range(0.02f, 0.3f)] public float holeRadius = 0.12f;   // fraction of screen height
        [Range(0.005f, 0.1f)] public float holeEdge = 0.04f;
        public float depthMargin = 1.0f;
        [Tooltip("Editor captures: stand-in player (Play finds the RoomRun 'Player' by itself).")]
        public Transform playerOverride;

        [Header("Lanterns")]
        public Light[] lanterns = new Light[0];
        public float warmRadius = 7f;
        [Range(0, 0.3f)] public float flickerAmount = 0.1f;
        public float flickerSpeed = 3.5f;

        [Header("Height fog (full screen, after opaques)")]
        public Material heightFog;
        [Header("Occluded player silhouette")]
        public Material silhouette;
        [Tooltip("e14: only occluders this high above the player's feet trigger the silhouette (enemies do not).")]
        public float silhouetteMinHeight = 2.6f;
        [Header("Monster blob shadows")]
        public Material blobShadow;     // round blob under the feet (_Stretch 0)
        public Material blobStretch;    // stretched away from a lantern (_Stretch 1)
        public float blobSize = 1.3f;

        static readonly int ID_Snow = Shader.PropertyToID("_SP_SnowAmount"), ID_SnowP = Shader.PropertyToID("_SP_SnowParams"),
            ID_Lan = Shader.PropertyToID("_SP_Lanterns"), ID_RimW = Shader.PropertyToID("_SP_RimWarm"), ID_RimC = Shader.PropertyToID("_SP_RimCold"),
            ID_GroundOn = Shader.PropertyToID("_SP_GroundOn"), ID_GroundY = Shader.PropertyToID("_SP_GroundY"),
            ID_Players = Shader.PropertyToID("_PlayerScreenPos"), ID_See = Shader.PropertyToID("_SP_SeeThrough"), ID_Flick = Shader.PropertyToID("_SP_Flicker"),
            ID_RFTime = Shader.PropertyToID("_RF_Time"), ID_RFWind = Shader.PropertyToID("_RF_Wind"), ID_RFGust = Shader.PropertyToID("_RF_Gust"),
            ID_RFStalk = Shader.PropertyToID("_RF_Stalkers"), ID_RFFogCol = Shader.PropertyToID("_RF_FogCol"), ID_RFFogP = Shader.PropertyToID("_RF_FogParams");

        readonly Vector4[] players = new Vector4[3];
        readonly Vector4[] lanArr = new Vector4[4];
        static readonly Vector4[] noStalkers = new Vector4[4];   // 11-12 perf: no per-frame arrays
        readonly float[] best = new float[4];
        float[] lanBase;
        Transform player;
        Renderer[] playerRenderers = new Renderer[0];
        float nextFind;
        FogPass fogPass;
        SilhouettePass silPass;

        void OnEnable()
        {
            lanBase = new float[lanterns.Length];
            for (int i = 0; i < lanterns.Length; i++) lanBase[i] = lanterns[i] ? lanterns[i].intensity : 0f;
            fogPass = new FogPass { renderPassEvent = RenderPassEvent.AfterRenderingSkybox };
            silPass = new SilhouettePass { renderPassEvent = RenderPassEvent.AfterRenderingSkybox + 1 };
            RenderPipelineManager.beginCameraRendering += Inject;
            SetGlobals(Camera.main);
            // 11-14: temporal AA + no film grain while this field is live (twig shimmer, SPTemporalAA.cs)
            if (Application.isPlaying && !GetComponent<SPTemporalAA>()) gameObject.AddComponent<SPTemporalAA>();
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Inject;
            for (int i = 0; i < lanterns.Length && lanBase != null && i < lanBase.Length; i++) if (lanterns[i]) lanterns[i].intensity = lanBase[i];
            // leave nothing behind for the next stage (RoomRun loads stages in Single mode; globals survive)
            Shader.SetGlobalVector(ID_See, Vector4.zero);
            Shader.SetGlobalFloat(ID_Snow, 0f);
        }

        void Update()
        {
            if (Application.isPlaying && Time.unscaledTime >= nextFind)
            {
                nextFind = Time.unscaledTime + 1f;
                var go = GameObject.Find("Player");
                player = go ? go.transform : null;
                playerRenderers = player ? player.GetComponentsInChildren<Renderer>() : new Renderer[0];
                FindMonsters();
            }
            Shader.SetGlobalFloat("_SP_SilMinY", player ? player.position.y + silhouetteMinHeight : 0f);
            // lantern flicker: one shared value (shader cores and light shafts use the same global)
            float t = Application.isPlaying ? Time.time : 0f;
            float f = Mathf.PerlinNoise(t * flickerSpeed, 0.37f);
            for (int i = 0; i < lanterns.Length && i < lanBase.Length; i++)
                if (lanterns[i]) lanterns[i].intensity = lanBase[i] * (1f + (Mathf.PerlinNoise(t * flickerSpeed, i * 1.7f) * 2f - 1f) * flickerAmount);
            Shader.SetGlobalFloat(ID_Flick, f);
            if (Application.isPlaying) DrawBlobs();
        }

        void LateUpdate() => SetGlobals(Camera.main);

        public void SetGlobals(Camera cam)
        {
            Shader.SetGlobalFloat(ID_Snow, snowAmount);
            Shader.SetGlobalVector(ID_SnowP, snowParams);
            Shader.SetGlobalVector(ID_RimW, new Vector4(rimWarm.r, rimWarm.g, rimWarm.b, rimWarmStrength));
            Shader.SetGlobalVector(ID_RimC, new Vector4(rimCold.r, rimCold.g, rimCold.b, rimColdStrength));
            Shader.SetGlobalFloat(ID_GroundOn, 0f);   // baked ground albedo for prop melt: not made yet
            Shader.SetGlobalFloat(ID_GroundY, transform.position.y);
            // ReedField globals used through ReedCommon.hlsl. Its own distance/ground fog is off here (the full-screen
            // height fog does that), trail/stalkers/shadow maps from earlier stages are cleared.
            Shader.SetGlobalFloat(ID_RFTime, Application.isPlaying ? Time.time : (float)(Time.realtimeSinceStartupAsDouble % 3600.0));
            float a = windAngle * Mathf.Deg2Rad;
            Shader.SetGlobalVector(ID_RFWind, new Vector4(Mathf.Sin(a), Mathf.Cos(a), windStrength, gustSpeed));
            Shader.SetGlobalVector(ID_RFGust, new Vector4(gustSize, gustStrength, flutter, 0f));
            Shader.SetGlobalVectorArray(ID_RFStalk, noStalkers);
            Shader.SetGlobalVector(ID_RFFogCol, new Vector4(0.6f, 0.67f, 0.72f, 0f));
            Shader.SetGlobalVector(ID_RFFogP, Vector4.zero);
            Shader.SetGlobalVector("_RF_FogArena", new Vector4(transform.position.x, transform.position.z, arena.x * 0.5f, arena.y * 0.5f));
            Shader.SetGlobalFloat("_RF_ShadowOn", 0f);
            Shader.SetGlobalTexture("_RF_PropShadow", Texture2D.whiteTexture);
            Shader.SetGlobalFloat("_LSTrailOn", 0f);

            // the 4 lit lanterns closest to what the camera looks at drive the warm rim
            var focus = cam ? cam.transform.position + cam.transform.forward * 22f : transform.position;
            for (int k = 0; k < 4; k++) lanArr[k] = Vector4.zero;
            for (int k = 0; k < 4; k++) best[k] = 1e9f;
            foreach (var l in lanterns)
            {
                if (!l || !l.isActiveAndEnabled) continue;
                var p = l.transform.position; float d = (p - focus).sqrMagnitude;
                for (int k = 0; k < 4; k++)
                    if (d < best[k])
                    {
                        for (int j = 3; j > k; j--) { best[j] = best[j - 1]; lanArr[j] = lanArr[j - 1]; }
                        best[k] = d; lanArr[k] = new Vector4(p.x, p.y, p.z, warmRadius); break;
                    }
            }
            Shader.SetGlobalVectorArray(ID_Lan, lanArr);

            // players on screen (viewport xy, linear eye depth z, w = active)
            for (int k = 0; k < 3; k++) players[k] = Vector4.zero;
            var pl = playerOverride ? playerOverride : player;
            if (cam && pl)
            {
                var c = pl.position + Vector3.up * 1f;
                var vp = cam.WorldToViewportPoint(c);
                if (vp.z > 0) players[0] = new Vector4(vp.x, vp.y, vp.z, 1);
            }
            Shader.SetGlobalVectorArray(ID_Players, players);
            Shader.SetGlobalVector(ID_See, new Vector4(holeRadius, holeEdge, depthMargin, seeThrough ? 1 : 0));
        }

        // ------------------------------------------------------------------ monster blob shadows
        readonly List<Transform> monsters = new List<Transform>();
        readonly List<Matrix4x4> blobs = new List<Matrix4x4>();
        Mesh quad;
        System.Type enemyType;

        void FindMonsters()
        {
            monsters.Clear();
            if (enemyType == null)
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                    if ((enemyType = asm.GetType("RoomRun.EnemyController")) != null) break;
            if (enemyType == null) return;
            foreach (var o in FindObjectsByType(enemyType, FindObjectsSortMode.None))
            {
                var c = o as Component; if (!c) continue;
                monsters.Add(c.transform);
                foreach (var r in c.GetComponentsInChildren<Renderer>())
                    if (r.shadowCastingMode != ShadowCastingMode.Off) r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        void DrawBlobs()
        {
            if (!blobShadow || monsters.Count == 0) return;
            if (!quad) quad = BuildBlobQuad();
            blobs.Clear();
            foreach (var m in monsters)
            {
                if (!m) continue;
                var p = m.position; p.y = transform.position.y + 0.03f;
                blobs.Add(Matrix4x4.TRS(p, Quaternion.identity, new Vector3(blobSize, 1, blobSize)));
                // stretched shadow away from the nearest lit lantern within its warm radius
                Light near = null; float nd = warmRadius * warmRadius;
                foreach (var l in lanterns) { if (!l || !l.isActiveAndEnabled) continue; float d = (l.transform.position - p).sqrMagnitude; if (d < nd) { nd = d; near = l; } }
                if (near)
                {
                    var away = p - near.transform.position; away.y = 0;
                    if (away.sqrMagnitude > 0.01f && blobStretch)
                    {
                        float len = Mathf.Lerp(3.5f, 1.2f, Mathf.Sqrt(nd) / warmRadius);
                        var dir = away.normalized;
                        // the quad is centred: shift it half its length away from the lantern so v = 0 sits at the feet
                        stretched.Add(Matrix4x4.TRS(p + Vector3.up * 0.005f + dir * len * 0.5f, Quaternion.LookRotation(dir), new Vector3(blobSize * 0.8f, 1, len)));
                    }
                }
            }
            var bounds = new Bounds(transform.position, new Vector3(arena.x + 40, 20, arena.y + 40));
            Draw(blobShadow, blobs, bounds);
            if (blobStretch) Draw(blobStretch, stretched, bounds);
            stretched.Clear();
        }

        readonly List<Matrix4x4> stretched = new List<Matrix4x4>();
        void Draw(Material m, List<Matrix4x4> list, Bounds bounds)
        {
            if (list.Count == 0) return;
            var rp = new RenderParams(m) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = bounds };
            for (int i = 0; i < list.Count; i += 1023)
                Graphics.RenderMeshInstanced(rp, quad, 0, list, Mathf.Min(1023, list.Count - i), i);
        }

        // flat quad on XZ, UV (0.5, 0) at the origin (the feet), v grows along +Z
        static Mesh BuildBlobQuad()
        {
            var m = new Mesh { name = "SP_BlobQuad", hideFlags = HideFlags.DontSave };
            m.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ passes
        void Inject(ScriptableRenderContext ctx, Camera cam)
        {
            if (!isActiveAndEnabled) return;
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            if (heightFog)
            {
                fogPass.mat = heightFog;
                fogPass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color);
                data.scriptableRenderer.EnqueuePass(fogPass);
            }
            if (silhouette && playerRenderers.Length > 0 && cam.cameraType == CameraType.Game)
            {
                silPass.mat = silhouette; silPass.renderers = playerRenderers;
                data.scriptableRenderer.EnqueuePass(silPass);
            }
        }

        class FogPass : ScriptableRenderPass
        {
            public Material mat;
            public FogPass() { requiresIntermediateTexture = true; }
            class PassData { public TextureHandle src, depth; public Material mat; }

            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>();
                if (res.isActiveTargetBackBuffer || mat == null) return;
                var src = res.activeColorTexture;
                var desc = rg.GetTextureDesc(src);
                desc.name = "SP_HeightFog"; desc.clearBuffer = false; desc.depthBufferBits = 0;
                var dst = rg.CreateTexture(desc);
                using (var b = rg.AddRasterRenderPass<PassData>("SP Height Fog", out var d))
                {
                    d.src = src; d.mat = mat; d.depth = res.cameraDepthTexture;
                    b.UseTexture(src, AccessFlags.Read);
                    if (d.depth.IsValid()) b.UseTexture(d.depth, AccessFlags.Read);
                    b.SetRenderAttachment(dst, 0, AccessFlags.Write);
                    b.AllowGlobalStateModification(true);
                    b.SetRenderFunc((PassData p, RasterGraphContext c) =>
                    {
                        if (p.depth.IsValid()) p.mat.SetTexture("_CameraDepthTexture", p.depth);
                        Blitter.BlitTexture(c.cmd, p.src, new Vector4(1, 1, 0, 0), p.mat, 0);
                    });
                }
                res.cameraColor = dst;
            }
        }

        class SilhouettePass : ScriptableRenderPass
        {
            public Material mat;
            public Renderer[] renderers;
            class PassData { public Material mat; public Renderer[] rs; public TextureHandle depth; }

            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>();
                if (mat == null || renderers == null) return;
                using (var b = rg.AddRasterRenderPass<PassData>("SP Occluded Player", out var d))
                {
                    d.mat = mat; d.rs = renderers; d.depth = res.cameraDepthTexture;
                    if (d.depth.IsValid()) b.UseTexture(d.depth, AccessFlags.Read);
                    b.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.Write);
                    b.SetRenderAttachmentDepth(res.activeDepthTexture, AccessFlags.Read);
                    b.AllowPassCulling(false);
                    b.SetRenderFunc((PassData p, RasterGraphContext c) =>
                    {
                        if (p.depth.IsValid()) p.mat.SetTexture("_CameraDepthTexture", p.depth);
                        foreach (var r in p.rs)
                        {
                            if (!r || !r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                            int subs = r.sharedMaterials.Length;
                            for (int s = 0; s < subs; s++) c.cmd.DrawRenderer(r, p.mat, s, 0);
                        }
                    });
                }
            }
        }
    }
}
