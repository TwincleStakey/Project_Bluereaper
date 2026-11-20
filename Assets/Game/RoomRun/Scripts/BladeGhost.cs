// 11-19 blade afterimages (Kido on the V Rising side-by-side: "잔상, 우리는 저런 잔상도 없어"): in the reference the fast frames of a
// swing show one or two faint copies of the blade trailing behind it (motion blur). Here: while the blade turns fast, the
// sword's own meshes are drawn again 1/3 and 2/3 of a frame back along the way it came (pose blended between the last and
// the current frame), pale and see-through. Nothing is drawn while the blade is slow. Draw calls only; no objects, no clips.
using System.Collections.Generic;
using UnityEngine;

namespace RoomRun
{
    [DefaultExecutionOrder(300)]   // after every pose pass (SplitBody 50, ArmStretch 150, eye flames 200)
    public sealed class BladeGhost : MonoBehaviour
    {
        public static bool On = true;                 // shots token ghost=0|1
        public static float MinDegPerFrame = 12f;     // slower than this (per frame) = no afterimage
        public static float Alpha = 0.28f;
        public Transform sword;
        readonly List<(Mesh mesh, Matrix4x4 local)> parts = new List<(Mesh, Matrix4x4)>();
        Material mat; MaterialPropertyBlock block;
        Vector3 prevPos; Quaternion prevRot; bool hasPrev;
        static readonly int ColorId = Shader.PropertyToID("_Color"), BaseColorId = Shader.PropertyToID("_BaseColor");

        public static BladeGhost Create(Transform sword)
        {
            var g = sword.gameObject.GetComponent<BladeGhost>() ?? sword.gameObject.AddComponent<BladeGhost>();
            g.sword = sword; g.Build();
            return g;
        }

        void Build()
        {
            parts.Clear();
            foreach (var mf in sword.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh) parts.Add((mf.sharedMesh, sword.worldToLocalMatrix * mf.transform.localToWorldMatrix));
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (!sh) return;
            mat = new Material(sh) { name = "RR_BladeGhost" };
            // transparent, no depth write
            mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            block = new MaterialPropertyBlock();
        }

        void LateUpdate()
        {
            if (!sword || !mat) return;
            var pos = sword.position; var rot = sword.rotation;
            if (On && hasPrev && Time.deltaTime > 0f)
            {
                float deg = Vector3.Angle(prevRot * Vector3.forward, rot * Vector3.forward);
                float k = Mathf.InverseLerp(MinDegPerFrame, MinDegPerFrame * 3f, deg);
                if (k > 0.01f)
                    for (int i = 1; i <= 2; i++)
                    {
                        float u = 1f - i / 3f;   // 2/3 and 1/3 of the way from the last frame's pose to this one
                        var m = Matrix4x4.TRS(Vector3.Lerp(prevPos, pos, u), Quaternion.Slerp(prevRot, rot, u), sword.lossyScale);
                        var c = new Color(0.78f, 0.92f, 1f, Alpha * k * (i == 1 ? 1f : 0.55f));
                        block.SetColor(ColorId, c); block.SetColor(BaseColorId, c);
                        foreach (var p in parts) Graphics.DrawMesh(p.mesh, m * p.local, mat, sword.gameObject.layer, null, 0, block, false, false);
                    }
            }
            prevPos = pos; prevRot = rot; hasPrev = true;
        }
    }
}
