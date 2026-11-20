// 11-19 Kido: "the body looks stiff next to the weapon - would a long coat with cloth sim help?" A black long coat skirt
// (waist to ankle) simulated with Unity Cloth, added to the player at runtime. Ported from LocoSplit.LSRobeCloth (a
// different assembly) and made lighter so it flies out on the whip's turns: open front, back vent, side slits, leg
// capsules so the legs never poke through. Toggle RR.Coat (dev panel 플레이어 > 롱코트) or shots token coat=1/0.
using System.Collections.Generic;
using UnityEngine;

namespace RoomRun
{
    public sealed class CoatTail : MonoBehaviour
    {
        public const string Key = "RR.Coat";
        public static bool? Force;                         // shots token coat=
        public static bool On => Force ?? PlayerPrefs.GetInt(Key, 0) != 0;

        public Animator anim;
        public float topAboveHips = 0.12f;   // pinned band starts here (under the jacket line)
        public float hemHeight = 0.1f;       // ankle
        public float waistRadius = 0.17f, topRadius = 0.18f, hemRadius = 0.27f;   // v3 (ref image 13:06): straight drop, not a flared skirt (was 0.36)
        public float depthScale = 0.8f;
        public float slitFromWaist = 0.08f, slitGapDeg = 6f;
        // v2 (Kido ref 13:06, modern 저승사자: black suit + ankle-length black coat, thin white piping on the front edges):
        // heavier so it swings as one sheet instead of strips, white trim on the front edges and the hem
        public float maxSwing = 0.6f;
        public float stiffness = 1f, bending = 0.7f, damping = 0.25f;
        public float velocityScale = 0.45f, accelerationScale = 0.55f, friction = 0.5f;
        public bool trim = true;
        public float thighRadius = 0.1f, shinRadius = 0.08f;   // v4: was .09/.07
        public Color color = new Color(0.035f, 0.035f, 0.04f);

        SkinnedMeshRenderer smr; Cloth cloth; GameObject go; readonly List<GameObject> cols = new List<GameObject>();
        float[] maxDist;

        public static void Sync(Animator a)
        {
            if (!a) return;
            var c = a.GetComponent<CoatTail>();
            if (On && !c) { c = a.gameObject.AddComponent<CoatTail>(); c.anim = a; }
            else if (!On && c) Destroy(c);
        }

        void Start() { if (!anim) anim = GetComponent<Animator>(); if (anim) Build(); }

        void OnDestroy()
        {
            if (go) Destroy(go);
            foreach (var c in cols) if (c) Destroy(c);
        }

        void Build()
        {
            var root = transform;
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            var spine = anim.GetBoneTransform(HumanBodyBones.Spine) ?? hips;
            if (!hips) return;
            var bones = new[] { hips, spine };
            float hipY = root.InverseTransformPoint(hips.position).y;
            var src = GetComponentInChildren<SkinnedMeshRenderer>();

            go = new GameObject("CoatTail");
            go.transform.SetParent(src ? src.transform.parent : root, false);
            smr = go.AddComponent<SkinnedMeshRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(sh) { name = "CoatTail" };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.08f);   // v42 (Kido: less shiny), was .28
            mat.SetFloat("_SpecularHighlights", 0f); mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.SetFloat("_EnvironmentReflections", 0f); mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            mat.SetFloat("_Cull", 0f);                 // both faces: the inside shows when it flies open
            if (trim)
            {
                // u runs once round the coat (0 and 1 = the two front edges), v = 0 at the hem
                const int TW = 512, TH = 64;
                var tex = new Texture2D(TW, TH, TextureFormat.RGBA32, true) { name = "CoatTrim", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[TW * TH];
                for (int y = 0; y < TH; y++)
                    for (int x = 0; x < TW; x++)
                    {
                        float u = (x + 0.5f) / TW, v = (y + 0.5f) / TH;
                        bool edge = u < 0.006f || u > 0.994f || v < 0.025f;
                        px[y * TW + x] = edge ? new Color32(235, 235, 230, 255) : new Color32(255, 255, 255, 255);
                    }
                tex.SetPixels32(px); tex.Apply(true);
                mat.SetTexture("_BaseMap", tex);
                // trim texture is white-on-white; the colour multiplies the body only: use a lit black body via a second texel value
                for (int i = 0; i < px.Length; i++) if (px[i].r == 255) px[i] = new Color32((byte)(color.r * 255), (byte)(color.g * 255), (byte)(color.b * 255), 255);
                tex.SetPixels32(px); tex.Apply(true);
                mat.SetColor("_BaseColor", Color.white);
            }
            smr.sharedMaterial = mat;
            smr.updateWhenOffscreen = true;
            var rootToCoat = go.transform.worldToLocalMatrix * root.localToWorldMatrix;

            // panels between the cuts: front opening (0), right slit, back vent (180), left slit
            float[] cuts = { 0f, 100f, 180f, 260f, 360f };
            int perPanelCols = 9;
            float top = hipY + topAboveHips, waist = hipY + 0.02f, hem = hemHeight;
            float slitY = waist - slitFromWaist;
            int rowsTop = 3, rowsBottom = 16;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var bw = new List<BoneWeight>();
            var tris = new List<int>(); var md = new List<float>();
            int panels = cuts.Length - 1;
            for (int p = 0; p < panels; p++)
            {
                float a0 = cuts[p], a1 = cuts[p + 1];
                bool frontEdge0 = p == 0, frontEdge1 = p == panels - 1;
                int baseIdx = verts.Count, rows = rowsTop + rowsBottom;
                for (int r = 0; r <= rows; r++)
                {
                    float y = r <= rowsTop ? Mathf.Lerp(top, waist, r / (float)rowsTop) : Mathf.Lerp(waist, hem, (r - rowsTop) / (float)rowsBottom);
                    float rad = y >= waist ? Mathf.Lerp(waistRadius, topRadius, Mathf.InverseLerp(waist, top, y))
                                           : Mathf.Lerp(waistRadius, hemRadius, Mathf.Pow(Mathf.InverseLerp(waist, hem, y), 1.2f));
                    // front stays open all the way; side and back slits open below slitY
                    float gapS = y < slitY ? slitGapDeg * 0.5f * Mathf.InverseLerp(slitY, slitY - 0.1f, y) : 0f;
                    float gapF = 1.5f + 3.5f * Mathf.InverseLerp(waist, hem, y);   // v3: front edges nearly meet (was 3+9)
                    for (int c = 0; c <= perPanelCols; c++)
                    {
                        float g0 = frontEdge0 ? gapF : gapS, g1 = frontEdge1 ? gapF : gapS;
                        float a = Mathf.Lerp(a0 + g0, a1 - g1, c / (float)perPanelCols) * Mathf.Deg2Rad;
                        var p3 = new Vector3(Mathf.Sin(a) * rad, y, Mathf.Cos(a) * rad * depthScale);
                        verts.Add(rootToCoat.MultiplyPoint3x4(p3));
                        uvs.Add(new Vector2((p + c / (float)perPanelCols) / panels, Mathf.InverseLerp(hem, top, y)));
                        float t = Mathf.InverseLerp(waist, top, y);
                        var w = new BoneWeight();
                        if (t > 0.5f) { w.boneIndex0 = 1; w.weight0 = 1; }
                        else { w.boneIndex0 = 0; w.weight0 = 1; }
                        bw.Add(w);
                        md.Add(y >= waist ? 0f : Mathf.Pow(Mathf.InverseLerp(waist, hem, y), 0.75f));
                    }
                }
                int row = perPanelCols + 1;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < perPanelCols; c++)
                    {
                        int i0 = baseIdx + r * row + c, i1 = i0 + 1, i2 = i0 + row, i3 = i2 + 1;
                        tris.Add(i0); tris.Add(i2); tris.Add(i1);
                        tris.Add(i1); tris.Add(i2); tris.Add(i3);
                    }
            }
            var mesh = new Mesh { name = "CoatTailMesh" };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.boneWeights = bw.ToArray();
            var bind = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) bind[i] = bones[i].worldToLocalMatrix * go.transform.localToWorldMatrix;
            mesh.bindposes = bind;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            smr.bones = bones; smr.rootBone = hips; smr.sharedMesh = mesh;

