// 장산범 footfall dust (11-19 Kido: "발 걸을 때마다 연기나게"): every time a paw comes down, a ring of dust puffs rolls out
// low along the ground and lifts slowly, with a few grit flecks thrown out. Puffs use the Kenney CC0 smoke flipbook already in
// the project (BossSeal/Source/Kenney_Smoke). Runs by itself in Play mode; capture tools call Sample(time) per frame.
using UnityEngine;

public class JangsanbeomFootDust : MonoBehaviour
{
    public string[] toeBones = { "FrontToes.L", "FrontToes.R", "HindToes.L", "HindToes.R" };
    public Material dustMaterial;            // alpha-blended particles, 4x4 smoke flipbook
    [Tooltip("A paw counts as down within this height of the lowest it has been (world units)")] public float contactBand = 0.25f;
    public int puffs = 16;
    public float puffSize = 6.5f, puffSpeed = 4f, puffLife = 2.2f;
    public Color dustColor = new Color(0.62f, 0.5f, 0.4f, 0.8f);

    Transform[] toes; float[] lowest; bool[] down; ParticleSystem ps; float lastSim = -1;

    void Awake() { Build(); }

    public void Build()
    {
        foreach (Transform c in transform) if (c.name == "FootDustFX") { if (Application.isPlaying) Destroy(c.gameObject); else DestroyImmediate(c.gameObject); }
        var all = GetComponentsInChildren<Transform>(true);
        toes = System.Array.ConvertAll(toeBones, n => System.Array.Find(all, t => t.name == n));
        lowest = new float[toes.Length]; down = new bool[toes.Length];
        for (int i = 0; i < toes.Length; i++) { lowest[i] = float.MaxValue; down[i] = true; }
        var go = new GameObject("FootDustFX") { hideFlags = HideFlags.DontSave }; go.transform.SetParent(transform, false);
        ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = false; m.playOnAwake = false; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 1500;
        m.startLifetime = puffLife; m.startSize = puffSize; m.gravityModifier = -0.02f; m.startSpeed = 0;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0.6f; lim.dampen = 0.08f;   // rolls out fast, then hangs
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.07f), new GradientAlphaKey(0.55f, 0.45f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.45f), new Keyframe(0.3f, 1f), new Keyframe(1, 1.7f)));
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        var ts = ps.textureSheetAnimation; ts.enabled = true; ts.numTilesX = 4; ts.numTilesY = 4;
        ts.animation = ParticleSystemAnimationType.WholeSheet; ts.startFrame = new ParticleSystem.MinMaxCurve(0, 15); ts.frameOverTime = new ParticleSystem.MinMaxCurve(0.25f);
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = dustMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; r.sortingFudge = -5;
    }

    void LateUpdate() { if (Application.isPlaying) Sample(Time.time); }

    public void Sample(float time)
    {
        if (toes == null) return;
        if (lastSim >= 0 && time < lastSim) ResetFX();
        for (int i = 0; i < toes.Length; i++)
        {
            var t = toes[i]; if (t == null) continue;
            float y = t.position.y; lowest[i] = Mathf.Min(lowest[i], y);
            bool now = y <= lowest[i] + contactBand;
            if (now && !down[i] && lastSim >= 0) Puff(t.position, i);
            down[i] = now;
        }
        if (!Application.isPlaying)
        {
            float step = lastSim < 0 ? 0f : time - lastSim;
            if (step > 0 && ps) ps.Simulate(step, true, false, false);
        }
        lastSim = time;
    }

    void Puff(Vector3 at, int leg)
    {
        var e = new ParticleSystem.EmitParams();
        float ground = at.y - 0.1f;
        bool hind = leg >= 2;
        int n = Mathf.RoundToInt(puffs * (hind ? 0.8f : 1f));
        for (int k = 0; k < n; k++)
        {
            float a = (k + Random.value * 0.6f) / n * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            e.position = new Vector3(at.x, ground + 0.3f, at.z) + dir * Random.Range(0.6f, 1.4f);
            e.velocity = dir * puffSpeed * Random.Range(0.6f, 1.3f) + Vector3.up * Random.Range(0.3f, 1.2f);
            e.startSize = puffSize * Random.Range(0.7f, 1.25f); e.startLifetime = puffLife * Random.Range(0.75f, 1.2f);
            e.rotation = Random.Range(0f, 360f);
            var c = dustColor; float v = Random.Range(0.85f, 1.1f); e.startColor = new Color(c.r * v, c.g * v, c.b * v, c.a);
            ps.Emit(e, 1);
        }
    }

    public void ResetFX()
    {
        if (ps) ps.Clear(true);
        if (lowest != null) for (int i = 0; i < lowest.Length; i++) { lowest[i] = float.MaxValue; down[i] = true; }
        lastSim = -1;
    }
}
