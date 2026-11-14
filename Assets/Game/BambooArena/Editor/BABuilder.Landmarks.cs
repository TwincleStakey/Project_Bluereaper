// 11-13 redo (Kido picked: B2 floor, 서낭당 landmarks, C night mood; and the Hades 2 foreground): big landmarks just
// outside the arena edge so the open floor gets depth without anything standing in the fight area.
//   north: an old three-storey stone pagoda (폐사지) in a clearing of the bamboo, two stone literati (문인석) before it;
//          a pair of 장승 and 솟대 where the path leaves the clearing;
//   west:  a 서낭당 cairn (a heap of mossy stones) under the bamboo with 솟대 and a 장승;
//   east:  two 문인석 flanking a second, smaller cairn;
//   south (camera side): very tall near-black 장승 / 솟대 silhouettes among the dark bamboo (BambooField.silhouetteMat).
// Models (CC BY, credits in ThirdParty/CREDITS.md): KHS stone pagoda and stone literatus, ththqkf 장승; 솟대 and the
// rubble/rock scans are the 폐촌 field's (AbandonedVillage). No colliders: everything stands outside the arena walls.
using System.Collections.Generic;
using System.Linq;
using BambooArena;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class BABuilder
{
    const string TP = Root + "/ThirdParty";

    static Material ScanMat(string name, float tint = 1f, float smooth = 0.12f)
    {
        var A = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TP}/{name}/BA_{name}_A.png");
        var N = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TP}/{name}/BA_{name}_N.png");
        string suffix = tint < 0.5f ? "_Sil" : "";
        return Mat($"BA_{name}{suffix}", "Universal Render Pipeline/Lit", m =>
        {
            m.SetTexture("_BaseMap", A); m.SetColor("_BaseColor", new Color(tint, tint * 0.98f, tint * 0.95f));
            if (N && tint >= 0.5f) { m.SetTexture("_BumpMap", N); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f); }
            else { m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", tint < 0.5f ? 0f : smooth);
            m.SetFloat("_EnvironmentReflections", 0f); m.SetFloat("_SpecularHighlights", tint < 0.5f ? 0f : 1f);
            if (tint < 0.5f) { m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); }
        });
    }

    static void ScanImport()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TP }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            bool nrm = path.EndsWith("_N.png");
            bool flip = nrm && path.Contains("Pagoda");   // KHS source zip: Unreal (DirectX) normal map
            var type = nrm ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (imp.textureType == type && imp.maxTextureSize == 2048 && imp.flipGreenChannel == flip) continue;
            imp.textureType = type; imp.maxTextureSize = 2048; imp.flipGreenChannel = flip; imp.sRGBTexture = !nrm;
            imp.SaveAndReimport();
        }
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { TP }))
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            if (imp.materialImportMode == ModelImporterMaterialImportMode.None && !imp.importAnimation) continue;
            imp.materialImportMode = ModelImporterMaterialImportMode.None; imp.importAnimation = false;
            imp.importCameras = false; imp.importLights = false;
            imp.SaveAndReimport();
        }
    }

    // Instantiates a model scaled to a height, its footprint centred on pos and its base on the ground (sunk a little).
    static GameObject Landmark(Transform parent, string path, Vector3 pos, float yaw, float height, Material mat, float sink = 0.08f, bool shadows = true)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!src) { Debug.LogWarning("[Bamboo] missing " + path); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, yaw, 0));
        go.transform.localScale = Vector3.one;
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        var rs = go.GetComponentsInChildren<Renderer>();
        if (mat) foreach (var r in rs) r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
        foreach (var r in rs) r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        Bounds B() { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }
        var b0 = B();
        go.transform.localScale = Vector3.one * (height / Mathf.Max(0.01f, b0.size.y));
        var b1 = B();
        go.transform.position = pos - new Vector3(b1.center.x, b1.min.y, b1.center.z) + Vector3.down * sink * height;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    static void Landmarks(BambooField field, Transform props, System.Random rs)
    {
        ScanImport();
        float Rn() => (float)rs.NextDouble();
        float hx = field.arena.x * 0.5f, hz = field.arena.y * 0.5f;
        var root = new GameObject("Landmarks").transform;
        var clear = new List<Vector3>();
        // r29 (Kido 11-14: the pagoda rock "looks like a flat cutout sitting on the floor"): every big stone piece
        // registers its foot with the ground shader (soil and moss heaped against it, soft occlusion at the base)
        field.contacts = new List<Vector4>();
        GameObject Foot(GameObject go, float radiusMul = 0.55f, float strength = 1f)
        {
            if (!go) return go;
            var rs0 = go.GetComponentsInChildren<Renderer>(); if (rs0.Length == 0) return go;
            var bb = rs0[0].bounds; foreach (var r0 in rs0) bb.Encapsulate(r0.bounds);
            var fp = field.transform.position;
            if (field.contacts.Count < 12)
                field.contacts.Add(new Vector4(bb.center.x - fp.x, bb.center.z - fp.z, Mathf.Max(bb.extents.x, bb.extents.z) * radiusMul * 2f, strength));
            return go;
        }


        var pagodaMat = ScanMat("Pagoda", 0.62f, 0.08f);
        var litMat = ScanMat("Literatus", 0.6f, 0.08f);
        var jangMat = ScanMat("Jangseung", 1.1f, 0.06f);
        var jangSil = ScanMat("Jangseung", 0.12f);
        var stoneMat = AssetDatabase.LoadAssetAtPath<Material>($"{Mats}/BA_Stone.mat");
        var stone2Mat = AssetDatabase.LoadAssetAtPath<Material>($"{Mats}/BA_Stone2.mat");
        var woodMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/AbandonedVillage/Materials/AV_Wood.mat");
        var woodSil = Mat("BA_SotdaeSil", "Universal Render Pipeline/Lit", m => { m.SetColor("_BaseColor", new Color(0.03f, 0.03f, 0.03f)); m.SetFloat("_Smoothness", 0f); });
        var rubbleMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/AbandonedVillage/Materials/AV_MS_RubblePatch.mat");
        string pagoda = $"{TP}/Pagoda/BA_Pagoda.fbx", lit = $"{TP}/Literatus/BA_Literatus.fbx", jang = $"{TP}/Jangseung/BA_Jangseung.fbx";
        string sotdae = "Assets/Game/AbandonedVillage/Models/AV_Sotdae.fbx", rubble = $"{AVMS}/RubblePatch/AV_MS_RubblePatch.fbx";
        string Rock(int i) => $"Assets/Art/LookStudy/Stages/Models/Rock_rock{i:00}.fbx";
        string Boulder(int i) => $"Assets/Art/LookStudy/Stages/Models/Rock_boulder0{i}.fbx";

        // a 서낭당 cairn: one big boulder at the heart, mossy stones heaped round it in rings, smaller toward the top
        void Cairn(Vector3 c, float size)
        {
            Block(Foot(Landmark(root, Boulder(rs.Next(2) == 0 ? 1 : 3), c, Rn() * 360, 1.1f * size, stoneMat, 0.15f), 1.15f, 0.9f), 1.6f);   // r27: the cairn heap blocks as a whole
            for (int ring = 0; ring < 3; ring++)
            {
                int n = 9 - ring * 3; float rad = (1.5f - ring * 0.5f) * size, y = ring * 0.32f * size;
                for (int k = 0; k < n; k++)
                {
                    float a = (k + Rn() * 0.6f) / n * Mathf.PI * 2f;
                    var p = c + new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad);
                    var go = Landmark(root, Rock(1 + rs.Next(13)), p, Rn() * 360, (0.42f - ring * 0.08f) * size * (0.8f + Rn() * 0.4f), Rn() < 0.6f ? stoneMat : stone2Mat, 0.2f);
                    if (go) go.transform.rotation = Quaternion.Euler(Rn() * 30 - 15, Rn() * 360, Rn() * 30 - 15);
                }
            }
            Landmark(root, rubble, c + new Vector3(0.6f, 0, -0.8f) * size, Rn() * 360, 0.35f, rubbleMat, 0.1f, false);
        }

        // r27 (Kido 11-14: objects inside the arena are fine now; bring the shrine to the middle): the 서낭당 court in the
        // clearing, paved (floor shader, vertex a): the old pagoda at its heart, two literati facing the path, cairns at
        // the court rim, 장승 where the path enters, offering candles. The big pieces block (box colliders) like walls.
        var pag = new Vector3(field.shrine.x, 0, field.shrine.y);
        // r29 (Kido 11-14: the rock under the pagoda read as a flat cutout lying on the floor): the KHS scan stands on its
        // own bedrock outcrop with a cut-off underside. Find the outcrop (the height slices that are still wide), sink it
        // so only its upper part shows, tip it a little, and seat it: Megascans small rocks and low plants half buried
        // along its rim (inside the blocker, so nothing to snag on), plus the shader's soil/moss heap (Foot).
        GameObject PagodaOnRock(GameObject go)
        {
            if (!go) return go;
            var mfs = go.GetComponentsInChildren<MeshFilter>();
            var pts = new List<Vector3>();
            foreach (var mf in mfs) { var m = mf.sharedMesh; if (!m) continue; var v = m.vertices; for (int k = 0; k < v.Length; k += 3) pts.Add(mf.transform.TransformPoint(v[k])); }
            if (pts.Count == 0) return go;
            float y0 = pts.Min(q => q.y), y1 = pts.Max(q => q.y);
            var c0 = new Vector2(pts.Average(q => q.x), pts.Average(q => q.z));
            const int S = 40; var ext = new float[S];
            foreach (var q in pts) { int si = Mathf.Clamp((int)((q.y - y0) / (y1 - y0) * S), 0, S - 1); ext[si] = Mathf.Max(ext[si], Vector2.Distance(new Vector2(q.x, q.z), c0)); }
            float maxExt = ext.Max(); int top = 0;
            for (int si = 0; si < S; si++) if (ext[si] > maxExt * 0.62f) top = si;
            float rockH = (top + 1f) / S * (y1 - y0);
            float sink = rockH * 0.4f;
            go.transform.position += Vector3.down * sink;
            go.transform.RotateAround(new Vector3(c0.x, 0, c0.y), Vector3.right, 1.5f);
            go.transform.RotateAround(new Vector3(c0.x, 0, c0.y), Vector3.forward, -1.0f);
            // rim: the outcrop's outline where it now meets the ground
            var rim = new List<Vector3>();
            foreach (var mf in mfs) { var m = mf.sharedMesh; if (!m) continue; var v = m.vertices; for (int k = 0; k < v.Length; k += 7) { var q = mf.transform.TransformPoint(v[k]); if (Mathf.Abs(q.y) < 0.12f) rim.Add(q); } }
            Debug.Log($"[Bamboo] pagoda rock: height {y1 - y0:0.00}, outcrop {rockH:0.00} m (slice {top}/{S}), sunk {sink:0.00}, widest {maxExt:0.00} m, rim points {rim.Count}");
            if (rim.Count < 8) return go;
            string smallRocks = $"{AVMS}/SmallRocks/AV_MS_SmallRocks.fbx";
            var srMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/AbandonedVillage/Materials/AV_MS_SmallRocks.mat");
            string[] plantVars = { "VarA", "VarB", "VarC" };
            var plantMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/AbandonedVillage/Materials/AV_MS_WildGreen.mat");
            int n = 16;
            for (int k = 0; k < n; k++)
            {
                var q = rim[rs.Next(rim.Count)];
                var outward = new Vector3(q.x - c0.x, 0, q.z - c0.y).normalized;
                var p = q + outward * (0.05f + Rn() * 0.35f); p.y = 0;
                if (k % 3 != 2)
                {
                    string src = Rn() < 0.5f ? smallRocks : Rock(1 + rs.Next(13));
                    float h = 0.18f + Rn() * 0.3f;
                    var r = Landmark(root, src, p, Rn() * 360, h, src == smallRocks && srMat ? srMat : (Rn() < 0.6f ? stoneMat : stone2Mat), 0.45f, false);
                    if (r) r.transform.rotation = Quaternion.Euler(Rn() * 24 - 12, Rn() * 360, Rn() * 24 - 12);
                }
                else
                {
                    string plants = $"{AVMS}/WildGreen/AV_MS_WildGreen_{plantVars[rs.Next(3)]}.fbx";
                    Landmark(root, plants, p + outward * 0.2f, Rn() * 360, 0.3f + Rn() * 0.25f, plantMat, 0.05f, false);
                }
            }
            return go;
        }
        void Block(GameObject go, float shrink)
        {
            if (!go) return;
            var rs0 = go.GetComponentsInChildren<Renderer>(); if (rs0.Length == 0) return;
            var bb = rs0[0].bounds; foreach (var r0 in rs0) bb.Encapsulate(r0.bounds);
            var bc = new GameObject("Blocker").AddComponent<BoxCollider>();
            bc.transform.SetParent(go.transform.parent, false);
            bc.transform.position = new Vector3(bb.center.x, bb.min.y + bb.size.y * 0.5f, bb.center.z);
            bc.size = new Vector3(bb.size.x * shrink, bb.size.y, bb.size.z * shrink);
        }
        Block(Foot(PagodaOnRock(Landmark(root, pagoda, pag, 180f + Rn() * 20f - 10f, 6.2f, pagodaMat, 0.04f)), 0.5f, 1f), 0.55f);
        var pathIn = new Vector3(field.TrailX(pag.z - field.shrineRadius + 1f), 0, pag.z - field.shrineRadius + 1f);
        Block(Foot(Landmark(root, lit, pag + new Vector3(-3.2f, 0, -3.6f), 180f + 10f, 2.5f, litMat), 0.8f, 0.7f), 0.6f);
        Block(Foot(Landmark(root, lit, pag + new Vector3(3.2f, 0, -3.6f), 180f - 10f, 2.5f, litMat), 0.8f, 0.7f), 0.6f);
        Cairn(pag + new Vector3(-6.4f, 0, 2.2f), 0.85f);
        Cairn(pag + new Vector3(6.6f, 0, 1.0f), 0.7f);
        Block(Foot(Landmark(root, jang, pathIn + new Vector3(-2.3f, 0, 0), 175f, 2.1f, jangMat), 0.9f, 0.6f), 0.8f);
        Block(Foot(Landmark(root, jang, pathIn + new Vector3(2.3f, 0, 0), 185f, 2.0f, jangMat), 0.9f, 0.6f), 0.8f);
        Block(Landmark(root, sotdae, pag + new Vector3(-5.2f, 0, -2.4f), 20f, 5.4f, woodMat), 0.5f);
        float tx = field.TrailX(hz + 1.5f);
        Landmark(root, jang, new Vector3(tx - 2.7f, 0, hz + 1.4f), 172f, 1.9f, jangMat);
        Landmark(root, jang, new Vector3(tx + 2.7f, 0, hz + 1.3f), 188f, 1.8f, jangMat);
        Landmark(root, sotdae, new Vector3(tx + 4.6f, 0, hz + 2.6f), 30f, 5.2f, woodMat);
        clear.Add(new Vector3(tx, hz + 2f, 4.2f));

        // west: the 서낭당 cairn under the bamboo, 솟대 pair and a 장승
        var wc = new Vector3(-hx - 1.5f, 0, 7f);
        Cairn(wc, 1.25f);
        Landmark(root, sotdae, wc + new Vector3(1.6f, 0, 2.6f), 100f, 5.6f, woodMat);
        Landmark(root, sotdae, wc + new Vector3(2.2f, 0, -2.4f), 70f, 4.8f, woodMat);
        Landmark(root, jang, wc + new Vector3(3.0f, 0, 0.4f), 90f, 1.9f, jangMat);
        clear.Add(new Vector3(wc.x, wc.z, 9.5f));
        // r25: offering candles at the cairn foot: a low warm light spilling into the arena edge
        void Warm(Vector3 p, float range, float inten) { var lg = new GameObject("OfferingLight"); lg.transform.SetParent(root, false); lg.transform.position = p; var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.58f, 0.28f); l.range = range; l.intensity = inten; l.shadows = LightShadows.None; }
        Warm(wc + new Vector3(2.4f, 0.6f, 0.2f), 7f, 2.6f);
        Warm(pag + new Vector3(0, 0.7f, -2.6f), 9f, 2.6f);
        Warm(pag + new Vector3(-6.0f, 0.5f, 1.2f), 6f, 1.8f);   // r18: the leaning wall hid it

        // east: two literati flanking a smaller cairn
        var ec = new Vector3(hx + 2.4f, 0, -9f);
        Cairn(ec, 0.9f);
        Foot(Landmark(root, lit, ec + new Vector3(-1.4f, 0, 3.2f), -90f + 6f, 2.5f, litMat), 0.8f, 0.7f);
        Foot(Landmark(root, lit, ec + new Vector3(-1.4f, 0, -3.2f), -90f - 6f, 2.5f, litMat), 0.8f, 0.7f);
        clear.Add(new Vector3(ec.x, ec.z, 7f));

        // south (camera side): tall near-black 장승 and 솟대 among the dark bamboo, spaced so the middle stays open
        // r3: 11-14 m out (closer they covered the player near the south edge); at the 1.6x camera they rise only into the
        // bottom quarter of the screen
        Landmark(root, sotdae, new Vector3(-31f, 0, -hz - 12f), 0f, 13f, woodSil, 0.02f, false);
        Landmark(root, sotdae, new Vector3(9f, 0, -hz - 13f), 40f, 14f, woodSil, 0.02f, false);
        // (r4: big black 장승 here read as one huge wedge over the player; only thin 솟대 poles and the bamboo)

        field.clearings = clear;
        Debug.Log($"[Bamboo] landmarks: {root.childCount} pieces, {clear.Count} bamboo clearings");
    }
}