            var caps = new List<CapsuleCollider>();
            void Cap(HumanBodyBones a, HumanBodyBones b, float rr)
            {
                var ta = anim.GetBoneTransform(a); var tb = anim.GetBoneTransform(b);
                if (!ta || !tb) return;
                var holder = new GameObject("CoatCol_" + a);
                holder.layer = 2;
                holder.transform.SetParent(ta, false);
                holder.transform.rotation = Quaternion.FromToRotation(Vector3.up, tb.position - ta.position);
                var c = holder.AddComponent<CapsuleCollider>();
                c.isTrigger = true;                           // only the cloth uses it; no physics contacts, no hits
                float len = Vector3.Distance(ta.position, tb.position) / Mathf.Max(0.0001f, holder.transform.lossyScale.y);
                c.direction = 1; c.radius = rr / Mathf.Max(0.0001f, holder.transform.lossyScale.x);
                c.height = len + c.radius * 2f; c.center = new Vector3(0, len * 0.5f, 0);
                caps.Add(c); cols.Add(holder);
            }
            // v4 (Kido 14:42 "옷이 살을 안 뚫게, 특히 다리 팔"): every limb that can reach the skirt pushes it out, radii scaled
            // to the body (hips height / 0.9 m), slightly fatter than the limb so the trousers and sleeves never show through
            float bk = Mathf.Clamp(hipY * root.lossyScale.y / 0.9f, 0.85f, 2f);
            Cap(HumanBodyBones.Hips, HumanBodyBones.Spine, 0.15f * bk);
            Cap(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, thighRadius * bk);
            Cap(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, thighRadius * bk);
            Cap(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, shinRadius * bk);
            Cap(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, shinRadius * bk);
            Cap(HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, 0.055f * bk);
            Cap(HumanBodyBones.RightFoot, HumanBodyBones.RightToes, 0.055f * bk);
            Cap(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.06f * bk);
            Cap(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.06f * bk);
            Cap(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.05f * bk);
            Cap(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.05f * bk);
            Cap(HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal, 0.05f * bk);
            Cap(HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal, 0.05f * bk);

            cloth = go.AddComponent<Cloth>();
            cloth.capsuleColliders = caps.ToArray();
            cloth.useGravity = true;
            cloth.clothSolverFrequency = 180;
            cloth.useTethers = true;
            cloth.stretchingStiffness = stiffness;
            cloth.bendingStiffness = bending;
            cloth.damping = damping;
            cloth.worldVelocityScale = velocityScale;
            cloth.worldAccelerationScale = accelerationScale;
            cloth.friction = friction;
            var cv = cloth.vertices;
            maxDist = new float[cv.Length];
            var mv = mesh.vertices;
            for (int i = 0; i < cv.Length; i++)
            {
                int best = 0; float bd = float.MaxValue;
                for (int j = 0; j < mv.Length; j++) { float d = (mv[j] - cv[i]).sqrMagnitude; if (d < bd) { bd = d; best = j; } }
                maxDist[i] = md[best];
            }
            var co = cloth.coefficients;
            for (int i = 0; i < co.Length; i++) co[i].maxDistance = maxDist[i] * maxSwing;
            cloth.coefficients = co;
        }
    }
}
