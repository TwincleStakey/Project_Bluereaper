// 장산범 ground scrape FX: while a front claw drags along the ground it kicks up dust, throws dirt clods back along the drag,
// spits red sparks and leaves claw gouges that fade. Contact = claw tip near the ground and moving fast sideways.
// Runs by itself in Play mode; capture tools call Sample(time) per frame (it also steps the particles in edit mode).
using System.Collections.Generic;
using UnityEngine;

public class JangsanbeomScrapeFX : MonoBehaviour
{
    public string[] toeBones = { "FrontToes.R", "FrontToes.L" };
    public Material dustMaterial, sparkMaterial, gougeMaterial;
    [Tooltip("Claw tip height above the boss root where it counts as touching the ground (world units)")] public float contactHeight = 1.4f;
    [Tooltip("Horizontal claw speed (world units / s) where the scrape starts")] public float minSpeed = 8f;
    [Tooltip("Horizontal claw speed for the full burst")] public float fullSpeed = 30f;
    public float gougeLife = 2.5f, gougeWidth = 0.9f, clawSpacing = 1.3f;

    class Claw { public Transform toe, paw; public Vector3 last; public float lastT = -1; public Gouge cur; }
    class Gouge { public LineRenderer[] lines; public readonly List<Vector3> pts = new List<Vector3>(); public Vector3 side; public float end; }
    readonly List<Claw> claws = new List<Claw>();
    readonly List<Gouge> gouges = new List<Gouge>();
    ParticleSystem dust, clods, sparks;
    [System.NonSerialized] public string debugStats = ""; [System.NonSerialized] public int contacts; float minH = 1e9f, maxV;
    float lastSim = -1;

    void Awake() { Build(); }

    public void Build()
    {
        foreach (Transform c in transform) if (c.name.StartsWith("ScrapeFX")) { if (Application.isPlaying) Destroy(c.gameObject); else DestroyImmediate(c.gameObject); }
        claws.Clear(); gouges.Clear();
        var all = GetComponentsInChildren<Transform>(true);
        foreach (var n in toeBones)
        {
            var toe = System.Array.Find(all, t => t.name == n); if (toe == null) continue;
            claws.Add(new Claw { toe = toe, paw = toe.parent });
        }
        var root = new GameObject("ScrapeFX") { hideFlags = HideFlags.DontSave }; root.transform.SetParent(transform, false);
        dust = MakePS(root.transform, "ScrapeFX_Dust", dustMaterial, false, 1.2f, 2.2f, -0.05f);
        clods = MakePS(root.transform, "ScrapeFX_Clods", dustMaterial, false, 0.9f, 0.5f, 2.2f);
        sparks = MakePS(root.transform, "ScrapeFX_Sparks", sparkMaterial, true, 0.35f, 0.18f, 1.2f);
    }

