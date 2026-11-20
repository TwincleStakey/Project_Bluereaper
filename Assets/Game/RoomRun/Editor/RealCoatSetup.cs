// 11-20: the downloaded long coat (ThirdParty/LongLeatherCoat, CC BY - see CREDITS.md) skinned to Joe INSIDE Unity.
// JoeCoatT.fbx is the coat already put into Joe's T rest shape in Blender (static mesh). Bake: every coat vertex takes the
// bone weights of the nearest Joe body vertex (sleeves follow the arms, the torso the spine); below the hips it is
// weighted to the hips only (the cloth swings it). The mesh is written in Joe body-mesh space with Joe body's own
// bindposes, so it skins on the live Joe bones exactly like Joe's clothes (Blender-exported bindposes used other bone axes).
// rr_queue: call RoomRun.RealCoatSetup.Import  (texture settings + bake -> JoeCoat_Skinned.asset)
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RoomRun
{
    public static class RealCoatSetup
    {
        const string Dir = "Assets/Game/RoomRun/ThirdParty/LongLeatherCoat/";
        const string JoeFbx = "Assets/Game/RoomRun/ThirdParty/MixamoJoe/Joe.fbx";
        public const string Baked = Dir + "JoeCoat_Skinned.asset";

        public static void Import()
        {
            foreach (var n in new[] { "Coat_Base_color", "Coat_Normal_OpenGL" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(Dir + "Textures/" + n + ".png");
                if (!ti) { Debug.LogWarning("[RealCoat] missing " + n); continue; }
                ti.textureType = n.Contains("Normal") ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.maxTextureSize = 2048;
                ti.SaveAndReimport();
            }
            var mi = (ModelImporter)AssetImporter.GetAtPath(Dir + "JoeCoatT.fbx");
            if (mi) { mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importAnimation = false; mi.isReadable = true; mi.SaveAndReimport(); }
            Bake();
        }

        public static void Bake()
        {
            var joe = AssetDatabase.LoadAssetAtPath<GameObject>(JoeFbx);
            var coatGo = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "JoeCoatT.fbx");
            if (!joe || !coatGo) { Debug.LogError("[RealCoat] bake inputs missing"); return; }
            SkinnedMeshRenderer body = null;
            foreach (var s in joe.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (s.name.EndsWith("Body")) body = s;
            var mf = coatGo.GetComponentInChildren<MeshFilter>(true);
            if (!body || !mf) { Debug.LogError("[RealCoat] no body / coat mesh"); return; }
            var bm = body.sharedMesh; var bv = bm.vertices; var bw = bm.boneWeights; var bind = bm.bindposes;
            var cm = mf.sharedMesh;
            // coat vertices -> body mesh space (both FBX roots at the prefab origin); try 0 and 180 deg about Y, keep the closer fit
            Matrix4x4 coatToWorld = mf.transform.localToWorldMatrix, worldToBody = body.transform.worldToLocalMatrix;
            var cv0 = cm.vertices;
            Vector3[] Best()
            {
                Vector3[] bestV = null; float bestD = float.MaxValue;
                foreach (var yaw in new[] { 0f, 180f })
                {
                    var R = Matrix4x4.Rotate(Quaternion.Euler(0, yaw, 0));
                    var vs = new Vector3[cv0.Length];
                    for (int i = 0; i < vs.Length; i++) vs[i] = worldToBody.MultiplyPoint3x4(R.MultiplyPoint3x4(coatToWorld.MultiplyPoint3x4(cv0[i])));
                    float d = 0f; int n = 0;
                    for (int i = 0; i < vs.Length; i += 97) { d += Nearest(bv, vs[i], out _); n++; }
                    d /= Mathf.Max(1, n);
                    Debug.Log($"[RealCoat] fit yaw {yaw}: mean nearest {d:0.000}");
                    if (d < bestD) { bestD = d; bestV = vs; }
                }
                return bestV;
            }
            var cv = Best();
            // hips height in body space
            int hipsIdx = -1;
            for (int i = 0; i < body.bones.Length; i++) if (body.bones[i] && body.bones[i].name.EndsWith("Hips")) hipsIdx = i;
            float hipsY = hipsIdx >= 0 ? bind[hipsIdx].inverse.MultiplyPoint3x4(Vector3.zero).y : 0f;
            float U = bm.bounds.size.y / 1.78f;   // body mesh units (Mixamo bodies are often in cm)
            var grid = new Dictionary<Vector3Int, List<int>>();
            float C = 0.06f * U;
            for (int i = 0; i < bv.Length; i++)
            {
                var k = new Vector3Int(Mathf.FloorToInt(bv[i].x / C), Mathf.FloorToInt(bv[i].y / C), Mathf.FloorToInt(bv[i].z / C));
                if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                l.Add(i);
            }
            var outW = new BoneWeight[cv.Length];
            int skirt = 0;
            for (int i = 0; i < cv.Length; i++)
            {
                if (hipsIdx >= 0 && cv[i].y < hipsY - 0.02f * U && Mathf.Abs(cv[i].x) < 0.3f * U)
                { outW[i] = new BoneWeight { boneIndex0 = hipsIdx, weight0 = 1f }; skirt++; continue; }
                int best = -1; float bd = float.MaxValue;
                var c = new Vector3Int(Mathf.FloorToInt(cv[i].x / C), Mathf.FloorToInt(cv[i].y / C), Mathf.FloorToInt(cv[i].z / C));
                for (int r = 1; r <= 4 && best < 0; r++)
                    for (int x = -r; x <= r; x++) for (int y = -r; y <= r; y++) for (int z = -r; z <= r; z++)
                        if (grid.TryGetValue(new Vector3Int(c.x + x, c.y + y, c.z + z), out var l))
                            foreach (var j in l) { float d = (bv[j] - cv[i]).sqrMagnitude; if (d < bd) { bd = d; best = j; } }
                if (best < 0) Nearest(bv, cv[i], out best);
                outW[i] = bw[best];
            }
            var mesh = Object.Instantiate(cm);
            mesh.name = "JoeCoat_Skinned";
            mesh.vertices = cv;
            mesh.boneWeights = outW;
            mesh.bindposes = bind;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.DeleteAsset(Baked);
            AssetDatabase.CreateAsset(mesh, Baked);
            AssetDatabase.SaveAssets();
            Debug.Log($"[RealCoat] units {U:0.###} hipsY {hipsY:0.###}");
            Debug.Log($"[RealCoat] baked {cv.Length} verts ({skirt} skirt on hips), {bind.Length} bones -> {Baked}");
        }

        static float Nearest(Vector3[] pts, Vector3 p, out int idx)
        {
            idx = 0; float bd = float.MaxValue;
            for (int j = 0; j < pts.Length; j++) { float d = (pts[j] - p).sqrMagnitude; if (d < bd) { bd = d; idx = j; } }
            return Mathf.Sqrt(bd);
        }
    }
}
