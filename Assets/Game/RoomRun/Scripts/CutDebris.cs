// Greatsword cut debris: real pieces of the plants a swing cut, flung by a cheap ballistic + tumble simulation.
// Reeds: the top of the reed model (GS_CutPieces.asset, baked from the reed FBXs by ls_cmd `cutpieces`) in the reed's own
// material. Grass: the actual blade copied out of the baked grass field mesh (or the instanced blade mesh), in that
// field's grass colour. Each piece gets a random launch (outward + along the swing + up), random spin, gravity, air drag,
// bounces and settles on the ground, then shrinks away. Pooled, at most `poolSize` alive (oldest recycled).
using System.Collections.Generic;
using UnityEngine;

namespace RoomRun
{
    public sealed class CutDebris : MonoBehaviour
    {
        public int poolSize = 40;
        public float gravity = 13f;

        sealed class Piece
        {
            public GameObject go; public Transform tr; public MeshFilter mf; public MeshRenderer mr;
            public Mesh own;                // per-piece mesh for grass blades copied from a field
            public Vector3 vel, spin;       // spin: angular velocity (rad/s, world)
            public float delay, age, life, drag, ground, baseScale, bounce;
            public bool alive, resting;
        }

        readonly List<Piece> pool = new List<Piece>();
        CutPieceSet set;
        Material grassMat;
        MaterialPropertyBlock mpb;
        static readonly int IdCutOn = Shader.PropertyToID("_RRCutOn"), IdBase = Shader.PropertyToID("_BaseColor");
        readonly List<Vector3> vTmp = new List<Vector3>();
        readonly List<Vector2> uvTmp = new List<Vector2>();

        void Awake()
        {
            set = Resources.Load<CutPieceSet>("GS_CutPieces");
            mpb = new MaterialPropertyBlock();
            var lit = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (lit)
            {
                grassMat = new Material(lit) { name = "GS Cut Grass" };
                grassMat.SetFloat("_Cull", 0f);   // blades are single-sided cards
                grassMat.enableInstancing = true;
            }
            for (int i = 0; i < poolSize; i++)
            {
                var go = new GameObject("cut piece");
                go.transform.SetParent(transform, false);
                var p = new Piece { go = go, tr = go.transform, mf = go.AddComponent<MeshFilter>(), mr = go.AddComponent<MeshRenderer>() };
                p.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                p.mr.receiveShadows = true;
                go.SetActive(false);
                pool.Add(p);
            }
        }

        void OnDestroy()
        {
            foreach (var p in pool) if (p.own) Destroy(p.own);
            if (grassMat) Destroy(grassMat);
        }

        Piece Take()
        {
            Piece best = null;
            foreach (var p in pool)
            {
                if (!p.alive) return p;
                if (best == null || p.age > best.age) best = p;   // recycle the oldest
            }
            return best;
        }

        /// Throw pieces for the plants hit by a swing. centre = player, aim = swing direction, sweepSign = +1/-1 (which
        /// way round the blade travels) for the tangential push.
        public void Spawn(List<VegetationQuery.Hit> hits, Vector3 centre, Vector3 aim, float sweepSign)
        {
            foreach (var h in hits)
            {
                var p = Take();
                if (p == null) return;
                if (!Setup(p, h)) continue;
                var outward = h.pos - centre; outward.y = 0;
                outward = outward.sqrMagnitude > 0.01f ? outward.normalized : aim;
                var tangent = Vector3.Cross(Vector3.up, outward) * sweepSign;
                bool reed = h.kind == VegetationQuery.Kind.Reed;
                float up = reed ? Random.Range(2.5f, 6.5f) : Random.Range(2f, 5.5f);
                p.vel = outward * Random.Range(1.2f, 4.5f) + tangent * Random.Range(0.5f, 4f) + aim * Random.Range(0f, 1.5f)
                        + Vector3.up * up + Random.insideUnitSphere * 0.8f;
                p.spin = Random.onUnitSphere * Random.Range(4f, reed ? 11f : 16f);
                p.delay = Random.Range(0f, 0.08f);
                p.age = 0f;
                p.life = Random.Range(2.4f, 3.8f);
                p.drag = reed ? Random.Range(0.6f, 1.2f) : Random.Range(1.2f, 2.2f);   // blades are light, catch the air
                p.bounce = Random.Range(0.15f, 0.35f);
                p.ground = h.pos.y + 0.02f;
                p.alive = true; p.resting = false;
                p.go.SetActive(false);   // shown after its delay
            }
        }

