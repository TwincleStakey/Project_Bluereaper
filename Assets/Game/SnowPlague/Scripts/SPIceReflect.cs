// Frozen river reflections (SnowPlague/Ice, SPIceSurface.hlsl).
//   Glows: every frame up to 64 live bullets that are over the river (nearest to what the camera looks at) go to the
//   ice as global arrays (_SPIce_Glow / _SPIce_GlowCol / _SPIce_GlowCount); the ice draws each one's mirrored streak.
//   RoomRun is not referenced by SnowPlague.Runtime, so the bullets are read through reflection, bound once into a
//   typed delegate (RunDirector.Instance.Bullets.GetLive). In edit mode the debugGlows list stands in for them.
//   SSR: a pass after transparents copies colour + linear eye depth to a half-res history per camera; a tiny pass
//   before opaques hands last frame's copy and view-projection to the ice, which marches its reflected rays in it.
//   One frame late, which is not visible at 60 fps. The project's URP asset and renderer are not touched.
// Dev toggles: SPIceReflect.SSROn / GlowOn / StreaksOn (static, A/B the cost).
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SnowPlague
{
    [ExecuteAlways]
    public class SPIceReflect : MonoBehaviour
    {
        public static bool SSROn = false, GlowOn = true, StreaksOn = true;   // SSR off by default: blocky smears in the r5 captures, needs a fix pass
        public const int MaxGlows = 64;

        [Header("SSR history")]
        public Shader copyShader;   // Hidden/SnowPlague/IceCopy (set by SPBuilder so builds include it)
        [Range(0.25f, 1f)] public float historyScale = 1f;   // full res: the half-res copy showed blocks

        [Header("Bullet glows")]
        [ColorUsage(false, true)] public Color playerGlow = new Color(0.3f, 0.7f, 1.6f) * 1.4f;
        [ColorUsage(false, true)] public Color enemyGlow = new Color(1.6f, 0.38f, 0.07f) * 1.4f;
        public float playerSize = 0.3f, enemySize = 0.42f;
        [Tooltip("Bullets farther than this from the camera focus are skipped (m).")]
        public float focusRange = 34f;

        [Header("River (SPBuilder.Ice)")]
        public Vector2[] centre = new Vector2[0];
        public float[] halfWidth = new float[0];
        public float margin = 2.5f;

        [Serializable]
        public struct DebugGlow
        {
            public Vector3 pos;
            [ColorUsage(false, true)] public Color color;
            public float size;
        }
        [Header("Edit-mode test glows (also in Play when useDebugInPlay)")]
        public List<DebugGlow> debugGlows = new List<DebugGlow>();
        public bool useDebugInPlay;

        static readonly int ID_Glow = Shader.PropertyToID("_SPIce_Glow"), ID_GlowCol = Shader.PropertyToID("_SPIce_GlowCol"),
            ID_Count = Shader.PropertyToID("_SPIce_GlowCount"), ID_Params = Shader.PropertyToID("_SPIce_Params"),
            ID_Prev = Shader.PropertyToID("_SPIce_Prev"), ID_PrevVP = Shader.PropertyToID("_SPIce_PrevVP"),
            ID_PrevCam = Shader.PropertyToID("_SPIce_PrevCam"), ID_PrevFwd = Shader.PropertyToID("_SPIce_PrevFwd"),
            ID_PrevTexel = Shader.PropertyToID("_SPIce_PrevTexel");

        readonly Vector4[] glowPos = new Vector4[MaxGlows], glowCol = new Vector4[MaxGlows];
        readonly float[] glowDist = new float[MaxGlows];
        int glowN;

        // ------------------------------------------------------------------ river mask (1 m grid)
        bool[] mask; Vector2 maskMin; int mw, mh;

        void BuildMask()
        {
            mask = null;
            if (centre == null || centre.Length < 2) return;
            Vector2 mn = centre[0], mx = centre[0];
            float wmax = 0;
            for (int i = 0; i < centre.Length; i++) { mn = Vector2.Min(mn, centre[i]); mx = Vector2.Max(mx, centre[i]); wmax = Mathf.Max(wmax, Hw(i)); }
            float pad = wmax + margin + 1f;
            maskMin = mn - Vector2.one * pad;
            mw = Mathf.CeilToInt(mx.x - mn.x + 2 * pad) + 1; mh = Mathf.CeilToInt(mx.y - mn.y + 2 * pad) + 1;
            mask = new bool[mw * mh];
            for (int s = 0; s + 1 < centre.Length; s++)
            {
                var a = centre[s]; var b = centre[s + 1];
                float r = Mathf.Max(Hw(s), Hw(s + 1)) + margin;
                int i0 = Mathf.Max(0, (int)(Mathf.Min(a.x, b.x) - r - maskMin.x)), i1 = Mathf.Min(mw - 1, (int)(Mathf.Max(a.x, b.x) + r - maskMin.x) + 1);
                int j0 = Mathf.Max(0, (int)(Mathf.Min(a.y, b.y) - r - maskMin.y)), j1 = Mathf.Min(mh - 1, (int)(Mathf.Max(a.y, b.y) + r - maskMin.y) + 1);
                var ab = b - a; float l2 = Mathf.Max(ab.sqrMagnitude, 1e-4f);
                for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    var p = new Vector2(maskMin.x + i + 0.5f, maskMin.y + j + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
                    if ((p - (a + ab * t)).magnitude < r + 0.75f) mask[j * mw + i] = true;
                }
            }
        }
        float Hw(int i) => halfWidth != null && i < halfWidth.Length ? halfWidth[i] : 6f;

        public bool OverRiver(Vector3 p)
        {
            if (mask == null) return true;
            int i = (int)(p.x - maskMin.x), j = (int)(p.z - maskMin.y);
            return i >= 0 && j >= 0 && i < mw && j < mh && mask[j * mw + i];
        }

        // ------------------------------------------------------------------ RoomRun bullets via reflection
        PropertyInfo instProp, bulletsProp, countProp;
        MethodInfo getLive;
        object boundBullets;
        Func<int, Vector4> readBullet;   // xyz position, w team (0 player, 1 enemy)
        bool rrSearched;
        readonly object[] invokeArgs = new object[4];

        delegate void GetLiveFn<T>(int i, out Vector3 pos, out Vector3 dir, out T team);
        static Func<int, Vector4> Bind<T>(object target, MethodInfo mi) where T : struct, IConvertible
        {
            var d = (GetLiveFn<T>)Delegate.CreateDelegate(typeof(GetLiveFn<T>), target, mi);
            return i => { d(i, out var p, out _, out var t); return new Vector4(p.x, p.y, p.z, UnsafeUtility.EnumToInt(t)); };
        }

        object FindBullets()
        {
            if (!rrSearched)
            {
                rrSearched = true;
                Type rd = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    if ((rd = asm.GetType("RoomRun.RunDirector")) != null) break;
                if (rd == null) return null;
                instProp = rd.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                bulletsProp = rd.GetProperty("Bullets", BindingFlags.Public | BindingFlags.Instance);
                var bs = bulletsProp?.PropertyType;
                if (bs == null) return null;
                countProp = bs.GetProperty("LiveCount");
                foreach (var m in bs.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    if (m.Name == "GetLive" && m.GetParameters().Length == 4) getLive = m;
            }
            if (instProp == null || bulletsProp == null || countProp == null || getLive == null) return null;
            var inst = instProp.GetValue(null) as UnityEngine.Object;
            if (!inst) return null;
            var bullets = bulletsProp.GetValue(inst) as UnityEngine.Object;
            if (!bullets) return null;
            if (!ReferenceEquals(bullets, boundBullets))
            {
                boundBullets = bullets; readBullet = null;
                try
                {
                    var team = getLive.GetParameters()[3].ParameterType.GetElementType();
                    var bind = typeof(SPIceReflect).GetMethod(nameof(Bind), BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(team);
                    readBullet = (Func<int, Vector4>)bind.Invoke(null, new object[] { bullets, getLive });
                }
                catch (Exception e) { Debug.LogWarning("SPIceReflect: typed bullet read unavailable, using slow path (" + e.Message + ")"); }
            }
            return bullets;
        }

        Vector4 ReadSlow(object bullets, int i)
        {
            invokeArgs[0] = i;
            getLive.Invoke(bullets, invokeArgs);
            var p = (Vector3)invokeArgs[1];
            return new Vector4(p.x, p.y, p.z, Convert.ToInt32(invokeArgs[3]));
        }

        void Offer(Vector3 p, Color c, float size, float d2)
        {
            int slot;
            if (glowN < MaxGlows) slot = glowN++;
            else
            {
                slot = 0;
                for (int k = 1; k < MaxGlows; k++) if (glowDist[k] > glowDist[slot]) slot = k;
                if (glowDist[slot] <= d2) return;
            }
            glowPos[slot] = new Vector4(p.x, p.y, p.z, size);
            glowCol[slot] = new Vector4(c.r, c.g, c.b, 1);
            glowDist[slot] = d2;
        }

        Vector3 Focus()
        {
            var cam = Camera.main;
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView && UnityEditor.SceneView.lastActiveSceneView.camera)
                cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
            if (!cam) return transform.position;
            var o = cam.transform.position; var f = cam.transform.forward;
            if (f.y < -0.05f) return o + f * ((transform.position.y - o.y) / f.y);
            return o + f * 20f;
        }

        void GatherGlows()
        {
            glowN = 0;
            var focus = Focus();
            float r2 = focusRange * focusRange;
            if (!Application.isPlaying || useDebugInPlay)
                foreach (var g in debugGlows)
                    Offer(g.pos, g.color, g.size > 0 ? g.size : 0.4f, (g.pos - focus).sqrMagnitude);
            if (Application.isPlaying)
            {
                var bullets = FindBullets();
                if (bullets != null)
                {
                    int n = (int)countProp.GetValue(bullets);
                    if (readBullet == null) n = Mathf.Min(n, 256);
                    for (int i = 0; i < n; i++)
                    {
                        var b = readBullet != null ? readBullet(i) : ReadSlow(bullets, i);
                        var p = new Vector3(b.x, b.y, b.z);
                        float dx = p.x - focus.x, dz = p.z - focus.z, d2 = dx * dx + dz * dz;
                        if (d2 > r2 || !OverRiver(p)) continue;
                        bool pl = b.w < 0.5f;
                        Offer(p, pl ? playerGlow : enemyGlow, pl ? playerSize : enemySize, d2);
                    }
                }
            }
            for (int k = glowN; k < MaxGlows; k++) { glowPos[k] = Vector4.zero; glowCol[k] = Vector4.zero; }
            Shader.SetGlobalVectorArray(ID_Glow, glowPos);
            Shader.SetGlobalVectorArray(ID_GlowCol, glowCol);
            Shader.SetGlobalFloat(ID_Count, GlowOn ? glowN : 0);
        }

        // ------------------------------------------------------------------ lifecycle
        Material copyMat;
        GlobalsPass globalsPass;
        CopyPass copyPass;
        class History { public RTHandle rt; public Matrix4x4 vp; public Vector3 pos, fwd; public bool valid; public int w, h; }
        readonly Dictionary<Camera, History> history = new Dictionary<Camera, History>();

        void OnEnable()
        {
            BuildMask();
            if (!copyShader) copyShader = Shader.Find("Hidden/SnowPlague/IceCopy");
            if (copyShader) copyMat = CoreUtils.CreateEngineMaterial(copyShader);
            globalsPass = new GlobalsPass { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques };
            copyPass = new CopyPass { renderPassEvent = RenderPassEvent.AfterRenderingTransparents };
            RenderPipelineManager.beginCameraRendering += Inject;
            rrSearched = false; boundBullets = null; readBullet = null;
            GatherGlows();
        }

        void OnValidate() { BuildMask(); }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Inject;
            foreach (var h in history.Values) h.rt?.Release();
            history.Clear();
            CoreUtils.Destroy(copyMat); copyMat = null;
            Shader.SetGlobalVector(ID_Params, Vector4.zero);
            Shader.SetGlobalFloat(ID_Count, 0f);
        }

        void Update() { if (!Application.isPlaying) GatherGlows(); }
        void LateUpdate() { if (Application.isPlaying) GatherGlows(); }

        void Inject(ScriptableRenderContext ctx, Camera cam)
        {
            if (!isActiveAndEnabled) return;
            if (!Application.isPlaying) GatherGlows();   // edit mode: Update may not have run yet (captures right after a build)
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;

            bool ssr = SSROn && copyMat;
            var prm = new Vector4(0, GlowOn ? 1 : 0, StreaksOn ? 1 : 0, 0);
            History h = null;
            if (ssr)
            {
                int w = Mathf.Max(16, (int)(cam.pixelWidth * historyScale)), hh = Mathf.Max(16, (int)(cam.pixelHeight * historyScale));
                if (!history.TryGetValue(cam, out h)) history[cam] = h = new History();
                if (h.rt == null || h.w != w || h.h != hh)
                {
                    h.rt?.Release();
                    h.rt = RTHandles.Alloc(w, hh, colorFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat,
                        filterMode: FilterMode.Bilinear, wrapMode: TextureWrapMode.Clamp, name: "SP_IceHistory");
                    h.w = w; h.h = hh; h.valid = false;
                }
                if (h.valid) prm.x = 1;
            }
            // last frame's history for this camera -> ice (before opaques), always enqueued so the params are per camera
            globalsPass.prm = prm;
            globalsPass.tex = h != null && h.valid ? h.rt : null;
            if (h != null && h.valid)
            {
                globalsPass.vp = h.vp; globalsPass.camPos = h.pos; globalsPass.camFwd = h.fwd;
                globalsPass.texel = new Vector4(1f / h.w, 1f / h.h, h.w, h.h);
            }
            data.scriptableRenderer.EnqueuePass(globalsPass);

            if (ssr)
            {
                h.vp = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false) * cam.worldToCameraMatrix;
                h.pos = cam.transform.position; h.fwd = cam.transform.forward;
                copyPass.mat = copyMat; copyPass.dst = h.rt; copyPass.history = h;
                copyPass.ConfigureInput(ScriptableRenderPassInput.Depth);
                data.scriptableRenderer.EnqueuePass(copyPass);
            }
            else
            {
                if (h != null) h.valid = false;
                foreach (var v in history.Values) v.valid = false;
            }
        }

        class GlobalsPass : ScriptableRenderPass
        {
            public RTHandle tex; public Matrix4x4 vp; public Vector3 camPos, camFwd; public Vector4 texel, prm;
            class PassData { public TextureHandle tex; public bool hasTex; public Matrix4x4 vp; public Vector4 pos, fwd, texel, prm; }

            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
            {
                using (var b = rg.AddUnsafePass<PassData>("SP Ice Globals", out var d))
                {
                    d.hasTex = tex != null && tex.rt != null;
                    if (d.hasTex) { d.tex = rg.ImportTexture(tex); b.UseTexture(d.tex, AccessFlags.Read); }
                    d.vp = vp; d.pos = camPos; d.fwd = camFwd; d.texel = texel; d.prm = prm;
                    b.AllowPassCulling(false);
                    b.AllowGlobalStateModification(true);
                    b.SetRenderFunc((PassData p, UnsafeGraphContext c) =>
                    {
                        var cmd = c.cmd;
                        if (p.hasTex) cmd.SetGlobalTexture(ID_Prev, p.tex);
                        else cmd.SetGlobalTexture(ID_Prev, Texture2D.blackTexture);
                        cmd.SetGlobalMatrix(ID_PrevVP, p.vp);
                        cmd.SetGlobalVector(ID_PrevCam, p.pos);
                        cmd.SetGlobalVector(ID_PrevFwd, p.fwd);
                        cmd.SetGlobalVector(ID_PrevTexel, p.texel);
                        cmd.SetGlobalVector(ID_Params, p.hasTex ? p.prm : new Vector4(0, p.prm.y, p.prm.z, 0));
                    });
                }
            }
        }

        class CopyPass : ScriptableRenderPass
        {
            public Material mat; public RTHandle dst; public History history;
            class PassData { public TextureHandle src, depth; public Material mat; }

            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>();
                if (mat == null || dst == null || res.isActiveTargetBackBuffer) return;
                var src = res.activeColorTexture;
                var depth = res.cameraDepthTexture;
                if (!src.IsValid() || !depth.IsValid()) return;
                var dstH = rg.ImportTexture(dst);
                using (var b = rg.AddRasterRenderPass<PassData>("SP Ice History", out var d))
                {
                    d.src = src; d.depth = depth; d.mat = mat;
                    b.UseTexture(src, AccessFlags.Read);
                    b.UseTexture(depth, AccessFlags.Read);
                    b.SetRenderAttachment(dstH, 0, AccessFlags.Write);
                    b.AllowPassCulling(false);
                    b.AllowGlobalStateModification(true);
                    b.SetRenderFunc((PassData p, RasterGraphContext c) =>
                    {
                        p.mat.SetTexture("_CameraDepthTexture", p.depth);
                        Blitter.BlitTexture(c.cmd, p.src, new Vector4(1, 1, 0, 0), p.mat, 0);
                    });
                }
                history.valid = true;
            }
        }
    }
}
