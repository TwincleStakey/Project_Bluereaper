// Puts the greatsword in the right hand and builds a long robe (skirt) skinned to the hips and legs at runtime,
// so both can be toggled and tuned from the dev panel.
using System.Collections.Generic;
using UnityEngine;

namespace LocoSplit
{
    public class LSRig : MonoBehaviour
    {
        public Animator anim;
        public GameObject swordPrefab;
        public Vector3 swordPos = new Vector3(0.0f, 0.08f, 0.02f);
        public Vector3 swordEuler = new Vector3(0, 0, 0);
        public float swordScale = 1f;

        [Header("Hat")]
        public GameObject hatPrefab;
        public Vector3 hatPos = new Vector3(0, 0.1f, 0.0f);
        public Vector3 hatEuler = Vector3.zero;
        public float hatScale = 1f;
        Transform hat;

        [Header("Robe")]
        public Material robeMaterial;
        public bool robeOn = false;   // old skinned cone; LSRobeCloth replaces it
        public float robeTop = 0.0f;        // relative to hips bone height
        public float robeLength = 0.95f;    // down from the top
        public float robeTopRadius = 0.17f;
        public float robeHemRadius = 0.42f;
        [Range(0, 1)] public float legFollow = 0.55f; // 0 = all hips (stiff bell), 1 = follows the legs fully

        Transform sword;
        SkinnedMeshRenderer robe;
        float builtTop, builtLen, builtTopR, builtHemR, builtFollow;

        void Start()
        {
            if (!anim) anim = GetComponentInChildren<Animator>();
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (swordPrefab && hand)
            {
                sword = Instantiate(swordPrefab, hand).transform;
                sword.name = "Greatsword";
            }
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            if (hatPrefab && head) { hat = Instantiate(hatPrefab, head).transform; hat.name = "Hat"; }
            if (robeOn) BuildRobe();
        }

        public bool autoGrip = true;
        public float gripAlongFingers = 0.07f;   // palm centre from the wrist bone
        int gripFrames;
        public void Regrip() { autoGrip = true; gripFrames = 15; }

        void LateUpdate()
        {
            if (!autoGrip || !sword) return;
            if (++gripFrames < 20) return;   // let the idle pose settle
            autoGrip = false;
            var r = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var l = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            Vector3 dir = r.InverseTransformDirection((r.position - l.position).normalized);
            // keep the blade's flat facing the hand's palm normal (local x) as well as possible
            swordEuler = Quaternion.LookRotation(dir, Vector3.right).eulerAngles;
            swordPos = new Vector3(0, gripAlongFingers, 0);
        }

        void Update()
        {
            if (sword)
            {
                sword.localPosition = swordPos;
                sword.localRotation = Quaternion.Euler(swordEuler);
                sword.localScale = Vector3.one * swordScale;
            }
            if (hat)
            {
                hat.localPosition = hatPos;
                hat.localRotation = Quaternion.Euler(hatEuler);
                hat.localScale = Vector3.one * hatScale;
            }
            if (robe) robe.enabled = robeOn;
            if (robeOn && (builtTop != robeTop || builtLen != robeLength || builtTopR != robeTopRadius ||
                           builtHemR != robeHemRadius || builtFollow != legFollow)) BuildRobe();
        }

        // A cone of rings from the waist to the hem. Weights: the top ring is all hips; lower down each vertex
        // goes to the thigh on its side (front/back centre is shared), and below the knee to the shin.
        public void BuildRobe()
        {
            builtTop = robeTop; builtLen = robeLength; builtTopR = robeTopRadius; builtHemR = robeHemRadius; builtFollow = legFollow;
            var smrSrc = GetComponentInChildren<SkinnedMeshRenderer>();
            if (!smrSrc || !robeMaterial) return;

            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            var lUp = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var rUp = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var lLo = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var rLo = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            var bones = new[] { hips, lUp, rUp, lLo, rLo };

            if (!robe)
            {
                var go = new GameObject("Robe");
                go.transform.SetParent(smrSrc.transform.parent, false);
                robe = go.AddComponent<SkinnedMeshRenderer>();
                robe.sharedMaterial = robeMaterial;
                robe.updateWhenOffscreen = true;
            }

            // Build in the character root's space at bind time (current pose is the T/idle pose on Start;
            // rebuilds use the stored bind matrices so the pose at rebuild time does not matter).
            Transform root = transform;
            if (bindPoses == null)
            {
                hipY = root.InverseTransformPoint(hips.position).y;
                kneeY = (root.InverseTransformPoint(lLo.position).y + root.InverseTransformPoint(rLo.position).y) * 0.5f;
                bindPoses = new Matrix4x4[bones.Length];
                for (int i = 0; i < bones.Length; i++) bindPoses[i] = bones[i].worldToLocalMatrix * robe.transform.localToWorldMatrix;
                bindRootToRobe = robe.transform.worldToLocalMatrix * root.localToWorldMatrix;
            }

            const int seg = 32, rings = 12;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>();
            var bw = new List<BoneWeight>(); var tris = new List<int>();
            float top = hipY + robeTop;
            for (int r = 0; r <= rings; r++)
            {
                float v = r / (float)rings;
                float y = top - v * robeLength;
                float rad = Mathf.Lerp(robeTopRadius, robeHemRadius, Mathf.SmoothStep(0, 1, v * 0.8f + v * v * 0.2f));
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                    // flatten a little front/back so it reads like a robe, not a bell
                    Vector3 p = new Vector3(dir.x * rad, y, dir.z * rad * 0.85f);
                    verts.Add(bindRootToRobe.MultiplyPoint3x4(p));
                    norms.Add(bindRootToRobe.MultiplyVector(dir).normalized);
                    uvs.Add(new Vector2(s / (float)seg, 1 - v));

                    // side: +x is the character's left in Mixamo (they face +z, left hand on +x)
                    float side = Mathf.Clamp01(0.5f + dir.x * 0.9f);        // 1 = left leg
                    float legAmt = Mathf.Clamp01((top - y) / Mathf.Max(0.05f, top - kneeY * 0.6f)) * legFollow;
                    float wl = legAmt * side, wr = legAmt * (1 - side), wh = 1 - legAmt;
                    var w = new BoneWeight
                    {
                        boneIndex0 = 0, weight0 = wh,
                        boneIndex1 = 1, weight1 = wl,
                        boneIndex2 = 2, weight2 = wr,
                    };
                    float sum = w.weight0 + w.weight1 + w.weight2;
                    w.weight0 /= sum; w.weight1 /= sum; w.weight2 /= sum;
                    bw.Add(w);
                }
            }
            int row = seg + 1;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int i0 = r * row + s, i1 = i0 + 1, i2 = i0 + row, i3 = i2 + 1;
                    tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    tris.Add(i1); tris.Add(i2); tris.Add(i3);
                }

            var mesh = new Mesh { name = "RobeMesh" };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.boneWeights = bw.ToArray();
            mesh.bindposes = bindPoses;
            mesh.RecalculateBounds();
            robe.bones = bones;
            robe.rootBone = hips;
            robe.sharedMesh = mesh;
        }

        Matrix4x4[] bindPoses;
        Matrix4x4 bindRootToRobe;
        float hipY, kneeY;
    }
}
