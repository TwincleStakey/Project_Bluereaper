// Greatsword blade trail (11-14 hack-and-slash feel pass). Samples the blade (grip-side point and tip) every frame after
// animation while Emitting is on, keeps the last `life` seconds, and draws them as one ribbon mesh (Catmull-Rom
// smoothed, so a fast swing at 60 fps still reads as a clean arc). Ages run on scaled time, so the trail freezes with
// the hit-stop. One draw call; the mesh lives in world space on its own object.
// 11-15 sweep fix (Kido: "삼각형으로 공격"): a 3-frame swing gave 2-3 samples 40+ deg apart and the ribbon cut the corner
// into a flat-sided wedge. With a pivot (the player), each new sample is joined to the last one by sub-samples turned
// about the pivot's vertical axis (angle, radius and height interpolated, one every arcStep deg), so the edge is a circle.
using System.Collections.Generic;
using UnityEngine;

namespace RoomRun
{
    [DefaultExecutionOrder(300)]   // after SplitBody / LSPoseFix / eye flames: the blade where it is drawn
    public sealed class BladeTrail : MonoBehaviour
    {
        public Transform pivot;          // the player root: sub-samples turn about its vertical axis
        public float arcStep = 4f;       // degrees between sub-samples
        public Transform sword;          // the Greatsword transform (blade along local +Z, see LSPoseFix.FindSword)
        public float bladeLen = 1.1f;    // grip to tip, metres
        public float baseFrac = 0.28f;   // the ribbon starts this far up the blade
        public float life = 0.13f;       // seconds a sample stays
        public int subdiv = 4;           // smoothing points between two samples
        public bool Emitting;
        public float Intensity = 1f;

        struct Sample { public Vector3 b, t, c; public float time; }
        readonly List<Sample> samples = new List<Sample>(64);
        readonly List<Vector3> verts = new List<Vector3>(512);
        readonly List<Vector2> uvs = new List<Vector2>(512);
        readonly List<Color> cols = new List<Color>(512);
        readonly List<int> tris = new List<int>(1536);
        Mesh mesh;
        MeshRenderer mr;

        public static BladeTrail Create(Transform sword, float bladeLen)
        {
            var go = new GameObject("GS Blade Trail");
            DontDestroyOnLoad(go);   // fields load as scenes; like the slash pools it lives as long as the player
            var bt = go.AddComponent<BladeTrail>();
            bt.sword = sword; bt.bladeLen = bladeLen;
            bt.mesh = new Mesh { name = "BladeTrail" };
            bt.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = bt.mesh;
            bt.mr = go.AddComponent<MeshRenderer>();
            bt.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bt.mr.receiveShadows = false;
            var sh = Shader.Find("RoomRun/BladeTrail");
            if (sh) bt.mr.sharedMaterial = new Material(sh) { name = "RR_BladeTrail" };
            else Debug.LogWarning("[RoomRun] RoomRun/BladeTrail shader missing; no blade trail");
            return bt;
        }

        /// Blade length from the sword's meshes (farthest +Z extent in sword space), as LSPoseFix measures it.
        public static float MeasureBlade(Transform sword)
        {
            float tip = 0f;
            foreach (var mf in sword.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    tip = Mathf.Max(tip, sword.InverseTransformPoint(mf.transform.TransformPoint(c)).z * sword.lossyScale.z);
                }
            }
            return tip > 0.2f ? tip : 1.1f;
        }

        public void Clear() { samples.Clear(); if (mesh) mesh.Clear(); }

        void OnDestroy() { if (mesh) Destroy(mesh); if (mr && mr.sharedMaterial) Destroy(mr.sharedMaterial); }

