// Persistent trample map for the stages: grass flattened behind the player and footprints in soft ground.
// Shaders read it through _LSTrail, _LSTrailRect and _LSTrailOn.
using UnityEngine;

public class LookStudyTrail : MonoBehaviour
{
    public static LookStudyTrail Instance;
    public Vector2 center;
    public float size = 84f;
    public int resolution = 2048;
    public bool ownedByWalker; // set by LookStudyWalker, so the trample feed leaves this trail alone
    RenderTexture a, b;
    Material mat;
    readonly Vector4[] feet = new Vector4[4];
    int footCount;
    Vector4 player;

    public static LookStudyTrail Ensure(Vector2 center, float size)
    {
        if (Instance) return Instance;
        var go = new GameObject("LS Trail");
        var t = go.AddComponent<LookStudyTrail>();
        t.center = center; t.size = size;
        return t;
    }

    void OnEnable()
    {
        Instance = this;
        var sh = Resources.Load<Shader>("LookStudyTrail");
        if (!sh) { Debug.LogWarning("[LS] trail shader missing"); enabled = false; return; }
        mat = new Material(sh);
        a = Make(); b = Make();
        Shader.SetGlobalFloat("_LSTrailOn", 1f);
    }

    RenderTexture Make()
    {
        var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        rt.Create();
        var prev = RenderTexture.active; RenderTexture.active = rt; GL.Clear(false, true, new Color(0, 0, 0.5f, 0.5f)); RenderTexture.active = prev;
        return rt;
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat("_LSTrailOn", 0f);
        if (a) { a.Release(); Destroy(a); } if (b) { b.Release(); Destroy(b); }
        if (mat) Destroy(mat);
        if (Instance == this) Instance = null;
    }

    public void SetPlayer(Vector3 p, float radius, float strength) => player = new Vector4(p.x, p.z, radius, strength);

    public void Foot(Vector3 p, float yawDegrees, float strength)
    {
        if (footCount >= feet.Length) return;
        feet[footCount++] = new Vector4(p.x, p.z, -yawDegrees * Mathf.Deg2Rad, strength);
    }

    void LateUpdate()
    {
        var rect = new Vector4(center.x - size * 0.5f, center.y - size * 0.5f, size, 0);
        mat.SetVector("_Rect", rect);
        mat.SetVector("_Player", player);
        for (int i = footCount; i < feet.Length; i++) feet[i] = Vector4.zero;
        mat.SetVectorArray("_Feet", feet);
        footCount = 0;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        // Grass springs back over about 7 s, footprints fade over about 40 s.
        // Grass: subtracted per frame, same rate as the original GrassTrample (max(1/2048, dt*0.14) → about 7 s).
        mat.SetVector("_Decay", new Vector4(Mathf.Max(1f / 2048f, dt * 0.14f), Mathf.Pow(0.01f, dt / 40f), 0, 0));
        Graphics.Blit(a, b, mat);
        (a, b) = (b, a);
        Shader.SetGlobalTexture("_LSTrail", a);
        Shader.SetGlobalVector("_LSTrailRect", rect);
    }
}
