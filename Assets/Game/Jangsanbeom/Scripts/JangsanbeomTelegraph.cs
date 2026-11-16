// 장산범 attack telegraph: makes the fur glow red (and the eyes flare) on the JangsanbeomGlow material before an attack.
// Call Telegraph(seconds) from the boss AI right before the howl or the charge; the glow ramps in, holds, and fades out.
using UnityEngine;

public class JangsanbeomTelegraph : MonoBehaviour
{
    [Tooltip("Body renderers that use BulletHeaven/JangsanbeomGlow. Filled automatically if empty.")]
    public Renderer[] bodyRenderers;
    public float rampIn = 0.25f, rampOut = 0.6f;
    [Range(0, 1)] public float maxWarn = 1f;

    static readonly int WarnId = Shader.PropertyToID("_Warn");
    MaterialPropertyBlock mpb;
    float holdUntil = -1f, warn;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        if (bodyRenderers == null || bodyRenderers.Length == 0)
            bodyRenderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(true), r => r.sharedMaterial != null && r.sharedMaterial.HasProperty(WarnId) && r.sharedMaterial.shader.name != "BulletHeaven/WindKey");   // the key keeps its slow turn
        Apply(0f);
    }

    /// <summary>Glow red for this many seconds (the wind-up before an attack).</summary>
    public void Telegraph(float seconds) { holdUntil = Mathf.Max(holdUntil, Time.time + seconds); }
    public void Stop() { holdUntil = -1f; }

    void Update()
    {
        float target = Time.time < holdUntil ? maxWarn : 0f;
        float rate = target > warn ? 1f / Mathf.Max(rampIn, 0.01f) : 1f / Mathf.Max(rampOut, 0.01f);
        float next = Mathf.MoveTowards(warn, target, rate * Time.deltaTime);
        if (!Mathf.Approximately(next, warn)) Apply(next);
    }

    void Apply(float w)
    {
        warn = w;
        foreach (var r in bodyRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb); mpb.SetFloat(WarnId, w); r.SetPropertyBlock(mpb);
        }
    }
}
