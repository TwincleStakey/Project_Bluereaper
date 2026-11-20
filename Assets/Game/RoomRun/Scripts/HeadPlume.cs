// 11-19 Kido: "a Joseon headband with two big pheasant tail feathers curving back (like 여포's 꿩깃), glowing". Built in
// code on the suit body's head: a black band with a glowing front ornament, and two long tapered feather ribbons that rise
// from the forehead, sweep up and arc back over the head. Each feather is a spring chain that trails its rest shape, so it
// lags and whips when the body turns (the whip's body follow drives the head). Emissive (HDR) so the bloom picks it up.
using System.Collections.Generic;
using UnityEngine;

namespace RoomRun
{
    [DefaultExecutionOrder(300)]   // after SuitBody (255) and WhipCombat (260, head turn in BodyFollow)
    public sealed class HeadPlume : MonoBehaviour
    {
        public static Color Glow = new Color(1f, 0f, 0f);   // pure red: the stage grade takes ~55% saturation away, so start fully saturated; tips go orange
        public static Color TipGlow = new Color(1f, 0.12f, 0f);
        public static float GlowMul = 4f;   // pure red survives the grade only when R alone is high (G/B at 0)
        public static float FeatherLen = 1.7f;   // metres at an adult head (scaled with the head)

        Transform head; float k;
        const int Pts = 14;
        class Feather { public bool rigid; public Vector3[] rest = new Vector3[Pts]; public Vector3[] p = new Vector3[Pts], q = new Vector3[Pts]; public Mesh mesh; public GameObject go; public bool init; }
        readonly List<Feather> feathers = new List<Feather>();
        Material featherMat; GameObject band;

        public static void Attach(Animator a)
        {
            if (!a || a.GetComponent<HeadPlume>()) return;
            a.gameObject.AddComponent<HeadPlume>().Build(a);
        }

