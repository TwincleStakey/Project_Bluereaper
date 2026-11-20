// 장산범 claw trails: a red afterimage ribbon follows each front claw tip while the paw moves fast (a swipe, a scrape),
// so the player can read the attack and dodge. Runs by itself in Play mode; capture tools call Sample(time) per frame.
using System.Collections.Generic;
using UnityEngine;

public class JangsanbeomClawTrails : MonoBehaviour
{
    public string[] toeBones = { "FrontToes.L", "FrontToes.R" };
    public Material trailMaterial;
    [Tooltip("Seconds a trail point lives")] public float lifetime = 0.28f;
    [Tooltip("Ribbon width at the claw (world units)")] public float width = 1.6f;
    [Tooltip("Claw speed (world units / s) where the trail starts to show")] public float minSpeed = 2.5f;
    [Tooltip("Claw speed where the trail is fully bright")] public float fullSpeed = 10f;

    class Trail
    {
        public Transform toe, paw; public LineRenderer line;
        public readonly List<(Vector3 p, float t, float a)> pts = new List<(Vector3, float, float)>();
        public Vector3 last; public float lastT = -1;
    }
    readonly List<Trail> trails = new List<Trail>();

    void Awake() { Build(); }

    public void Build()
    {
        foreach (var tr in trails) if (tr.line) DestroyImmediate(tr.line.gameObject);
        trails.Clear();
        var all = GetComponentsInChildren<Transform>(true);
        foreach (var n in toeBones)
        {
            var toe = System.Array.Find(all, t => t.name == n); if (toe == null) continue;
            var go = new GameObject($"ClawTrail_{n}"); go.transform.SetParent(transform, false);
            go.hideFlags = HideFlags.DontSave;
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.sharedMaterial = trailMaterial; lr.numCapVertices = 2; lr.alignment = LineAlignment.View;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false; lr.positionCount = 0;
            lr.textureMode = LineTextureMode.Stretch;
            lr.widthCurve = new AnimationCurve(new Keyframe(0, 0.05f), new Keyframe(0.8f, 0.8f), new Keyframe(1, 1f));   // thin tail -> wide at the claw
            trails.Add(new Trail { toe = toe, paw = toe.parent, line = lr });
        }
    }

    Vector3 Tip(Trail tr) => tr.paw ? tr.toe.position + (tr.toe.position - tr.paw.position) * 0.8f : tr.toe.position;

    void LateUpdate() { if (Application.isPlaying) Sample(Time.time); }

    /// <summary>Record the claw tips at this time and rebuild the ribbons.</summary>
    public void Sample(float time)
    {
        foreach (var tr in trails)
        {
            var p = Tip(tr);
            float a = 0;
            if (tr.lastT >= 0 && time > tr.lastT)
            {
                float speed = (p - tr.last).magnitude / (time - tr.lastT);
                a = Mathf.Clamp01((speed - minSpeed) / Mathf.Max(fullSpeed - minSpeed, 0.01f));
                // fill fast swipes with in-between points so the ribbon stays a smooth arc
                int sub = Mathf.Clamp(Mathf.CeilToInt((p - tr.last).magnitude / (width * 0.5f)), 1, 8);
                for (int k = 1; k < sub; k++) tr.pts.Add((Vector3.Lerp(tr.last, p, k / (float)sub), Mathf.Lerp(tr.lastT, time, k / (float)sub), a));
            }
            tr.pts.Add((p, time, a));
            tr.pts.RemoveAll(q => time - q.t > lifetime || q.t > time);
            tr.last = p; tr.lastT = time;

            var lr = tr.line; int n = tr.pts.Count; lr.positionCount = n; lr.widthMultiplier = width;
            if (n < 2) { lr.positionCount = 0; continue; }
            var cols = new GradientColorKey[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) };
            var alphas = new List<GradientAlphaKey>();
            for (int i = 0; i < n; i++)
            {
                lr.SetPosition(i, tr.pts[i].p);
                if (alphas.Count < 8 && (i % Mathf.Max(1, n / 7) == 0 || i == n - 1))
                {
                    float age = (time - tr.pts[i].t) / lifetime;
                    alphas.Add(new GradientAlphaKey(tr.pts[i].a * (1 - age) * (1 - age), i / (float)(n - 1)));
                }
            }
            var g = new Gradient(); g.SetKeys(cols, alphas.ToArray()); lr.colorGradient = g;
        }
    }

    /// <summary>Forget the trail history (e.g. after a teleport or a new capture).</summary>
    public void ResetTrails() { foreach (var tr in trails) { tr.pts.Clear(); tr.lastT = -1; tr.line.positionCount = 0; } }
}
