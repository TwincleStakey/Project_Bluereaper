// Bamboo arena: an empty square clearing walled in by very tall bamboo. Clumps are GPU-instanced (ReedInstancer),
// sway with the reed field's wind/bend code (ReedCommon.hlsl globals), lean slightly into the clearing and fade
// into haze further out, so no edge of the world shows from the RoomRun camera.
// No realtime bamboo shadows: the canopy shade is a baked top-down texture (dappled), fed to the ground shader.
// See-through: bamboo between the camera and the player dissolves in a screen-space circle (Bamboo.shader).
using System.Collections.Generic;
using Reeds;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BambooArena
{
    [ExecuteAlways, RequireComponent(typeof(ReedInstancer))]
    public class BambooField : MonoBehaviour
    {
        [Header("Layout")]
        public Vector2 arena = new Vector2(88, 72);   // the empty clearing (RoomRun room size)
        [Tooltip("Ragged edge: bamboo comes this far in or stays this far out of the clearing line.")]
        public float edgeNoise = 1.6f;
        public float wallDepth = 10f;                 // dense, detailed bamboo
        public float outerMargin = 30f;               // cheap far clumps out to here
        public float spacing = 1.3f;
        public float farSpacing = 3.0f;
        [Range(0.7f, 1.4f)] public float heightScale = 1.0f;   // clumps are modelled ~10 m tall
        [Range(0f, 15f)] public float leanIn = 6f;
        public int seed = 11;

        [Header("Wind (same globals as the reed field)")]
        [Range(0f, 2f)] public float windStrength = 0.45f;
        [Range(0f, 360f)] public float windAngle = 60f;
        [Range(0.1f, 3f)] public float gustSpeed = 0.7f;
        [Range(2f, 40f)] public float gustSize = 18f;
        [Range(0f, 1.5f)] public float gustStrength = 0.8f;

        [Header("Mood")]
        public Color lightColor = new Color(1.0f, 0.90f, 0.70f);
        public float lightIntensity = 1.35f;
        public Vector2 sun = new Vector2(52f, -35f);
        public Color ambient = new Color(0.20f, 0.25f, 0.21f);
        public Color fog = new Color(0.40f, 0.48f, 0.42f);
        [Range(0f, 1f)] public float groundFog = 0.18f;
        public float groundFogHeight = 2.2f;
        [Range(0f, 0.1f)] public float distanceFog = 0.045f;
        public float hazeStart = 34f;
        [Range(0f, 1f)] public float canopyShade = 0.6f;

        [Header("See-through")]
        public bool seeThrough = true;
        [Range(0.05f, 0.4f)] public float seeThroughRadius = 0.16f;
        [Tooltip("Optional stand-in (editor captures); in Play the object named Player is used.")]
        public Transform seeThroughTarget;

        [Header("Assets")]
        public Mesh[] clumps;
        public Mesh farClump;
        public Material bambooMat;
        [Tooltip("Low plants in the clearing (instanced, no colliders).")]
        public Mesh groundCover;
        public Material groundCoverMat;
        [Range(0.3f, 1.5f)] public float groundCoverScale = 1.1f;
        public Light sunLight;
        public Volume volume;

        ReedInstancer inst;
        Texture2D shade;
        bool dirty = true;
        float nextFind;
        readonly List<Vector3> placed = new List<Vector3>();

        static readonly int ID_See = Shader.PropertyToID("_BA_SeeThrough"), ID_SeeOn = Shader.PropertyToID("_BA_SeeThroughOn");

        void OnEnable() { inst = GetComponent<ReedInstancer>(); dirty = true; }
        void OnValidate() { dirty = true; }

        void Update()
        {
            if (dirty) { dirty = false; Generate(); ApplyMood(); }
            SetGlobals();
        }

        void LateUpdate()
        {
            UpdateSeeThrough();
            // RoomRun regrades and re-aims the sun for every room: keep this arena's own light.
            if (Application.isPlaying) ApplyLight();
        }

        // ------------------------------------------------------------------ layout
        public Vector2 FullSize => arena + Vector2.one * outerMargin * 2f;

        // Signed distance outside the clearing rectangle (negative inside), with a slow wobble so the wall is ragged.
        public float Outside(Vector2 p)
        {
            Vector2 h = arena * 0.5f;
            Vector2 d = new Vector2(Mathf.Abs(p.x) - h.x, Mathf.Abs(p.y) - h.y);
            float outside = new Vector2(Mathf.Max(d.x, 0), Mathf.Max(d.y, 0)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0);
            float wob = (Mathf.PerlinNoise(p.x * 0.12f + seed, p.y * 0.12f) - 0.5f) * 2f * edgeNoise;
            return outside + wob;
        }

        public void Generate()
        {
            if (!inst) inst = GetComponent<ReedInstancer>();
            inst.Clear(); placed.Clear();
            if (!bambooMat || clumps == null || clumps.Length == 0) return;
            var rnd = new System.Random(seed);
            float R() => (float)rnd.NextDouble();
            Vector2 full = FullSize;
            var origin = transform.position;

            // jittered grid: dense near the clearing, sparse and cheap further out
            void Fill(float step, bool far)
            {
                for (float x = -full.x * 0.5f; x < full.x * 0.5f; x += step)
                for (float z = -full.y * 0.5f; z < full.y * 0.5f; z += step)
                {
                    var p = new Vector2(x + (R() - 0.5f) * step * 0.9f, z + (R() - 0.5f) * step * 0.9f);
                    float o = Outside(p);
                    if (o < 0.3f) continue;
                    bool inner = o < wallDepth;
                    if (far == inner) continue;
                    // a few gaps so the wall is not a hedge
                    if (!far && Mathf.PerlinNoise(p.x * 0.3f + 40, p.y * 0.3f) < 0.12f && o > 4f) continue;
                    Vector2 inward = -new Vector2(Mathf.Clamp(p.x, -arena.x * 0.5f, arena.x * 0.5f) == p.x ? 0 : Mathf.Sign(p.x),
                                                   Mathf.Clamp(p.y, -arena.y * 0.5f, arena.y * 0.5f) == p.y ? 0 : Mathf.Sign(p.y));
                    if (inward == Vector2.zero) inward = -p.normalized;
                    inward.Normalize();
                    // lean into the clearing, strongest for the front row
                    float lean = leanIn * Mathf.Lerp(1f, 0.3f, Mathf.Clamp01(o / wallDepth)) * (0.6f + 0.8f * R());
                    var axis = Vector3.Cross(Vector3.up, new Vector3(inward.x, 0, inward.y));
                    var rot = Quaternion.AngleAxis(lean, axis) * Quaternion.Euler(0, R() * 360f, 0);
                    // shorter first row so the arena edge reads, then full height
                    float h = heightScale * Mathf.Lerp(0.8f, 1.15f, R()) * Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(o / 4f));
                    var mesh = far ? farClump : clumps[rnd.Next(clumps.Length)];
                    if (!mesh) continue;
                    var pos = origin + new Vector3(p.x, 0, p.y);
                    var m = Matrix4x4.TRS(pos, rot, Vector3.one * h);
                    float tall = mesh.bounds.max.y * h;
                    var wb = new Bounds(pos + Vector3.up * tall * 0.5f, new Vector3(tall * 0.6f + 4f, tall + 1f, tall * 0.6f + 4f));
                    inst.Add(mesh, bambooMat, m, wb);
                    placed.Add(new Vector3(p.x, p.y, far ? 0.6f : 1f));
                }
            }
            Fill(spacing, false);
            Fill(farSpacing, true);
            GroundCover(rnd);
            BakeShade();
        }

        // Low ground cover in the clearing (no colliders, knee height at most): patchy clusters, thicker toward the bamboo,
        // kept off the stone paving so the floor stays readable for combat.
        void GroundCover(System.Random rnd)
        {
            if (!groundCover || !groundCoverMat) return;
            float R() => (float)rnd.NextDouble();
            var origin = transform.position;
            float step = 0.85f;
            for (float x = -arena.x * 0.5f; x < arena.x * 0.5f; x += step)
            for (float z = -arena.y * 0.5f; z < arena.y * 0.5f; z += step)
            {
                var p = new Vector2(x + (R() - 0.5f) * step, z + (R() - 0.5f) * step);
                float o = Outside(p);
                if (o > 0.5f) continue;
                if (Trail(p) > 0.2f) continue;
                float cluster = Mathf.PerlinNoise(p.x * 0.16f + 31, p.y * 0.16f + seed);
                float edge = Mathf.InverseLerp(-6f, -0.5f, o);        // undergrowth mostly toward the bamboo
                if (cluster * 0.6f + edge * 0.7f < 0.62f + R() * 0.12f) continue;
                float s = groundCoverScale * Mathf.Lerp(0.6f, 1.05f, R()) * Mathf.Lerp(0.8f, 1.1f, edge);
                var pos = origin + new Vector3(p.x, 0, p.y);
                inst.Add(groundCover, groundCoverMat, Matrix4x4.TRS(pos, Quaternion.Euler(0, R() * 360f, 0), Vector3.one * s),
                         new Bounds(pos + Vector3.up * 0.4f, new Vector3(2f, 1.2f, 2f)));
            }
        }

        // Canopy shade on the ground: soft dark under the bamboo, a dappled fringe spilling into the clearing.
        public Vector4 ShadeRect { get { var f = FullSize; return new Vector4(transform.position.x - f.x * 0.5f, transform.position.z - f.y * 0.5f, f.x, f.y); } }

        public void BakeShade()
        {
            const int N = 512;
            if (!shade) shade = new Texture2D(N, N, TextureFormat.R8, false, true) { wrapMode = TextureWrapMode.Clamp, name = "BA_CanopyShade", hideFlags = HideFlags.DontSave };
            var full = FullSize;
            var px = new Color32[N * N];
            // light falls from the sun side: shift the shade away from the sun
            float yaw = sun.y * Mathf.Deg2Rad;
            Vector2 shift = -new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)) * 3.0f;
            for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                var p = new Vector2((i + 0.5f) / N * full.x - full.x * 0.5f, (j + 0.5f) / N * full.y - full.y * 0.5f);
                float o = Outside(p - shift);
                float under = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-3.5f, 2.5f, o));
                // dapple: light holes in the canopy
                float d = Mathf.PerlinNoise(p.x * 0.45f + 9, p.y * 0.45f + 3) * 0.65f + Mathf.PerlinNoise(p.x * 1.3f, p.y * 1.3f + 7) * 0.35f;
                float holes = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.72f, d));
                float lit = 1f - under * canopyShade * (1f - holes * 0.55f);
                px[j * N + i] = new Color32((byte)(Mathf.Clamp01(lit) * 255), 0, 0, 255);
            }
            shade.SetPixels32(px); shade.Apply(false);
        }

        // A narrow mountain-trail footpath (packed earth), winding through the clearing from the south bamboo to the
        // north bamboo, a little wider and narrower along the way. 0..1, flush with the ground (ground shader only).
        public float TrailX(float z) => Mathf.Sin(z * 0.075f + 0.6f) * 11f + (Mathf.PerlinNoise(z * 0.035f, 5) - 0.5f) * 8f;   // wider swings for the 88 x 72 clearing
        public float Trail(Vector2 p)
        {
            float half = 0.75f + 0.3f * Mathf.PerlinNoise(p.y * 0.2f, 17);
            float d = Mathf.Abs(p.x - TrailX(p.y));
            float t = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(half * 0.6f, half + 0.6f, d));
            return t * (1f - Mathf.Clamp01((Outside(p) - 2f) / 3f));   // runs on into the bamboo, then fades
        }

        public float UnderCanopy(Vector2 p) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1f, 3f, Outside(p)));

        // ------------------------------------------------------------------ globals, light, see-through
        public void SetGlobals()
        {
            float a = windAngle * Mathf.Deg2Rad;
            Shader.SetGlobalFloat("_RF_Time", Application.isPlaying ? Time.time : (float)(Time.realtimeSinceStartupAsDouble % 3600.0));
            Shader.SetGlobalVector("_RF_Wind", new Vector4(Mathf.Sin(a), Mathf.Cos(a), windStrength, gustSpeed));
            Shader.SetGlobalVector("_RF_Gust", new Vector4(gustSize, gustStrength, 0.4f, 0.4f));
            Shader.SetGlobalVectorArray("_RF_Stalkers", new Vector4[4]);
            Shader.SetGlobalVector("_RF_FogCol", new Vector4(fog.r, fog.g, fog.b, groundFogHeight));
            Shader.SetGlobalVector("_RF_FogParams", new Vector4(groundFog, distanceFog, 0.6f, hazeStart));
            Shader.SetGlobalVector("_RF_FogArena", new Vector4(transform.position.x, transform.position.z, arena.x * 0.5f, arena.y * 0.5f));
            Shader.SetGlobalFloat("_RF_ShadowOn", shade ? 1f : 0f);
            if (shade) Shader.SetGlobalTexture("_RF_ReedShadow", shade);
            Shader.SetGlobalTexture("_RF_PropShadow", Texture2D.whiteTexture);
            Shader.SetGlobalVector("_RF_ShadowRect", ShadeRect);
            Shader.SetGlobalFloat("_RF_WaterLevel", transform.position.y - 50f);   // no water here
        }

        void ApplyLight()
        {
            if (sunLight)
            {
                sunLight.transform.rotation = Quaternion.Euler(sun.x, sun.y, 0);
                sunLight.color = lightColor; sunLight.intensity = lightIntensity;
            }
            if (volume && volume.profile && volume.profile.TryGet(out ColorAdjustments ca))
            {
                ca.saturation.Override(-12f); ca.contrast.Override(14f); ca.postExposure.Override(0.1f); ca.colorFilter.Override(Color.white);
            }
        }

        public void ApplyMood()
        {
            ApplyLight();
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient * 1.4f;
            RenderSettings.fogColor = fog;
            if (bambooMat) bambooMat.SetColor("_Ambient", ambient);
            var cam = Camera.main;
            if (cam && cam.clearFlags == CameraClearFlags.SolidColor) cam.backgroundColor = fog;
        }

        public void LateUpdateNow() { if (Application.isPlaying) ApplyLight(); UpdateSeeThrough(); }

        public void UpdateSeeThrough()
        {
            var target = seeThroughTarget;
            if (!target && Application.isPlaying)
            {
                if (Time.unscaledTime >= nextFind) { nextFind = Time.unscaledTime + 1f; var go = GameObject.Find("Player"); if (go) seeThroughTarget = target = go.transform; }
            }
            var cam = Camera.main;
            if (!seeThrough || !target || !cam || !target.gameObject.activeInHierarchy) { Shader.SetGlobalFloat(ID_SeeOn, 0f); return; }
            var p = target.position + Vector3.up * 1.0f;
            var vp = cam.WorldToViewportPoint(p);
            if (vp.z <= 0) { Shader.SetGlobalFloat(ID_SeeOn, 0f); return; }
            Shader.SetGlobalVector(ID_See, new Vector4(vp.x, vp.y, seeThroughRadius, vp.z));
            Shader.SetGlobalFloat(ID_SeeOn, 1f);
        }

        void OnDisable() { Shader.SetGlobalFloat(ID_SeeOn, 0f); }
        void OnDestroy() { if (shade) DestroyImmediate(shade); }
    }
}