    static ParticleSystem MakePS(Transform parent, string name, Material mat, bool stretch, float life, float size, float gravity)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave }; go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = false; m.playOnAwake = false; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 2000;
        m.startLifetime = life; m.startSize = size; m.gravityModifier = gravity; m.startSpeed = 0;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.08f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, stretch ? AnimationCurve.Linear(0, 1, 1, 0.2f) : AnimationCurve.Linear(0, 0.6f, 1, 1.6f));
        if (!stretch) { var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f); }
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        if (stretch) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.2f; r.lengthScale = 1f; }
        return ps;
    }

    Vector3 Tip(Claw c) => c.paw ? c.toe.position + (c.toe.position - c.paw.position) * 0.8f : c.toe.position;

    void LateUpdate() { if (Application.isPlaying) Sample(Time.time); }

    /// <summary>Check the claws at this time, emit, and (edit mode) step the particles to this time.</summary>
    public void Sample(float time)
    {
        float groundY = transform.position.y;
        foreach (var c in claws)
        {
            var p = Tip(c);
            if (c.lastT >= 0 && time > c.lastT)
            {
                float dt = time - c.lastT;
                var v = (p - c.last) / dt; var vh = new Vector3(v.x, 0, v.z);
                float h = p.y - groundY;
                minH = Mathf.Min(minH, h); if (h < contactHeight * 2f) maxV = Mathf.Max(maxV, vh.magnitude);
                float k = h < contactHeight ? Mathf.Clamp01((vh.magnitude - minSpeed) / Mathf.Max(fullSpeed - minSpeed, 0.01f)) : 0f;
                if (k > 0)
                {
                    contacts++;
                    var at = new Vector3(p.x, Mathf.Max(groundY, p.y) + 0.05f, p.z); var dir = vh.normalized;
                    Emit(at, dir, k, dt);
                    if (c.cur == null || time - c.cur.end > 0.15f) { c.cur = NewGouge(Vector3.Cross(Vector3.up, dir).normalized); }
                    c.cur.pts.Add(at); c.cur.end = time;
                }
            }
            c.last = p; c.lastT = time;
        }
        debugStats = $"claws {claws.Count} contacts {contacts} minTipH {minH:F2} maxSpeedNearGround {maxV:F1} dust {(dust ? dust.particleCount : -1)} gouges {gouges.Count}";
        // gouges: fade with age
        for (int i = gouges.Count - 1; i >= 0; i--)
        {
            var gg = gouges[i]; float age = time - gg.end;
            if (age > gougeLife || age < -0.5f) { foreach (var l in gg.lines) if (l) { if (Application.isPlaying) Destroy(l.gameObject); else DestroyImmediate(l.gameObject); } gouges.RemoveAt(i); continue; }
            float a = 1f - Mathf.Clamp01(age / gougeLife); a *= a;
            for (int j = 0; j < gg.lines.Length; j++)
            {
                var l = gg.lines[j]; float off = (j - 1) * clawSpacing;
                l.positionCount = gg.pts.Count;
                for (int q = 0; q < gg.pts.Count; q++) l.SetPosition(q, gg.pts[q] + gg.side * off + Vector3.up * 0.02f * j);
                var col = new Color(1, 1, 1, a); l.startColor = col; l.endColor = col;
            }
        }
        if (!Application.isPlaying)
        {
            float step = lastSim < 0 || time < lastSim ? 0f : time - lastSim;
            if (step > 0) foreach (var ps in new[] { dust, clods, sparks }) if (ps) ps.Simulate(step, true, false, false);
            lastSim = time;
        }
    }

    Gouge NewGouge(Vector3 side)
    {
        var gg = new Gouge { side = side, lines = new LineRenderer[3] };
        for (int j = 0; j < 3; j++)
        {
            var go = new GameObject("ScrapeFX_Gouge") { hideFlags = HideFlags.DontSave }; go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(-90, 0, 0);   // TransformZ alignment: lie flat, front face up
            var l = go.AddComponent<LineRenderer>(); l.useWorldSpace = true; l.alignment = LineAlignment.TransformZ; l.sharedMaterial = gougeMaterial;
            l.widthMultiplier = gougeWidth; l.widthCurve = new AnimationCurve(new Keyframe(0, 0.2f), new Keyframe(0.3f, 1f), new Keyframe(1, 0.6f));
            l.numCapVertices = 2; l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; l.receiveShadows = false; l.positionCount = 0;
            gg.lines[j] = l;
        }
        gouges.Add(gg); return gg;
    }

    void Emit(Vector3 at, Vector3 dir, float k, float dt)
    {
        var side = Vector3.Cross(Vector3.up, dir);
        int nd = Mathf.CeilToInt(6f * k), nc = Mathf.CeilToInt(6f * k), ns = Mathf.CeilToInt(10f * k);
        var e = new ParticleSystem.EmitParams();
        for (int i = 0; i < nd; i++)   // dust: low brown puffs that spread and rise a little
        {
            e.position = at + side * Random.Range(-1.2f, 1.2f) + dir * Random.Range(-0.5f, 0.5f);
            e.velocity = -dir * Random.Range(1f, 4f) * k + Vector3.up * Random.Range(0.6f, 2.2f) + side * Random.Range(-1.5f, 1.5f);
            e.startSize = Random.Range(3.5f, 6f); e.startLifetime = Random.Range(1.0f, 1.8f);
            e.startColor = new Color(0.56f, 0.38f, 0.26f, 0.7f); e.rotation = Random.Range(0, 360);
            dust.Emit(e, 1);
        }
        for (int i = 0; i < nc; i++)   // clods: thrown back along the drag
        {
            e.position = at + side * Random.Range(-0.8f, 0.8f);
            e.velocity = (dir * Random.Range(4f, 9f) + Vector3.up * Random.Range(4f, 8f) + side * Random.Range(-2f, 2f)) * (0.6f + 0.4f * k);
            e.startSize = Random.Range(0.8f, 1.6f); e.startLifetime = Random.Range(0.7f, 1.1f);
            e.startColor = new Color(0.16f, 0.10f, 0.07f, 1f); e.rotation = Random.Range(0, 360);
            clods.Emit(e, 1);
        }
        for (int i = 0; i < ns; i++)   // red sparks off the claws
        {
            e.position = at + side * Random.Range(-1f, 1f) + Vector3.up * 0.2f;
            e.velocity = (dir * Random.Range(6f, 14f) + Vector3.up * Random.Range(3f, 9f) + side * Random.Range(-4f, 4f));
            e.startSize = Random.Range(0.3f, 0.55f); e.startLifetime = Random.Range(0.3f, 0.6f);
            e.startColor = new Color(1f, 0.55f, 0.3f, 1f);   // the material tint makes it HDR red e.rotation = 0;
            sparks.Emit(e, 1);
        }
    }

    /// <summary>Clear particles, gouges and history (e.g. before a capture).</summary>
    public void ResetFX()
    {
        foreach (var ps in new[] { dust, clods, sparks }) if (ps) ps.Clear(true);
        foreach (var gg in gouges) foreach (var l in gg.lines) if (l) { if (Application.isPlaying) Destroy(l.gameObject); else DestroyImmediate(l.gameObject); }
        gouges.Clear();
        foreach (var c in claws) { c.lastT = -1; c.cur = null; }
        lastSim = -1; contacts = 0; minH = 1e9f; maxV = 0;
    }
}
