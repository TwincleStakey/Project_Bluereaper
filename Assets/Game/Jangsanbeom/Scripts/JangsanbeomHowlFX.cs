// 장산범 howl FX (11-18 Kido: "하울할 때 입안에 좀 밝고 뭔가 퍼져나가는 광원 vfx"): while the jaw is open the inside of the mouth
// glows red-hot and lights the face (a point light), and rings of light roll out from the mouth along the line of the head,
// growing and fading like the sound itself. Driven by how far the Jaw bone is open, so it follows any howl/roar clip.
// Runs by itself in Play mode; capture tools call Sample(time) per frame.
using System.Collections.Generic;
using UnityEngine;

public class JangsanbeomHowlFX : MonoBehaviour
{
    public string headBone = "Head", jawBone = "Jaw";
    // 11-19 Kido ("이빨 쪽만 붉게, 소리는 축음기에서, 입이랑 그 가운데 어디서 동그란 이펙트 시작"): no glowing ball in the mouth any more -
    // the teeth glow red (JangsanbeomGlow _TeethMask, driven by the global _JSB_TeethGlow) - and the rings start halfway between the
    // mouth and the gramophone horn, rolling out along the horn's axis
    public string hornRim = "HornRim", hornBase = "Drum";
    [Range(0, 1)] public float ringOriginToHorn = 0.5f;
    public bool mouthBall = false;
    public Texture2D teethMask;                                  // UV mask of the teeth (v2_teethmask.py)
    [ColorUsage(false, true)] public Color teethColor = new Color(25f, 1f, 0.3f, 1f);
    public Material glowMaterial;                 // rings: additive HDR (JangsanbeomTrail shader, vertex colour fades it)
    public Material mouthMaterial;                // the hot inside of the mouth: an unlit HDR colour
    [Tooltip("Jaw opening (degrees past rest) where the FX starts / is at full strength")] public float openStart = 6f, openFull = 22f;
    public Color lightColor = new Color(1f, 0.06f, 0.02f);
    public float lightIntensity = 25f, lightRange = 5f;   // a small red light on the teeth only
    [Tooltip("Mouth glow ball radius (world units)")] public float mouthRadius = 0.9f;
    [Tooltip("Seconds between rings while the mouth is open")] public float ringEvery = 0.22f;
    public float ringLife = 1.1f, ringStartRadius = 0.8f, ringEndRadius = 11f, ringTravel = 9f, ringWidth = 0.45f;

    Transform head, jaw, rim, drum; Quaternion jawRest; Vector3 mouthLocal;
    Light lamp; Transform ball;
    class Ring { public LineRenderer lr; public float born; public Vector3 origin, dir; }
    readonly List<Ring> rings = new List<Ring>();
    float lastRing = -1e9f, lastT = -1;
    public float Strength { get; private set; }

    void Awake() { Build(); }

    public void Build()
    {
        foreach (Transform c in transform) if (c.name.StartsWith("HowlFX")) { if (Application.isPlaying) Destroy(c.gameObject); else DestroyImmediate(c.gameObject); }
        rings.Clear();
        var all = GetComponentsInChildren<Transform>(true);
        head = System.Array.Find(all, t => t.name == headBone); jaw = System.Array.Find(all, t => t.name == jawBone);
        rim = System.Array.Find(all, t => t.name == hornRim); drum = System.Array.Find(all, t => t.name == hornBase);
        if (head == null || jaw == null) return;
        jawRest = jaw.localRotation;
        // the mouth = between the jaw pivot and the jaw tip (the jaw's first child or a step along it), kept in the head's space
        var tip = jaw.childCount > 0 ? jaw.GetChild(0).position : jaw.position + (jaw.position - head.position).normalized * 0.6f;
        mouthLocal = head.InverseTransformPoint(Vector3.Lerp(jaw.position, tip, 0.45f));
        var root = new GameObject("HowlFX") { hideFlags = HideFlags.DontSave }; root.transform.SetParent(transform, false);
        lamp = new GameObject("HowlFX_Light") { hideFlags = HideFlags.DontSave }.AddComponent<Light>();
        lamp.transform.SetParent(root.transform, false);
        lamp.type = LightType.Point; lamp.color = lightColor; lamp.range = lightRange; lamp.shadows = LightShadows.None; lamp.intensity = 0;
        var b = GameObject.CreatePrimitive(PrimitiveType.Sphere); b.name = "HowlFX_Mouth"; b.hideFlags = HideFlags.DontSave;
        if (Application.isPlaying) Destroy(b.GetComponent<Collider>()); else DestroyImmediate(b.GetComponent<Collider>());
        b.transform.SetParent(root.transform, false);
        var mr = b.GetComponent<MeshRenderer>(); mr.sharedMaterial = mouthMaterial; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        ball = b.transform; ball.localScale = Vector3.zero;
    }

