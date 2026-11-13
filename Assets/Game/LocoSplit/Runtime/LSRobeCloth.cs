// Long Joseon-style robe (쾌자/도포 silhouette) simulated with Unity Cloth. The part above the waist is pinned to the
// spine/hips; below the waist it hangs free in three panels (side and back slits) and is pushed out by capsule
// colliders on the thighs and shins, so the legs can't poke through.
using System.Collections.Generic;
using UnityEngine;

namespace LocoSplit
{
    public class LSRobeCloth : MonoBehaviour
    {
        public Animator anim;
        public Material material;
        public Material sashMaterial;

        [Header("Shape (metres, character space)")]
        public float topAboveHips = 0.36f;   // chest line
        public float hemHeight = 0.28f;      // from the ground (착호갑사-ish: mid shin)
        public float chestRadius = 0.165f;
        public float waistRadius = 0.16f;
        public float hemRadius = 0.30f;
        public float depthScale = 0.78f;     // front-back flatter than side-side
        public float slitFromWaist = 0.12f;  // slits start this far below the waist
        public float slitGapDeg = 4f;

        [Header("Cloth")]
        public float maxSwing = 0.45f;
        public float thighRadius = 0.095f, shinRadius = 0.075f;
        public float stiffness = 0.9f, bending = 0.15f, damping = 0.12f;

        SkinnedMeshRenderer smr;
        Cloth cloth;
        public Cloth Cloth => cloth;

        void Start()
        {
            // A character that brings its own skirt mesh (the rigged 착호갑사) gets Cloth on that instead.
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>())
                if (r.name.Contains("Skirt")) { BuildOnExisting(r); return; }
            Build();
        }

        public void BuildOnExisting(SkinnedMeshRenderer r)
        {
            smr = r;
            var mesh = r.sharedMesh;
            var caps = MakeLegColliders();
            cloth = r.gameObject.AddComponent<Cloth>();
            cloth.capsuleColliders = caps.ToArray();
            cloth.useGravity = true;
            cloth.worldVelocityScale = 0.4f;
            cloth.worldAccelerationScale = 0.7f;
            cloth.friction = 0.3f;
            cloth.clothSolverFrequency = 180;
            cloth.useTethers = true;
            // swing limit grows with distance below the skirt's top ring (character-space height)
            var cv = cloth.vertices;
            float top = float.MinValue, bottom = float.MaxValue;
            var h = new float[cv.Length];
            for (int i = 0; i < cv.Length; i++)
            {
                h[i] = transform.InverseTransformPoint(r.transform.TransformPoint(cv[i])).y;
                top = Mathf.Max(top, h[i]); bottom = Mathf.Min(bottom, h[i]);
            }
            maxDist = new float[cv.Length];
            for (int i = 0; i < cv.Length; i++)
            {
                float below = Mathf.InverseLerp(top - 0.02f, bottom, h[i]);
                maxDist[i] = below <= 0f ? 0f : Mathf.Pow(below, 0.8f);
            }
            ApplyClothSettings();
        }

        public void SetVisible(bool on)
        {
            if (smr) smr.enabled = on;
            if (cloth) cloth.enabled = on;
        }

        public void ApplyClothSettings()
        {
            if (!cloth) return;
            cloth.stretchingStiffness = stiffness;
            cloth.bendingStiffness = bending;
            cloth.damping = damping;
            var co = cloth.coefficients;
            for (int i = 0; i < co.Length; i++)
                co[i].maxDistance = maxDist[i] * maxSwing;
            cloth.coefficients = co;
        }

        float[] maxDist;

        List<CapsuleCollider> MakeLegColliders()
        {
            var caps = new List<CapsuleCollider>();
            void Cap(HumanBodyBones a, HumanBodyBones b, float r)
            {
                var ta = anim.GetBoneTransform(a); var tb = anim.GetBoneTransform(b);
                var holder = new GameObject("ClothCol_" + a);
                // 11-13: Ignore Raycast layer. On Default, RoomRun's ground raycast landed on these leg capsules and
                // the player popped up and sank back while moving; the character's own controller must not hit them either.
                holder.layer = 2;
                holder.transform.SetParent(ta, false);
                holder.transform.rotation = Quaternion.FromToRotation(Vector3.up, tb.position - ta.position);
                var c = holder.AddComponent<CapsuleCollider>();
                float len = Vector3.Distance(ta.position, tb.position) / Mathf.Max(0.0001f, holder.transform.lossyScale.y);
                c.direction = 1; c.radius = r / Mathf.Max(0.0001f, holder.transform.lossyScale.x);
                c.height = len + c.radius * 2f; c.center = new Vector3(0, len * 0.5f, 0);
                caps.Add(c);
                var owner = GetComponentInParent<CharacterController>();
                if (owner) Physics.IgnoreCollision(owner, c);
            }
            Cap(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, thighRadius);
            Cap(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, thighRadius);
            Cap(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, shinRadius);
            Cap(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, shinRadius);
            return caps;
        }

