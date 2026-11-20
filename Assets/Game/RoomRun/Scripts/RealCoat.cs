// 11-20 Kido: "실제 롱코트를 받아서 입혀" - the downloaded Sketchfab "Long Leather Coat" (CC BY, crow; see
// ThirdParty/LongLeatherCoat/CREDITS.md) worn by the suit body. The coat FBX was bound to Joe's skeleton in Blender
// (weights transferred from Joe's body, skirt weighted to the hips); here its mesh is re-skinned onto the live Joe bones by
// name, and the skirt below the waist gets Unity Cloth (pinned at the waist, freer toward the hem, leg capsules) so it
// swings. It replaces the hand-built CoatTail panel. The SuitBody hero-look pass re-materials it like the suit.
// Editor-only asset load (like SuitBody): RoomRun is a look test here.
using System.Collections.Generic;
using UnityEngine;

namespace RoomRun
{
    public sealed class RealCoat : MonoBehaviour
    {
        public static bool Use = false;  // 11-20: OFF until the baked coat is verified (Kido: lower clothes looked broken). token realcoat=1 to test
        const string Fbx = "Assets/Game/RoomRun/ThirdParty/LongLeatherCoat/JoeCoat.fbx";
        const string TexDir = "Assets/Game/RoomRun/ThirdParty/LongLeatherCoat/Textures/";
        public Animator anim;
        public float maxSwing = 0.32f, damping = 0.4f, velocityScale = 0.25f, accelerationScale = 0.3f;
        GameObject go; readonly List<GameObject> cols = new List<GameObject>();

        void Start() { if (!anim) anim = GetComponent<Animator>(); Build(); }

        void Build()
        {
            Mesh mesh = null;
#if UNITY_EDITOR
            mesh = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Game/RoomRun/ThirdParty/LongLeatherCoat/JoeCoat_Skinned.asset");
#endif
            if (!mesh || !anim) { Debug.LogWarning("[RealCoat] baked coat mesh missing (call RoomRun.RealCoatSetup.Import)"); return; }
            // the coat was baked with Joe body's bindposes: use the live body's bone array as is
            SkinnedMeshRenderer bodySmr = null;
            foreach (var s in anim.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (s.name.EndsWith("Body")) bodySmr = s;
            if (!bodySmr) { Debug.LogWarning("[RealCoat] no Joe body renderer"); return; }
            var bones = bodySmr.bones; int miss = 0;
            go = new GameObject("RealCoat");
            go.transform.SetParent(bodySmr.transform.parent, false);
            go.transform.localPosition = bodySmr.transform.localPosition; go.transform.localRotation = bodySmr.transform.localRotation; go.transform.localScale = bodySmr.transform.localScale;
            go.layer = anim.gameObject.layer;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bodySmr.rootBone;
            smr.updateWhenOffscreen = true;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "RealCoat" };
#if UNITY_EDITOR
            var bc = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "Coat_Base_color.png");
            var nm = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "Coat_Normal_OpenGL.png");
            if (bc) mat.SetTexture("_BaseMap", bc);
            if (nm) { mat.SetTexture("_BumpMap", nm); mat.EnableKeyword("_NORMALMAP"); }
#endif
            mat.SetColor("_BaseColor", Tint);
            mat.SetFloat("_Smoothness", 0.3f);
            mat.SetFloat("_Cull", 0f);
            smr.sharedMaterial = mat;
            Debug.Log($"[RealCoat] coat on {anim.name}: {bones.Length} bones ({miss} unmatched), {smr.sharedMesh.vertexCount} verts");
            if (ClothOn) AddCloth(smr);
        }
        public static Color Tint = new Color(0.55f, 0.58f, 0.75f);   // pulls the black leather toward the suit's navy
        public static bool ClothOn = true;

        void AddCloth(SkinnedMeshRenderer smr)
        {
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            float hipY = anim.transform.InverseTransformPoint(hips.position).y;
            var caps = new List<CapsuleCollider>();
            void Cap(HumanBodyBones a, HumanBodyBones b, float r)
            {
                var ta = anim.GetBoneTransform(a); var tb = anim.GetBoneTransform(b);
                if (!ta || !tb) return;
                var h = new GameObject("RealCoatCol_" + a); h.layer = 2;
                h.transform.SetParent(ta, false);
                h.transform.rotation = Quaternion.FromToRotation(Vector3.up, tb.position - ta.position);
                var c = h.AddComponent<CapsuleCollider>(); c.isTrigger = true;
                float len = Vector3.Distance(ta.position, tb.position) / Mathf.Max(1e-4f, h.transform.lossyScale.y);
                c.direction = 1; c.radius = r / Mathf.Max(1e-4f, h.transform.lossyScale.x);
                c.height = len + c.radius * 2f; c.center = new Vector3(0, len * 0.5f, 0);
                caps.Add(c); cols.Add(h);
            }
            float k = Mathf.Clamp(hips.position.y - anim.transform.position.y, 0.5f, 2f) / 0.95f;
            Cap(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.1f * k);
            Cap(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.1f * k);
            Cap(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.08f * k);
            Cap(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.08f * k);
            var cloth = go.AddComponent<Cloth>();
            cloth.capsuleColliders = caps.ToArray();
            cloth.useGravity = true; cloth.clothSolverFrequency = 120; cloth.useTethers = true;
            cloth.stretchingStiffness = 1f; cloth.bendingStiffness = 0.85f; cloth.damping = damping;
            cloth.worldVelocityScale = velocityScale; cloth.worldAccelerationScale = accelerationScale; cloth.friction = 0.6f;
            // swing limit from the height of each cloth vertex in the character's space: 0 at the waist, maxSwing at the hem
            var cv = cloth.vertices; var co = cloth.coefficients;
            float lo = float.MaxValue;
            var hy = new float[cv.Length];
            for (int i = 0; i < cv.Length; i++)
            {
                hy[i] = anim.transform.InverseTransformPoint(go.transform.TransformPoint(cv[i])).y;
                lo = Mathf.Min(lo, hy[i]);
            }
            float waist = hipY - 0.03f * k;
            for (int i = 0; i < co.Length; i++)
            {
                float u = Mathf.InverseLerp(waist, lo, hy[i]);
                co[i].maxDistance = hy[i] >= waist ? 0f : maxSwing * k * Mathf.Pow(u, 0.9f);
            }
            cloth.coefficients = co;
        }

        int dbg;
        void LateUpdate()
        {
            if (dbg++ != 30 || !go) return;
            var r = go.GetComponent<SkinnedMeshRenderer>();
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            var bm = new Mesh(); r.BakeMesh(bm, true);
            var vs = bm.vertices; var chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            int up = 0; Vector3 sum = Vector3.zero; float maxY = float.MinValue;
            foreach (var v in vs) { var w = go.transform.TransformPoint(v); maxY = Mathf.Max(maxY, w.y); if (w.y > hips.position.y + 0.25f) { up++; sum += w; } }
            Debug.Log($"[RealCoat] baked: {vs.Length} verts, {up} above hips+.25, mean {(up > 0 ? sum / up : Vector3.zero)}, maxY {maxY}, chest {chest.position}, neck {anim.GetBoneTransform(HumanBodyBones.Neck).position}");
            Debug.Log($"[RealCoat] frame30 bounds c={r.bounds.center} size={r.bounds.size} hips={hips.position} enabled={r.enabled} mat={r.sharedMaterial.shader.name} scaleHips={hips.lossyScale}");
        }

        void OnDestroy()
        {
            if (go) Destroy(go);
            foreach (var c in cols) if (c) Destroy(c);
        }
    }
}
