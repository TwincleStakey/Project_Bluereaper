// A real fallen log (Quixel Megascans "Mossy Tree Log", Fab, Standard License) washed against the pool bank, replacing
// the old flat-stamped pine trunk bridge (11-13 Kido: it read as code-made). Low and without a collider, so the arena
// rule (nothing inside blocks, raises the floor or stops shots) holds.
// Spawned when the Valley scene loads, from Resources/QAValley; no scene edit needed.
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ValleyMossyLog
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        // the first scene is already loaded when this runs
        for (int i = 0; i < SceneManager.sceneCount; i++) OnLoaded(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
    }

    static void OnLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "Valley" || !Object.FindFirstObjectByType<LookStudyValley>()) return;
        foreach (var root in scene.GetRootGameObjects()) if (root.name == "MossyLog") return;

        var mesh = Resources.Load<Mesh>("QAValley/MossyLog");
        if (!mesh) { var go0 = Resources.Load<GameObject>("QAValley/MossyLog"); var mf0 = go0 ? go0.GetComponentInChildren<MeshFilter>() : null; mesh = mf0 ? mf0.sharedMesh : null; }
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (!mesh || !sh) { Debug.LogWarning("[Valley] mossy log: mesh or URP Lit missing"); return; }

        var mat = new Material(sh) { name = "MossyLog (runtime)" };
        mat.SetTexture("_BaseMap", Resources.Load<Texture2D>("QAValley/MossyLog_BaseColor"));
        var n = Resources.Load<Texture2D>("QAValley/MossyLog_Normal");
        if (n) { mat.SetTexture("_BumpMap", n); mat.EnableKeyword("_NORMALMAP"); }
        var ao = Resources.Load<Texture2D>("QAValley/MossyLog_AO");
        if (ao) { mat.SetTexture("_OcclusionMap", ao); mat.EnableKeyword("_OCCLUSIONMAP"); }
        mat.SetFloat("_Smoothness", 0.18f);
        mat.SetColor("_BaseColor", new Color(0.78f, 0.78f, 0.76f)); // the valley is a night scene; keep the scan from glaring

        // Washed up against the west bank of the middle (gourd) pool, half sunk in the shallows: low dressing like the
        // cobbles (its top stays ~0.25 m above the floor, no collider), so the arena rule holds and it is in view.
        var c = new Vector2(-0.6f, 10.2f);
        var flow = new Vector2(-7f, -6f).normalized;
        var across = new Vector2(-flow.y, flow.x);
        var axis = (flow * 0.94f - across * 0.34f).normalized;   // mostly along the bank, the lower end reaching into the water
        var b = mesh.bounds;
        bool alongX = b.size.x >= b.size.z;   // Megascans meshes lie flat; long axis is x or z
        float len = alongX ? b.size.x : b.size.z, scale = 1.1f, half = len * scale * 0.5f;
        var a2 = c - axis * half; var e2 = c + axis * half;
        float ya = Mathf.Max(LookStudyValley.Height(a2.x, a2.y), LookStudyValley.Water - 0.3f);
        float ye = Mathf.Max(LookStudyValley.Height(e2.x, e2.y), LookStudyValley.Water - 0.3f);
        var mid = c;
        float pitch = Mathf.Atan2(ye - ya, len * scale) * Mathf.Rad2Deg;
        float yaw = Mathf.Atan2(axis.x, axis.y) * Mathf.Rad2Deg - (alongX ? 90f : 0f);

        var go = new GameObject("MossyLog", typeof(MeshFilter), typeof(MeshRenderer));
        SceneManager.MoveGameObjectToScene(go, scene);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        var t = go.transform;
        t.localScale = Vector3.one * scale;
        t.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.AngleAxis(alongX ? pitch : -pitch, alongX ? Vector3.forward : Vector3.right);
        // sunk: the top sits ~0.25 m above the local floor
        float y = (ya + ye) * 0.5f - b.center.y * scale - b.extents.y * scale + 0.25f;
        t.position = new Vector3(mid.x, y, mid.y) - t.rotation * new Vector3(b.center.x, 0f, b.center.z) * scale;
        Debug.Log($"[Valley] mossy log at {t.position} rot {t.eulerAngles} bounds {mr.bounds} meshBounds {b} ya {ya:0.00} ye {ye:0.00}");
    }
}