        public void Build()
        {
            var src = GetComponentInChildren<SkinnedMeshRenderer>();
            var root = transform;
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            var spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            var chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            var upper = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? chest;
            var bones = new[] { hips, spine, chest, upper };
            float hipY = root.InverseTransformPoint(hips.position).y;

            var go = new GameObject("RobeCloth");
            go.transform.SetParent(src.transform.parent, false);
            smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMaterial = material;
            smr.updateWhenOffscreen = true;
            var rootToRobe = go.transform.worldToLocalMatrix * root.localToWorldMatrix;

            // Columns: three panels between slits at 90 (left), 180 (back), 270 (right). Front (0) is closed.
            float[] cuts = { 90f, 180f, 270f };
            int perPanelCols = 12;
            float top = hipY + topAboveHips, waist = hipY + 0.02f, hem = hemHeight;
            float slitY = waist - slitFromWaist;
            int rowsTop = 5, rowsBottom = 14;

            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var bw = new List<BoneWeight>();
            var tris = new List<int>(); var md = new List<float>();
            for (int p = 0; p < 3; p++)
            {
                // front panel 270 -> 450 (through 0), then left-back 90 -> 180, right-back 180 -> 270
                float a0 = p == 0 ? cuts[2] : cuts[p - 1];
                float a1 = p == 0 ? cuts[0] + 360f : cuts[p];
                int baseIdx = verts.Count;
                int rows = rowsTop + rowsBottom;
                for (int r = 0; r <= rows; r++)
                {
                    float y = r <= rowsTop ? Mathf.Lerp(top, waist, r / (float)rowsTop)
                                           : Mathf.Lerp(waist, hem, (r - rowsTop) / (float)rowsBottom);
                    float rad = y >= waist ? Mathf.Lerp(waistRadius, chestRadius, Mathf.InverseLerp(waist, top, y))
                                           : Mathf.Lerp(waistRadius, hemRadius, Mathf.Pow(Mathf.InverseLerp(waist, hem, y), 1.3f));
                    bool open = y < slitY;
                    float gap = open ? slitGapDeg * 0.5f * Mathf.InverseLerp(slitY, slitY - 0.1f, y) : 0f;
                    for (int c = 0; c <= perPanelCols; c++)
                    {
                        float a = Mathf.Lerp(a0 + gap, a1 - gap, c / (float)perPanelCols) * Mathf.Deg2Rad;
                        var p3 = new Vector3(Mathf.Sin(a) * rad, y, Mathf.Cos(a) * rad * depthScale);
                        verts.Add(rootToRobe.MultiplyPoint3x4(p3));
                        uvs.Add(new Vector2((p + c / (float)perPanelCols) / 3f, Mathf.InverseLerp(hem, top, y)));
                        // anchors: chest rows to the upper spine, waist to hips
                        float t = Mathf.InverseLerp(waist, top, y);
                        var w = new BoneWeight();
                        if (t > 0.66f) { w.boneIndex0 = 3; w.weight0 = 1; }
                        else if (t > 0.33f) { w.boneIndex0 = 2; w.weight0 = 0.6f; w.boneIndex1 = 1; w.weight1 = 0.4f; }
                        else if (t > 0f) { w.boneIndex0 = 1; w.weight0 = 0.5f; w.boneIndex1 = 0; w.weight1 = 0.5f; }
                        else { w.boneIndex0 = 0; w.weight0 = 1; }
                        bw.Add(w);
                        float below = Mathf.InverseLerp(waist, hem, y);   // 0 at waist, 1 at hem
                        md.Add(y >= waist ? 0f : Mathf.Pow(below, 0.8f));
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

            var mesh = new Mesh { name = "RobeClothMesh" };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.boneWeights = bw.ToArray();
            var bind = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) bind[i] = bones[i].worldToLocalMatrix * go.transform.localToWorldMatrix;
            mesh.bindposes = bind;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            smr.bones = bones; smr.rootBone = hips; smr.sharedMesh = mesh;

            // Sash at the waist (rigid on the hips).
            if (sashMaterial)
            {
                var sash = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(sash.GetComponent<Collider>());
                sash.name = "Sash";
                sash.transform.SetParent(hips, true);
                sash.transform.position = root.TransformPoint(new Vector3(0, waist + 0.01f, 0));
                sash.transform.rotation = root.rotation;
                sash.transform.localScale = new Vector3(waistRadius * 2.15f, 0.035f, waistRadius * 2.15f * depthScale);
                sash.GetComponent<Renderer>().sharedMaterial = sashMaterial;
            }

            var caps = MakeLegColliders();

            cloth = go.AddComponent<Cloth>();
            cloth.capsuleColliders = caps.ToArray();
            cloth.useGravity = true;
            cloth.worldVelocityScale = 0.4f;
            cloth.worldAccelerationScale = 0.7f;
            cloth.friction = 0.3f;
            cloth.clothSolverFrequency = 180;
            cloth.useTethers = true;

            // Cloth welds vertices at the same position, so its coefficient array can be shorter than the mesh:
            // map each cloth vertex to the nearest mesh vertex for its swing limit.
            var cv = cloth.vertices;
            maxDist = new float[cv.Length];
            var meshVerts = mesh.vertices;
            for (int i = 0; i < cv.Length; i++)
            {
                int best = 0; float bd = float.MaxValue;
                for (int j = 0; j < meshVerts.Length; j++)
                {
                    float d = (meshVerts[j] - cv[i]).sqrMagnitude;
                    if (d < bd) { bd = d; best = j; }
                }
                maxDist[i] = md[best];
            }
            ApplyClothSettings();
        }
    }
}
