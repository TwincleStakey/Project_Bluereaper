// Lets any player walking through a LookStudy field or stage flatten the grass and leave footprints,
// not only the LookStudyWalker test character. It looks for a GameObject named "Player" (the RoomRun player),
// covers the stage with the trail map, and feeds it the player's position and steps.
using UnityEngine;
using UnityEngine.SceneManagement;

public class LookStudyTrampleFeed : MonoBehaviour
{
    Transform player;
    Vector3 last;
    float stepTimer, findTimer, boundsTimer;
    int boundsScene = -1, boundsTries;
    bool left;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("LS Trample Feed");
        DontDestroyOnLoad(go);
        go.AddComponent<LookStudyTrampleFeed>();
        SceneManager.sceneLoaded += (s, m) => { if (LookStudyTrail.Instance && !LookStudyTrail.Instance.ownedByWalker) Destroy(LookStudyTrail.Instance.gameObject); };
    }

    void Update()
    {
        if (!player)
        {
            findTimer -= Time.deltaTime;
            if (findTimer > 0f) return;
            findTimer = 0.5f;
            var go = GameObject.Find("Player");
            if (!go || go.GetComponent("LookStudyWalker")) return; // the walker drives the trail itself
            player = go.transform; last = player.position;
        }
        var trail = LookStudyTrail.Instance;
        if (!trail)
        {
            // 11-10 perf: StageBounds walks every renderer in the scene. It used to run every frame in fields with no
            // LookStudy ground (폐촌: ~7,900 renderers, 9.6 ms a frame). Now at most once a second, and a scene that
            // still has none after a few tries (its stage meshes can appear a little after load) is left alone.
            int scene = SceneManager.GetActiveScene().handle;
            if (scene != boundsScene) { boundsScene = scene; boundsTries = 0; boundsTimer = 0f; }
            if (boundsTries >= 6) return;
            boundsTimer -= Time.deltaTime;
            if (boundsTimer > 0f) return;
            boundsTimer = 1f; boundsTries++;
            if (!StageBounds(out var b)) return; // not a LookStudy scene
            float size = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) + 4f, 40f, 160f);
            trail = LookStudyTrail.Ensure(new Vector2(b.center.x, b.center.z), size);
        }
        if (trail.ownedByWalker) return;

        var p = player.position;
        var d = p - last; d.y = 0;
        bool moving = d.sqrMagnitude > 1e-6f;
        last = p;
        trail.SetPlayer(p, 0.95f, moving ? 1f : 0.85f);
        stepTimer -= Time.deltaTime;
        if (moving && stepTimer <= 0f)
        {
            stepTimer = 0.28f;
            left = !left;
            var fwd = d.normalized;
            var right = new Vector3(fwd.z, 0, -fwd.x);
            trail.Foot(p + right * (left ? -0.13f : 0.13f) + fwd * 0.12f, Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg, 0.6f);
        }
    }

    // The area covered by LookStudy grass and ground, from instanced plants and LookStudy-shaded renderers.
    static bool StageBounds(out Bounds b)
    {
        b = default; bool any = false;
        // The generated field lives under a DontSave root, which FindObjectsByType skips, so walk the scene instead.
        // (With FindObjectsByType the grass field found nothing and never got a trail map.)
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var root in roots)
        foreach (var inst in root.GetComponentsInChildren<LookStudyInstancer>())
            if (inst.Count > 0) { if (any) b.Encapsulate(inst.WorldBounds); else { b = inst.WorldBounds; any = true; } }
        foreach (var root in roots)
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            var m = r.sharedMaterial;
            if (!m || !m.shader) continue;
            var n = m.shader.name;
            if (n != "LookStudy/Foliage" && n != "LookStudy/Ground" && n != "LookStudy/Grass" && n != "LookStudy/StageTerrain") continue;
            if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
        }
        return any;
    }
}