    // how open the mouth is, 0..1
    float Open()
    {
        if (jaw == null) return 0;
        float deg = Quaternion.Angle(jaw.localRotation, jawRest);
        return Mathf.Clamp01((deg - openStart) / Mathf.Max(0.01f, openFull - openStart));
    }

    void LateUpdate() { if (Application.isPlaying) Sample(Time.time); }

    public void Sample(float time)
    {
        if (head == null) return;
        if (lastT >= 0 && time < lastT) ResetFX();
        lastT = time;
        float s = Open(); s = s * s * (3 - 2 * s);
        // a slow throb on top, like the parasite's heartbeat
        float throb = 0.85f + 0.15f * Mathf.Sin(time * 9f);
        Strength = s;
        var mouth = head.TransformPoint(mouthLocal);
        var fwd = (mouth - head.position); fwd.y *= 0.5f; fwd.Normalize();
        lamp.transform.position = mouth + fwd * 0.4f;   // at the teeth
        lamp.intensity = lightIntensity * s * throb;
        Shader.SetGlobalFloat("_JSB_TeethGlow", teethMask != null ? s * throb : 0f);
        if (teethMask != null) Shader.SetGlobalTexture("_JSB_TeethMask", teethMask);
        Shader.SetGlobalVector("_JSB_TeethColor", teethColor);
        ball.position = mouth; ball.localScale = Vector3.one * mouthRadius * 2f * Mathf.Lerp(0.4f, 1f, s) * (s > 0.01f && mouthBall ? 1 : 0);
        var mpb = new MaterialPropertyBlock(); mpb.SetColor("_BaseColor", new Color(9f, 1.6f, 0.5f, 1f) * (s * throb)); ball.GetComponent<MeshRenderer>().SetPropertyBlock(mpb);

        if (s > 0.35f && time - lastRing >= ringEvery)
        {
            var o = mouth; var dr = fwd;
            if (rim != null)
            {   // the sound comes out of the gramophone: start between the mouth and the horn, travel along the horn
                var rc = BoundsCentre(rim);
                o = Vector3.Lerp(mouth, rc, ringOriginToHorn);
                if (drum != null) { var ax = rc - BoundsCentre(drum); if (ax.sqrMagnitude > 1e-4f) dr = ax.normalized; }
            }
            SpawnRing(time, o, dr); lastRing = time;
        }
        for (int i = rings.Count - 1; i >= 0; i--)
        {
            var r = rings[i]; float age = (time - r.born) / ringLife;
            if (age > 1f || age < 0f) { Kill(r); rings.RemoveAt(i); continue; }
            float e = 1f - (1f - age) * (1f - age);                          // fast out, slowing down
            float rad = Mathf.Lerp(ringStartRadius, ringEndRadius, e);
            var c = r.origin + r.dir * ringTravel * e;
            var u = Vector3.Cross(r.dir, Vector3.up); if (u.sqrMagnitude < 1e-4f) u = Vector3.right; u.Normalize();
            var v = Vector3.Cross(u, r.dir).normalized;
            int n = r.lr.positionCount;
            for (int k = 0; k < n; k++) { float a = k / (float)n * Mathf.PI * 2f; r.lr.SetPosition(k, c + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * rad); }
            float fade = (1f - age) * (1f - age) * Mathf.Clamp01(age * 8f);
            var col = new Color(1, 1, 1, fade); r.lr.startColor = col; r.lr.endColor = col;
            r.lr.widthMultiplier = ringWidth * Mathf.Lerp(1.4f, 0.5f, age);
        }
    }

    void SpawnRing(float time, Vector3 origin, Vector3 dir)
    {
        var go = new GameObject("HowlFX_Ring") { hideFlags = HideFlags.DontSave }; go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true; lr.loop = true; lr.positionCount = 48; lr.sharedMaterial = glowMaterial; lr.alignment = LineAlignment.View;
        lr.numCornerVertices = 2; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
        rings.Add(new Ring { lr = lr, born = time, origin = origin, dir = dir });
    }

    static Vector3 BoundsCentre(Transform t) { var r = t.GetComponent<Renderer>(); return r != null ? r.bounds.center : t.position; }

    void Kill(Ring r) { if (r.lr) { if (Application.isPlaying) Destroy(r.lr.gameObject); else DestroyImmediate(r.lr.gameObject); } }

    public void ResetFX()
    {
        foreach (var r in rings) Kill(r);
        rings.Clear(); lastRing = -1e9f; lastT = -1;
        if (lamp) lamp.intensity = 0; if (ball) ball.localScale = Vector3.zero; Shader.SetGlobalFloat("_JSB_TeethGlow", 0f);
    }
}