        bool Setup(Piece p, VegetationQuery.Hit h)
        {
            if (h.kind == VegetationQuery.Kind.Reed)
            {
                var pieces = set ? set.For(h.mesh) : null;
                if (pieces == null || pieces.Length == 0 || !h.mat) return false;
                p.mf.sharedMesh = pieces[Random.Range(0, pieces.Length)];
                p.mr.sharedMaterial = h.mat;
                float s = h.scale * Random.Range(0.85f, 1.1f);
                p.baseScale = s;
                // starts where the stalk was cut (40 % up the clump), leaning like the clump
                p.tr.SetPositionAndRotation(h.pos + Vector3.up * s * 0.4f, h.rot * Quaternion.Euler(Random.Range(-15f, 15f), Random.Range(0f, 360f), Random.Range(-15f, 15f)));
                mpb.Clear();
                mpb.SetFloat(IdCutOn, 0f);   // the cut map would squash a piece flying over the cut ground
                p.mr.SetPropertyBlock(mpb);
                p.tr.localScale = Vector3.one * s;
                return true;
            }
            if (!grassMat) return false;
            if (h.field)
            {
                // the blade itself, copied out of the field mesh (5 vertices, uv0 kept), pivot at its root
                if (!p.own) p.own = new Mesh { name = "cut blade" };
                var src = h.field;
                vTmp.Clear(); src.GetVertices(vTmp);
                uvTmp.Clear(); src.GetUVs(0, uvTmp);
                if (h.vertex + 5 > vTmp.Count) return false;
                var bv = new Vector3[5]; var buv = new Vector2[5];
                for (int i = 0; i < 5; i++) { bv[i] = vTmp[h.vertex + i] - h.pos; buv[i] = uvTmp[h.vertex + i]; }
                p.own.Clear();
                p.own.vertices = bv; p.own.uv = buv;
                p.own.triangles = new[] { 0, 2, 1, 1, 2, 3, 2, 4, 3 };
                p.own.RecalculateNormals(); p.own.RecalculateBounds();
                p.mf.sharedMesh = p.own;
                p.baseScale = Random.Range(0.8f, 1.15f);
                p.tr.SetPositionAndRotation(h.pos + Vector3.up * 0.06f, Quaternion.Euler(Random.Range(-20f, 20f), 0, Random.Range(-20f, 20f)));
            }
            else if (h.mesh)
            {
                p.mf.sharedMesh = h.mesh;
                p.baseScale = h.scale * Random.Range(0.8f, 1.05f);
                p.tr.SetPositionAndRotation(h.pos + Vector3.up * 0.06f, h.rot);
            }
            else return false;
            p.mr.sharedMaterial = grassMat;
            mpb.Clear();
            float v = Random.Range(0.8f, 1.15f);
            mpb.SetColor(IdBase, new Color(h.color.r * v, h.color.g * v, h.color.b * v, 1f));
            p.mr.SetPropertyBlock(mpb);
            p.tr.localScale = Vector3.one * p.baseScale;
            return true;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            foreach (var p in pool)
            {
                if (!p.alive) continue;
                if (p.delay > 0f) { p.delay -= dt; if (p.delay > 0f) continue; p.go.SetActive(true); }
                else if (!p.go.activeSelf) p.go.SetActive(true);
                p.age += dt;
                if (!p.resting)
                {
                    p.vel.y -= gravity * dt;
                    p.vel /= 1f + p.drag * dt;
                    var pos = p.tr.position + p.vel * dt;
                    if (pos.y <= p.ground)
                    {
                        pos.y = p.ground;
                        if (p.vel.y < -1.2f)
                        {
                            p.vel = new Vector3(p.vel.x * 0.55f, -p.vel.y * p.bounce, p.vel.z * 0.55f);
                            p.spin *= 0.5f;
                        }
                        else { p.resting = true; p.vel = Vector3.zero; }
                    }
                    p.tr.position = pos;
                    float w = p.spin.magnitude;
                    if (w > 1e-3f) p.tr.rotation = Quaternion.AngleAxis(w * dt * Mathf.Rad2Deg, p.spin / w) * p.tr.rotation;
                }
                else
                {
                    // lie down: tip the piece's long axis toward the ground
                    var upAxis = p.tr.up;
                    var flat = new Vector3(upAxis.x, 0, upAxis.z);
                    if (flat.sqrMagnitude < 1e-3f) flat = Vector3.forward;
                    var target = Quaternion.FromToRotation(upAxis, flat.normalized) * p.tr.rotation;
                    p.tr.rotation = Quaternion.Slerp(p.tr.rotation, target, 1f - Mathf.Exp(-8f * dt));
                }
                // shrink away at the end of its life
                float fade = Mathf.Clamp01((p.life - p.age) / 0.45f);
                p.tr.localScale = Vector3.one * p.baseScale * fade;
                if (p.age >= p.life) { p.alive = false; p.go.SetActive(false); }
            }
        }
    }
}
