// 태엽열쇠: sticks the wind-up key prefab into a monster's back. Put this on the monster root (the object that faces the
// walk direction). On Awake it finds the spine bone, places the key on the back surface (auto-probed once per mesh+bone and
// cached), and parents it to the bone so it rides the animation. The turn and the red glow are in the shader
// (BulletHeaven/WindKey), so a key costs nothing per frame on the CPU.
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class WindKeyAttach : MonoBehaviour
{
    [Tooltip("WindKey prefab (Assets/Game/WindKey/Prefabs/WindKey.prefab).")]
    public GameObject keyPrefab;
    [Tooltip("Bone to ride. Empty = first bone whose name contains one of the hints.")]
    public Transform bone;
    public string[] boneHints = { "Spine2", "Spine1", "Spine", "Chest" };

    [Header("Placement (in this object's axes: x = right, y = up, z = forward)")]
    [Tooltip("Gear-to-gear width of the key in world metres (the mesh is 0.864 wide, 0.5 tall).")]
    public float width = 1.3f;
    [Tooltip("Shaft direction out of the body: 0 = straight up (four-legged backs), 60-80 = out of an upright monster's back.")]
    [Range(-30, 90)] public float pitch = 10f;
    [Tooltip("Offset from the bone (x right, y up, z forward). Along the shaft it is replaced by the probed surface when Auto Surface is on.")]
    public Vector3 offset = Vector3.zero;
    [Tooltip("Find the body surface along the shaft from the skinned mesh (once per mesh+bone, cached).")]
    public bool autoSurface = true;
    [Tooltip("How deep the collar sinks into the body, as a fraction of the key height.")]
    [Range(0, 0.6f)] public float sink = 0.15f;
    [Tooltip("Yaw of the gear plane at rest (0 = gears across the shoulders).")]
    public float yaw = 0f;

    [Header("Look")]
    [Tooltip("Shared materials with different phases; one is picked per monster so the keys don't tick in unison.")]
    public Material[] phaseVariants;
    public bool castShadows = false;
    public bool attachOnAwake = true;

    // saved with the prefab when the key is placed in the editor (WindKeySetup), so spawning costs no probe at all
    [SerializeField, HideInInspector] Transform key;
    public Transform Key { get => key; private set => key = value; }
    const float MeshWidth = 0.864f, MeshHeight = 0.5f;
    static readonly Dictionary<(int, string), float> surfaceCache = new Dictionary<(int, string), float>();

    void Awake() { if (attachOnAwake) Attach(); }
    // the after-post glow pass (WindKeyGlowPass) only runs while some key is out
    bool counted;
    void OnEnable() { if (!counted) { counted = true; WindKeyGlowPass.ActiveKeys++; } }
    void OnDisable() { if (counted) { counted = false; WindKeyGlowPass.ActiveKeys--; } }

    // 11-17 (Kido: 발광 더 잘 보이게): stage grades change post exposure by ~2.5 stops (폐촌 +1.9, 풀밭 -0.7), which clipped the
    // red glow to peach in bright grades and dimmed it in dark ones. The shader divides the glow by the current post exposure
    // so it lands at the same brightness (and stays red) in every stage. One callback for all keys, no per-key cost.
    static readonly int PostExposureId = Shader.PropertyToID("_WK_PostExposure");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void HookExposure()
    {
        UnityEngine.Rendering.RenderPipelineManager.beginContextRendering -= OnBeginRendering;
        UnityEngine.Rendering.RenderPipelineManager.beginContextRendering += OnBeginRendering;
    }
    static void OnBeginRendering(UnityEngine.Rendering.ScriptableRenderContext ctx, List<Camera> cams)
    {
        var stack = UnityEngine.Rendering.VolumeManager.instance.stack;
        var ca = stack?.GetComponent<UnityEngine.Rendering.Universal.ColorAdjustments>();
        Shader.SetGlobalFloat(PostExposureId, ca != null && ca.active ? ca.postExposure.value : 0f);
    }

    public Transform Attach()
    {
        if (Key != null) return Key;
        if (keyPrefab == null) return null;
        if (bone == null) bone = FindBone();
        var anchor = bone != null ? bone : transform;
        // shaft direction: up, tipped back toward the tail by pitch
        Vector3 outDir = Quaternion.AngleAxis(-pitch, transform.right) * transform.up;
        float s = width / MeshWidth;
        Vector3 c = anchor.position + transform.rotation * offset;
        float along = autoSurface ? SurfaceDistance(anchor, c, outDir) : 0f;
        Vector3 pos = c + outDir * (along - sink * MeshHeight * s);
        Quaternion rot = Quaternion.AngleAxis(yaw, outDir) * Quaternion.LookRotation(Vector3.Cross(transform.right, outDir), outDir);

        var go = Instantiate(keyPrefab, pos, rot);
        go.name = keyPrefab.name;
        Key = go.transform;
        Key.localScale = Vector3.one * s;
        Key.SetParent(anchor, true);   // keeps the world pose and scale under any bone scale
        var r = go.GetComponentInChildren<Renderer>();
        if (r != null)
        {
            if (phaseVariants != null && phaseVariants.Length > 0)
                r.sharedMaterial = phaseVariants[(int)((uint)GetInstanceID() * 2654435761u % (uint)phaseVariants.Length)];
            r.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        return Key;
    }

    public void Detach() { if (Key != null) { if (Application.isPlaying) Destroy(Key.gameObject); else DestroyImmediate(Key.gameObject); } Key = null; }

    Transform FindBone()
    {
        var all = GetComponentsInChildren<Transform>(true);
        foreach (var h in boneHints)
            foreach (var t in all)
                if (t.name.EndsWith(h, System.StringComparison.OrdinalIgnoreCase) || t.name.EndsWith(":" + h, System.StringComparison.OrdinalIgnoreCase)) return t;
        foreach (var h in boneHints)
            foreach (var t in all)
                if (t.name.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0) return t;
        return null;
    }

    // furthest point of the body along the shaft direction, within a small radius around the shaft line
    float SurfaceDistance(Transform anchor, Vector3 c, Vector3 outDir)
    {
        var smr = GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr == null || smr.sharedMesh == null) return 0f;
        var key = (smr.sharedMesh.GetInstanceID(), $"{anchor.name}|{pitch:F1}|{offset.x:F2}|{offset.y:F2}|{offset.z:F2}");
        float sc = Mathf.Max(1e-4f, transform.lossyScale.y);
        if (surfaceCache.TryGetValue(key, out float h)) return h * sc;

        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var verts = baked.vertices;
        var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
        Bounds b = smr.bounds; float rad = Mathf.Max(0.12f, 0.1f * Mathf.Max(b.size.x, b.size.z));
        float best = 0f;
        foreach (var v0 in verts)
        {
            var d = m.MultiplyPoint3x4(v0) - c;
            float a = Vector3.Dot(d, outDir);
            if (a <= best || (d - outDir * a).sqrMagnitude > rad * rad) continue;
            best = a;
        }
        if (Application.isPlaying) Destroy(baked); else DestroyImmediate(baked);
        surfaceCache[key] = best / sc;
        return best;
    }
}