        void Build(Animator a)
        {
            head = a.GetBoneTransform(HumanBodyBones.Head);
            if (!head) return;
            Transform top = null;
            foreach (var t in head.GetComponentsInChildren<Transform>(true)) if (t.name.Contains("HeadTop")) { top = t; break; }
            var root = a.transform;
            Vector3 up = root.up, fwd = root.forward, right = root.right;
            float d = top ? Vector3.Distance(head.position, top.position) : 0.2f * root.lossyScale.y;
            k = d / 0.2f;                                         // 1 at an adult Mixamo head
            Vector3 c = head.position + up * (d * 0.62f);          // band height: forehead
            float r = d * 0.5f;

            if (Hat) { BuildHat(a, top, up, fwd, right, d); return; }
            // band: a ring (dark lacquer) + a small glowing jewel at the front
            band = new GameObject("PlumeBand");
            band.transform.SetParent(head, false);
            var bm = new Mesh { name = "PlumeBand" };
            var bv = new List<Vector3>(); var bt = new List<int>();
            int seg = 28; float h = 0.04f * k;
            for (int i = 0; i <= seg; i++)
            {
                float ang = i / (float)seg * Mathf.PI * 2f;
                var dir = (right * Mathf.Sin(ang) + fwd * Mathf.Cos(ang) * 1.12f) * r;   // a bit deeper front-back
                bv.Add(head.InverseTransformPoint(c + dir - up * h * 0.5f));
                bv.Add(head.InverseTransformPoint(c + dir + up * h * 0.5f));
                if (i < seg) { int b = i * 2; bt.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 }); }
            }
            bm.SetVertices(bv); bm.SetTriangles(bt, 0); bm.RecalculateNormals(); bm.RecalculateBounds();
            band.AddComponent<MeshFilter>().sharedMesh = bm;
            var lit = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "PlumeBand" };
            lit.SetColor("_BaseColor", new Color(0.03f, 0.03f, 0.035f)); lit.SetFloat("_Smoothness", 0.25f); lit.SetFloat("_Cull", 0f);
            band.AddComponent<MeshRenderer>().sharedMaterial = lit;

            var unlit = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            featherMat = new Material(unlit) { name = "PlumeFeather" };
            featherMat.SetColor("_BaseColor", Glow * GlowMul);   // vertex colours are ignored by this shader here: the colour lives in the material
            featherMat.SetFloat("_Cull", 0f);
            featherMat.renderQueue = 2450;

            var jewel = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(jewel.GetComponent<Collider>());
            jewel.name = "PlumeJewel";
            jewel.transform.SetParent(head, true);
            jewel.transform.position = c + fwd * r * 1.12f;
            jewel.transform.localScale = Vector3.one * (0.035f * k / Mathf.Max(1e-4f, head.lossyScale.x));
            var jm = new Material(featherMat); jm.SetColor("_BaseColor", Glow * (GlowMul * 1.3f));
            jewel.GetComponent<Renderer>().sharedMaterial = jm;

            // feathers: from the front of the band, up and slightly out, over the top, arcing back and down behind
            for (int s = -1; s <= 1; s += 2)
            {
                var f = new Feather(); Vector3 cur = Vector3.zero;
                var basePt = c + fwd * r * 0.95f + right * (s * r * 0.35f) + up * h * 0.4f;
                float L = FeatherLen * k;
                for (int i = 0; i < Pts; i++)
                {
                    float u = i / (float)(Pts - 1);
                    // v2 (Kido 17:58 "깃털 모양이 왜 저래": v1 was a 200 deg hoop): 꿩깃 like the 여포 helmet plumes: leaves the
                    // band almost straight up (a touch forward), then bends back in one long sweep, the tip trailing behind
                    // the head slightly downward; the pair opens in a V
                    if (i == 0) { cur = basePt; }
                    else
                    {
                        float bend = Mathf.Lerp(-6f, 55f, Mathf.Pow(u, 2.2f)) * Mathf.Deg2Rad;   // v4 (Kido 17:58 "빠딱 서있고"): upright, only the top third leans back (was -8..118)   // from vertical toward the back
                        var dir = (up * Mathf.Cos(bend) - fwd * Mathf.Sin(bend) + right * (s * 0.22f)).normalized;
                        cur += dir * (L / (Pts - 1));
                    }
                    f.rest[i] = head.InverseTransformPoint(cur);
                }
                f.go = new GameObject(s < 0 ? "PlumeL" : "PlumeR");
                f.mesh = new Mesh { name = "PlumeFeather" }; f.mesh.MarkDynamic();
                f.go.AddComponent<MeshFilter>().sharedMesh = f.mesh;
                f.go.AddComponent<MeshRenderer>().sharedMaterial = featherMat;
                feathers.Add(f);
            }
        }

        // v5 (Kido 20:20, ref 경복궁 수문장): a 주립 (wide flat brim, short round crown, round badge in front) in black lacquer,
        // with two thin plumes standing straight up from the crown, rigid, burning as hard light rods (no sway).
        public static bool Hat = true;
        public static float RodGlow = 3.5f;   // v6 (Kido 11-20 "광원을 해당 깃털만"): 14 bloomed into a big red orb round the head   // v5b: brighter so the bloom makes a hard light rod
        readonly List<GameObject> hatParts = new List<GameObject>();
        GameObject Part(PrimitiveType t, Vector3 pos, Quaternion rot, Vector3 size, Material m, string name)
        {
            var g = GameObject.CreatePrimitive(t);
            Destroy(g.GetComponent<Collider>());
            g.name = name;
            g.transform.SetParent(head, true);
            g.transform.SetPositionAndRotation(pos, rot);
            float hs = Mathf.Max(1e-4f, head.lossyScale.x);
            g.transform.localScale = size / hs;
            g.GetComponent<Renderer>().sharedMaterial = m;
            hatParts.Add(g);
            return g;
        }
        void BuildHat(Animator a, Transform top, Vector3 up, Vector3 fwd, Vector3 right, float d)
        {
            var topPos = top ? top.position : head.position + up * d;
            var rot = Quaternion.LookRotation(fwd, up);
            var lacquer = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "HatLacquer" };
            lacquer.SetColor("_BaseColor", new Color(0.008f, 0.007f, 0.007f)); lacquer.SetFloat("_Smoothness", 0.05f);
            lacquer.SetFloat("_SpecularHighlights", 0f); lacquer.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            lacquer.SetFloat("_EnvironmentReflections", 0f); lacquer.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");   // v5b: read black, not grey
            float brimY = -0.05f * k, brimD = 0.46f * k, crownD = 0.17f * k, crownH = 0.12f * k;
            var brimC = topPos + up * brimY;
            Part(PrimitiveType.Cylinder, brimC, rot, new Vector3(brimD, 0.006f * k, brimD), lacquer, "HatBrim");
            var crownC = brimC + up * (crownH * 0.5f);
            Part(PrimitiveType.Cylinder, crownC, rot, new Vector3(crownD, crownH * 0.5f, crownD), lacquer, "HatCrown");
            Part(PrimitiveType.Cylinder, crownC + up * (crownH * 0.5f), rot, new Vector3(crownD * 0.92f, 0.004f * k, crownD * 0.92f), lacquer, "HatTop");
            // round badge on the crown front (dim gold, like the 수문장 패)
            var gold = new Material(lacquer) { name = "HatBadge" }; gold.SetColor("_BaseColor", new Color(0.55f, 0.38f, 0.08f)); gold.SetFloat("_Smoothness", 0.5f);
            Part(PrimitiveType.Cylinder, crownC + fwd * (crownD * 0.5f + 0.002f * k), Quaternion.LookRotation(up, fwd) , new Vector3(0.07f * k, 0.004f * k, 0.07f * k), gold, "HatBadge");
            // two rigid light rods from the crown top: nearly vertical, splayed a little out and back
            var unlit = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            featherMat = new Material(unlit) { name = "PlumeRod" };
            featherMat.SetColor("_BaseColor", Glow * RodGlow);
            featherMat.SetFloat("_Cull", 0f);
            featherMat.renderQueue = 2450;
            var crownTop = crownC + up * (crownH * 0.5f);
            for (int s = -1; s <= 1; s += 2)
            {
                var f = new Feather { rigid = true };
                var basePt = crownTop + right * (s * crownD * 0.22f) - fwd * (crownD * 0.1f);
                var dir = (up + right * (s * 0.13f) - fwd * 0.07f).normalized;
                float L = RodLen * k;
                for (int i = 0; i < Pts; i++) f.rest[i] = head.InverseTransformPoint(basePt + dir * (L * i / (Pts - 1)));
                f.go = new GameObject(s < 0 ? "PlumeRodL" : "PlumeRodR");
                f.mesh = new Mesh { name = "PlumeRod" }; f.mesh.MarkDynamic();
                f.go.AddComponent<MeshFilter>().sharedMesh = f.mesh;
                f.go.AddComponent<MeshRenderer>().sharedMaterial = featherMat;
                feathers.Add(f);
            }
            // a real light source: the rods throw red light on the hat brim, shoulders and the ground around
            if (RodLight <= 0f) return;   // v6: off; the glow lives on the two rods only
            var lg = new GameObject("PlumeLight");
            lg.transform.SetParent(head, true);
            lg.transform.position = crownTop + up * (RodLen * k * 0.45f);
            var li = lg.AddComponent<Light>();
            li.type = LightType.Point; li.color = new Color(1f, 0.12f, 0.08f); li.range = 2.2f * k; li.intensity = RodLight; li.shadows = LightShadows.None;
            hatParts.Add(lg);
        }
        public static float RodLen = 0.5f, RodLight = 0f;   // v6: was 3 (point light lit the hat, shoulders and ground red)

        void LateUpdate()
        {
            if (!head) return;
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            foreach (var f in feathers)
            {
                for (int i = 0; i < Pts; i++)
                {
                    var target = head.TransformPoint(f.rest[i]);
                    if (!f.init || i == 0 || f.rigid) { f.p[i] = target; f.q[i] = target; continue; }
                    float u = i / (float)(Pts - 1);
                    var vel = (f.p[i] - f.q[i]) * 0.5f;   // v4: heavy damping (was .86)
                    f.q[i] = f.p[i];
                    float pull = Mathf.Lerp(0.95f, 0.6f, u);   // v4: stiff, barely moves (was .7/.25)   // v2: stiffer (was .55/.12), the shape holds and only the tip whips                 // the base is stiff, the tip trails
                    f.p[i] += vel + (target - f.p[i]) * pull + Vector3.down * (0.1f * u * dt * dt * 60f);
                }
                // keep segment lengths (inextensible feather)
                for (int it = 0; it < 2; it++)
                    for (int i = 1; i < Pts; i++)
                    {
                        float want = (head.TransformPoint(f.rest[i]) - head.TransformPoint(f.rest[i - 1])).magnitude;
                        var dv = f.p[i] - f.p[i - 1]; float len = dv.magnitude;
                        if (len > 1e-5f) f.p[i] = f.p[i - 1] + dv * (want / len);
                    }
                f.init = true;
                BuildRibbon(f);
            }
        }

        readonly List<Vector3> rv = new List<Vector3>(); readonly List<Color> rc = new List<Color>(); readonly List<int> rt = new List<int>();
        void BuildRibbon(Feather f)
        {
            rv.Clear(); rc.Clear(); rt.Clear();
            var side0 = head.right;
            for (int i = 0; i < Pts; i++)
            {
                float u = i / (float)(Pts - 1);
                var tan = (i < Pts - 1 ? f.p[i + 1] - f.p[i] : f.p[i] - f.p[i - 1]).normalized;
                var side = Vector3.Cross(tan, Vector3.Cross(side0, tan)).sqrMagnitude > 1e-6f ? Vector3.Cross(tan, Vector3.Cross(side0, tan)).normalized : side0;
                // the vane lies flat across (seen from above), widest a third of the way, pointed tip
                float w = f.rigid ? k * Mathf.Lerp(0.045f, 0.028f, u) : k * (0.012f + 0.05f * Mathf.Sin(Mathf.Pow(u, 0.7f) * Mathf.PI) * (1f - 0.5f * u));   // v3: a real vane (wide in the middle, pointed tip); v2 .022->.004 read as antennae from above   // v2: slender tail feather, pointed
                side = Vector3.Cross(tan, Vector3.up).sqrMagnitude > 0.05f ? Vector3.Cross(tan, Vector3.up).normalized : side;
                rv.Add(f.p[i] - side * w); rv.Add(f.p[i] + side * w);
                // banded pattern like a pheasant tail: crimson with brighter bars, gold toward the tip
                float bar = 0.65f + 0.35f * (Mathf.Repeat(u * 9f, 1f) < 0.5f ? 1f : 0f);
                var col = Color.Lerp(Glow, TipGlow, Mathf.SmoothStep(0.5f, 1f, u)) * bar;
                col.a = 1f;
                rc.Add(col); rc.Add(col);
                if (i < Pts - 1) { int b = i * 2; rt.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 }); }
            }
            f.mesh.Clear();
            f.mesh.SetVertices(rv); f.mesh.SetColors(rc); f.mesh.SetTriangles(rt, 0);
            f.mesh.RecalculateBounds();
        }

        void OnDestroy()
        {
            foreach (var f in feathers) if (f.go) Destroy(f.go);
            if (band) Destroy(band);
            foreach (var g in hatParts) if (g) Destroy(g);
        }
    }

}
