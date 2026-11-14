// Moonlight shafts over the reed field (11-13, Kido: "dark fantasy where the glow carries the scene, moonlight beams
// from the sky"). A handful of beams fall through broken clouds along the moon direction, drift slowly with the wind and
// breathe as clouds pass. Each beam is one additive billboard quad (ReedField/LightShaft); the reeds and the ground
// inside a beam light up through the same globals (RFShaftLight in ReedCommon.hlsl), so a beam reads as a pool of moon
// on the reed tops from the top-down camera. No realtime lights, 8 tiny draws. Added by ReedField in Play.
using UnityEngine;

namespace Reeds
{
    public class RFMoonShafts : MonoBehaviour
    {
        public int count = 8;
        public Vector2 radius = new Vector2(3.6f, 6.0f);
        public float length = 36f;
        [ColorUsage(false, true)] public Color color = new Color(0.75f, 0.82f, 1.0f);
        [Range(0, 2)] public float beamIntensity = 0.3f;
        [Range(0, 3)] public float poolIntensity = 1.3f;
        public float drift = 0.22f;

        ReedField field;
        Transform[] beams;
        MeshRenderer[] rends;
        MaterialPropertyBlock mpb;
        Vector2[] pos; float[] rad, phase;
        readonly Vector4[] shafts = new Vector4[8];
        readonly Vector4[] fades = new Vector4[2];
        static readonly int ID_Shafts = Shader.PropertyToID("_RF_Shafts"), ID_Fade = Shader.PropertyToID("_RF_ShaftFadeArr"),
            ID_Dir = Shader.PropertyToID("_RF_ShaftDir"), ID_Col = Shader.PropertyToID("_RF_ShaftCol"), ID_F = Shader.PropertyToID("_ShaftFade");

        void Start()
        {
            field = GetComponent<ReedField>();
            // 11-14 (Kido: "하얀 빛대신 석양이 지는 느낌으로 좀 붉은 느낌"): in the sunset mood the beams are low red-orange
            // sun rays through the clouds, not cold white moonlight
            if (field && field.mood == ReedField.Mood.Sunset) { color = new Color(1.0f, 0.4f, 0.2f); beamIntensity = 0.24f; poolIntensity = 1.1f; }
            var mat = Resources.Load<Material>("RF_LightShaft");
            count = Mathf.Clamp(count, 0, 8);
            beams = new Transform[count]; rends = new MeshRenderer[count];
            pos = new Vector2[count]; rad = new float[count]; phase = new float[count];
            mpb = new MaterialPropertyBlock();
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var rnd = new System.Random(field ? field.seed + 77 : 77);
            Vector2 half = field ? field.arena * 0.5f : new Vector2(50, 40);
            for (int i = 0; i < count; i++)
            {
                // spread over the arena on a jittered ring of cells so two beams never stack
                float u = (i + (float)rnd.NextDouble() * 0.8f) / count;
                pos[i] = new Vector2(Mathf.Lerp(-half.x, half.x, u), Mathf.Lerp(-half.y, half.y, (float)rnd.NextDouble())) * 0.85f;
                rad[i] = Mathf.Lerp(radius.x, radius.y, (float)rnd.NextDouble());
                phase[i] = (float)rnd.NextDouble() * 100f;
                if (!mat) continue;
                var go = new GameObject("MoonShaft" + i);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                beams[i] = go.transform; rends[i] = r;
            }
            var sb = new System.Text.StringBuilder("[RFMoonShafts] beams at");
            for (int i = 0; i < count; i++) sb.Append($" ({transform.position.x + pos[i].x:0},{transform.position.z + pos[i].y:0} r{rad[i]:0.0})");
            Debug.Log(sb.ToString() + (mat ? "" : " (no RF_LightShaft material: pools only)"));
        }

        Light sun;
        Vector3 ToMoon()
        {
            // resolved once (no per-frame scene search): the field's moon, else the scene sun
            if (!sun) sun = field && field.moon && field.moon.isActiveAndEnabled ? field.moon : RenderSettings.sun;
            var d = sun ? -sun.transform.forward : new Vector3(0.3f, 0.8f, 0.5f);
            // keep the beams slanted but never flat: a low sun would lay them along the ground
            float minY = field && field.mood == ReedField.Mood.Sunset ? 0.42f : 0.55f;   // sunset: a lower, longer slant
            if (d.y < minY) { d.y = minY; }
            return d.normalized;
        }

        void Update()
        {
            if (beams == null) return;
            var L = ToMoon();
            float t = Time.time;
            Vector2 half = field ? field.arena * 0.5f : new Vector2(50, 40);
            Vector2 wind = field ? new Vector2(Mathf.Sin(field.windAngle * Mathf.Deg2Rad), Mathf.Cos(field.windAngle * Mathf.Deg2Rad)) : Vector2.right;
            var origin = transform.position;
            for (int i = 0; i < 8; i++) shafts[i] = Vector4.zero;
            for (int i = 0; i < beams.Length; i++)
            {
                // clouds carry the beams across the field; out one side, back in from the other
                pos[i] += wind * drift * Time.deltaTime;
                if (pos[i].x > half.x + 6) pos[i].x -= half.x * 2 + 12;
                if (pos[i].x < -half.x - 6) pos[i].x += half.x * 2 + 12;
                if (pos[i].y > half.y + 6) pos[i].y -= half.y * 2 + 12;
                if (pos[i].y < -half.y - 6) pos[i].y += half.y * 2 + 12;
                float g = field ? field.GroundHeight(pos[i]) : 0f;
                var p = origin + new Vector3(pos[i].x, g, pos[i].y);
                // breathing: slow cloud cover, never fully on for long
                float n = Mathf.PerlinNoise(phase[i], t * 0.05f);
                float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.75f, n));
                shafts[i] = new Vector4(p.x, p.y, p.z, rad[i]);
                fades[i >> 2][i & 3] = fade;
                if (!beams[i]) continue;
                beams[i].position = p;
                beams[i].rotation = Quaternion.identity;
                beams[i].localScale = new Vector3(rad[i] * 1.4f, length, 1f);   // 11-14 QA: narrower, reads as a beam not fog
                rends[i].GetPropertyBlock(mpb);
                mpb.SetFloat(ID_F, fade);
                mpb.SetColor("_Color", color);
                mpb.SetFloat("_Intensity", beamIntensity);
                rends[i].SetPropertyBlock(mpb);
                // generous bounds: the quad is moved in the vertex shader
                rends[i].bounds = new Bounds(p + L * length * 0.5f, new Vector3(length, length, length));
            }
            Shader.SetGlobalVectorArray(ID_Shafts, shafts);
            Shader.SetGlobalVectorArray(ID_Fade, fades);
            Shader.SetGlobalVector(ID_Dir, new Vector4(L.x, L.y, L.z, poolIntensity));
            Shader.SetGlobalVector(ID_Col, color);
        }

        void OnDisable()
        {
            // the bamboo arena and the snow field use the same shaders: no beams outside this field
            Shader.SetGlobalVector(ID_Dir, Vector4.zero);
        }
    }
}
