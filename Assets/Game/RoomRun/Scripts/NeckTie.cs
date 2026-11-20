// 11-20 Kido: "넥타이랑 리본 넥타이 텍스처 너무 이상한데, 그냥 흰색으로 다 칠하고 넥타이만 하나 클로스 시뮬로 붙이는 게 나을 것 같은데".
// On the suit body (Mixamo Joe): the shirt front is painted plain white (its texture had a painted tie and bow tie), Joe's own
// tie mesh is hidden, and one red necktie hangs from the collar as a Unity Cloth strip: the knot rows are pinned to the upper
// chest, the blade swings with the walk and lies on a capsule round the chest. Proposal: OFF by default until Kido approves
// (shots token tie=1 turns it on for a capture).
using System.Collections.Generic;
using UnityEngine;

namespace RoomRun
{
    public sealed class NeckTie : MonoBehaviour
    {
        public static bool On = false;                                  // shots token tie=0|1
        public static Color TieColor = new Color(0.62f, 0.03f, 0.03f);
        public static Color ShirtColor = new Color(0.82f, 0.82f, 0.80f);
        public static float Length = 0.36f, KnotW = 0.045f, BladeW = 0.085f;   // metres at an adult Mixamo body (scaled with it)

        public static void Attach(Animator body)
        {
            if (!On || !body || body.GetComponentInChildren<NeckTie>()) return;
            var chest = body.GetBoneTransform(HumanBodyBones.UpperChest) ?? body.GetBoneTransform(HumanBodyBones.Chest);
            var neck = body.GetBoneTransform(HumanBodyBones.Neck);
            if (!chest || !neck) return;
            // shirt front plain white, Joe's own tie hidden
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.Contains("Tie")) { r.enabled = false; continue; }
                if (!r.name.Contains("Shirt")) continue;
                foreach (var m in r.materials)
                {
                    if (!m) continue;
                    if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Texture2D.whiteTexture);
                    if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", Texture2D.whiteTexture);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", ShirtColor);
                    // v3 (Kido "나비넥타이 같은 검은 선도 지워"): the dark loops at the collar were the black neck seen through the
                    // collar's culled inner faces; draw the shirt two-sided so the collar inside reads white
                    if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
                }
            }
            var go = new GameObject("NeckTie");
            go.transform.SetParent(chest, false);
            go.AddComponent<NeckTie>().Build(body, chest, neck);
        }

        void Build(Animator body, Transform chest, Transform neck)
        {
            var root = body.transform;
            float k = Mathf.Abs(root.lossyScale.y) * (body.humanScale > 0.01f ? body.humanScale : 1f);   // ~ hips height in metres
            k /= 1.0f;                                                                                       // adult Mixamo hips ~1.0 m
            Vector3 fwd = root.forward, up = root.up, right = root.right;
            // collar point: just in front of the neck base, a little below it
            float chestDepth = 0.11f * k;
            Vector3 top = neck.position - up * (0.05f * k) + fwd * chestDepth;
            transform.position = top;
            transform.rotation = Quaternion.LookRotation(fwd, up);
            int rows = 10, cols = 3;
            float len = Length * k;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= rows; i++)
            {
                float a = i / (float)rows;
                float w = Mathf.Lerp(KnotW, BladeW, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.7f, a))) * k;
                if (a > 0.92f) w *= Mathf.Lerp(1f, 0.15f, (a - 0.92f) / 0.08f);   // pointed tip
                for (int j = 0; j <= cols; j++)
                {
                    float b = j / (float)cols - 0.5f;
                    var p = transform.InverseTransformPoint(top - up * (len * a) + right * (b * w) + fwd * (0.008f * k * Mathf.Sin(a * 3.1f)));
                    v.Add(p); uv.Add(new Vector2(j / (float)cols, 1f - a));
                    if (i < rows && j < cols) { int c = i * (cols + 1) + j, n = c + cols + 1; t.AddRange(new[] { c, n, c + 1, c + 1, n, n + 1 }); }
                }
            }
            // double sided: a back copy with flipped winding
            int vc = v.Count; var tb = new List<int>(t);
            for (int i = 0; i < t.Count; i += 3) { tb.Add(t[i] + vc); tb.Add(t[i + 2] + vc); tb.Add(t[i + 1] + vc); }
            v.AddRange(new List<Vector3>(v)); uv.AddRange(new List<Vector2>(uv));
            var mesh = new Mesh { name = "NeckTie" };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tb, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var smr = gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            var sh = Shader.Find("GSHero/Readable") ?? Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(sh) { name = "NeckTie" };
            mat.SetColor("_BaseColor", TieColor);
            if (mat.HasProperty("_HatAmount")) mat.SetFloat("_HatAmount", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            smr.sharedMaterial = mat;
            smr.updateWhenOffscreen = true;
            var cloth = gameObject.AddComponent<Cloth>();
            var coeff = new ClothSkinningCoefficient[mesh.vertexCount];
            for (int i = 0; i < coeff.Length; i++)
            {
                int row = (i % vc) / (cols + 1);
                coeff[i].maxDistance = row <= 1 ? 0f : Mathf.Lerp(0.02f, 0.35f, row / (float)rows) * k;   // knot pinned
                coeff[i].collisionSphereDistance = 0f;
            }
            cloth.coefficients = coeff;
            cloth.stretchingStiffness = 0.95f; cloth.bendingStiffness = 0.6f;
            cloth.damping = 0.25f; cloth.useGravity = true;
            cloth.worldVelocityScale = 0.35f; cloth.worldAccelerationScale = 0.6f;
            // the chest it lies on
            // v2 (Kido "찍을 때 왜 캐릭터가 공중으로 계속 뛰어올라"): a solid capsule on the chest was stood on by the player's own
            // controller and lifted him; the cloth collider now lives on its own trigger child (cloth still collides with it)
            var cgo = new GameObject("NeckTieChest"); cgo.transform.SetParent(chest, false);
            var col = cgo.AddComponent<CapsuleCollider>();
            col.isTrigger = true; col.direction = 1;
            col.center = chest.InverseTransformPoint(chest.position + fwd * (0.0f) - up * (0.12f * k));
            col.radius = 0.13f * k / Mathf.Max(1e-4f, chest.lossyScale.x); col.height = 0.45f * k / Mathf.Max(1e-4f, chest.lossyScale.y);
            cloth.capsuleColliders = new[] { col };
            Debug.Log($"[RoomRun] NeckTie on {body.name}: length {len:0.000} m, k {k:0.000}");
        }
    }
}