        void LateUpdate()
        {
            float now = Time.time;
            if (sword && sword.gameObject.activeInHierarchy && Emitting)
            {
                var fwd = sword.forward;
                var s = new Sample { b = sword.position + fwd * (bladeLen * baseFrac), t = sword.position + fwd * bladeLen, time = now,
                                     c = pivot ? pivot.position : sword.position };
                if (pivot && samples.Count > 0) ArcFill(samples[samples.Count - 1], s);
                samples.Add(s);
            }
            while (samples.Count > 0 && now - samples[0].time > life) samples.RemoveAt(0);
            if (samples.Count > 400) samples.RemoveRange(0, samples.Count - 400);
            Build(now);
        }

        // sub-samples between a and b turned about the (moving) pivot's vertical axis
        void ArcFill(Sample a, Sample b)
        {
            static void Polar(Vector3 p, Vector3 c, out float ang, out float r, out float y)
            {
                var d = p - c; ang = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; r = new Vector2(d.x, d.z).magnitude; y = d.y;
            }
            Polar(a.t, a.c, out float at, out float rt0, out float yt0); Polar(b.t, b.c, out float bt, out float rt1, out float yt1);
            Polar(a.b, a.c, out float ab, out float rb0, out float yb0); Polar(b.b, b.c, out float bb, out float rb1, out float yb1);
            float dt = Mathf.DeltaAngle(at, bt), db = Mathf.DeltaAngle(ab, bb);
            // a near-centre base point flips angle easily: it turns with the tip
            if (Mathf.Abs(db - dt) > 90f) db = dt;
            int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(dt) / Mathf.Max(0.5f, arcStep)), 1, 24);
            for (int i = 1; i < n; i++)
            {
                float u = i / (float)n;
                var c = Vector3.Lerp(a.c, b.c, u);
                Vector3 P(float a0, float da, float r0, float r1, float y0, float y1)
                {
                    float ang = (a0 + da * u) * Mathf.Deg2Rad, r = Mathf.Lerp(r0, r1, u);
                    return c + new Vector3(Mathf.Sin(ang) * r, Mathf.Lerp(y0, y1, u), Mathf.Cos(ang) * r);
                }
                samples.Add(new Sample { t = P(at, dt, rt0, rt1, yt0, yt1), b = P(ab, db, rb0, rb1, yb0, yb1), c = c, time = Mathf.Lerp(a.time, b.time, u) });
            }
        }

        static Vector3 CR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        void Build(float now)
        {
            mesh.Clear();
            int n = samples.Count;
            if (n < 2) return;
            verts.Clear(); uvs.Clear(); cols.Clear(); tris.Clear();
            var col = new Color(1f, 1f, 1f, Mathf.Clamp01(Intensity));
            // oldest first; the newest point sits on the blade itself
            for (int i = 0; i < n - 1; i++)
            {
                var s0 = samples[Mathf.Max(0, i - 1)]; var s1 = samples[i]; var s2 = samples[i + 1]; var s3 = samples[Mathf.Min(n - 1, i + 2)];
                int steps = i == n - 2 ? subdiv + 1 : subdiv;
                for (int k = 0; k < steps; k++)
                {
                    float f = k / (float)subdiv;
                    var b = CR(s0.b, s1.b, s2.b, s3.b, f);
                    var t = CR(s0.t, s1.t, s2.t, s3.t, f);
                    float time = Mathf.Lerp(s1.time, s2.time, f);
                    float age = Mathf.Clamp01((now - time) / life);
                    // v3: soft tail (the oldest edge of a held hit read as a bright spike)
                    var cf = col; cf.a = col.a * (1f - age) * (1f - age);
                    verts.Add(b); uvs.Add(new Vector2(age, 0f)); cols.Add(cf);
                    verts.Add(t); uvs.Add(new Vector2(age, 1f)); cols.Add(cf);
                }
            }
            for (int i = 0; i + 3 < verts.Count; i += 2)
            {
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i + 2); tris.Add(i + 1); tris.Add(i + 3);
            }
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetColors(cols); mesh.SetTriangles(tris, 0, false);
            mesh.RecalculateBounds();
        }
    }
}
