// 장산범 retargeted move set (free quadruped clips -> AutoRig, see ArtSource/QuadAnim/README.md) played in Play mode so the
// skinned body really deforms. Runs a list of moves with cross-fades, moves the boss by its planted paw (no foot sliding for
// in-place clips), adds a procedural hit reaction (spring kick on the spine/neck/head + hit-stop + red flash), and can
// record 30 fps frames from the game camera, then leaves Play mode.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class JSBQuadShowcase : MonoBehaviour
{
    [System.Serializable]
    public class Move
    {
        public string state;          // Animator state (= clip name)
        public float seconds = 2f;    // how long to stay (0 = one clip length)
        public float fade = 0.15f;    // cross-fade into this move
        public float speed = 1f;      // animator speed for this move
        public float[] hits = new float[0];   // seconds into the move where the boss is struck (procedural flinch)
        public float turn = 0f;       // deg/s yaw while this move plays
    }

    public Animator animator;
    public Transform follow;          // camera follows this (boss root)
    public Camera cam;
    public Vector3 camOffset = new Vector3(0f, 17f, -14.5f);
    public float camZoom = 2.7f;
    public bool walkByPaws = true;    // translate the root by the planted paw
    public List<Move> moves = new List<Move>();
    public Vector3 arenaCenter = new Vector3(0f, 0f, 0f);
    public float arenaRadius = 16f;   // locomotion steers back toward the centre beyond this
    public string trailMoves = "";   // claw afterimages per move (e.g. "swipe,slam"); off: at 30 Hz Play sampling they drew big zigzag boxes
    public bool loop = true;

    [Header("Hit reaction")]
    public float hitKickDeg = 9f;
    public float hitStop = 0.07f;
    public float springFreq = 7f, springDamp = 0.35f;

    [Header("Recording (Play mode)")]
    public bool record;
    public string recordDir = "ArtSource/QuadAnim/renders/play";
    public int width = 1600, height = 900;
    public bool quitWhenDone = true;

    Transform[] toes; Transform[] kickBones; float[] kickWeights = { 0.35f, 0.6f, 0.9f, 1f };
    Vector3[] prevToeLocal; Vector3 vel; float kick, kickVel; Vector3 kickAxis = Vector3.right;
    Renderer[] warnRenderers; float flash;
    public string currentMove { get; private set; }

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (follow == null) follow = animator.transform;
        var all = animator.GetComponentsInChildren<Transform>();
        Transform F(string n) { foreach (var t in all) if (t.name == n) return t; return null; }
        toes = new[] { F("HindToes.L"), F("HindToes.R"), F("FrontToes.L"), F("FrontToes.R") };
        kickBones = new[] { F("Spine2"), F("Spine3"), F("Neck2"), F("Head") };
        prevToeLocal = new Vector3[toes.Length];
        var wr = new List<Renderer>();
        foreach (var r in animator.GetComponentsInChildren<Renderer>()) if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Warn")) wr.Add(r);
        warnRenderers = wr.ToArray();
    }

    IEnumerator Start()
    {
        for (int i = 0; i < 300 && cam == null; i++) yield return null;   // JSBQMapLoader hands over the arena camera
        if (cam != null) { cam.transform.position = follow.position + Vector3.up * 3f + camOffset * camZoom; cam.transform.rotation = Quaternion.LookRotation(-camOffset); }
        if (record) { Time.captureFramerate = 30; StartCoroutine(Record()); }
        do
        {
            foreach (var m in moves)
            {
                currentMove = m.state;
                var ct = animator.GetComponent<JangsanbeomClawTrails>();
                if (ct != null) { bool on = false; foreach (var k in trailMoves.Split(',')) if (m.state.Contains(k)) on = true; if (!on) ct.ResetTrails(); ct.enabled = on; }
                animator.speed = m.speed;
                animator.CrossFadeInFixedTime(m.state, m.fade, 0, 0f);
                yield return null;
                float len = m.seconds > 0 ? m.seconds : ClipLength(m.state) / Mathf.Max(0.01f, m.speed);
                float t = 0; int hi = 0;
                while (t < len)
                {
                    if (hi < m.hits.Length && t >= m.hits[hi]) { hi++; StartCoroutine(Hit(m.speed)); }
                    if (m.turn != 0) follow.Rotate(0, m.turn * Time.deltaTime, 0, Space.World);
                    var away = follow.position - arenaCenter; away.y = 0;
                    if (away.magnitude > arenaRadius)
                    {   // steer home: turn toward the centre
                        float ang = Vector3.SignedAngle(follow.forward, -away, Vector3.up);
                        follow.Rotate(0, Mathf.Clamp(ang, -120f * Time.deltaTime, 120f * Time.deltaTime), 0, Space.World);
                    }
                    t += Time.deltaTime; yield return null;
                }
            }
            if (record) { done = true; yield break; }
        } while (loop);
    }

    float ClipLength(string state)
    {
        foreach (var c in animator.runtimeAnimatorController.animationClips) if (c.name == state) return c.length;
        return 2f;
    }

    IEnumerator Hit(float resume)
    {
        // struck from the camera side: the head and chest snap back and away, then spring home
        kickAxis = follow.InverseTransformDirection(Vector3.Cross(Vector3.up, cam ? (follow.position - cam.transform.position).normalized : follow.forward)).normalized;
        kickVel += hitKickDeg * springFreq * 2.2f;
        flash = 1f;
        animator.speed = 0.05f;
        float s = 0; while (s < hitStop) { s += Time.unscaledDeltaTime; yield return null; }
        animator.speed = resume;
    }

    void LateUpdate()
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        // spring for the flinch (critically under-damped: one snap + small settle)
        float w = 2 * Mathf.PI * springFreq;
        // sub-stepped: one 30 fps step of this stiff spring is unstable and blew the torso round in circles
        int sub = Mathf.CeilToInt(dt * 480f); float h = dt / sub;
        for (int k = 0; k < sub; k++) { kickVel += (-w * w * kick - 2 * springDamp * w * kickVel) * h; kick += kickVel * h; }
        kick = Mathf.Clamp(kick, -45f, 45f);
        for (int i = 0; i < kickBones.Length; i++)
            if (kickBones[i] != null) kickBones[i].rotation = Quaternion.AngleAxis(kick * kickWeights[i], follow.TransformDirection(kickAxis)) * kickBones[i].rotation;
        flash = Mathf.MoveTowards(flash, 0f, dt * 5f);
        if (warnRenderers != null)
            foreach (var r in warnRenderers) { var pb = new MaterialPropertyBlock(); r.GetPropertyBlock(pb); pb.SetFloat("_Warn", flash); r.SetPropertyBlock(pb); }

        // locomotion from the lowest (planted) paw: in-place clips walk without sliding
        if (walkByPaws && toes[0] != null)
        {
            int lo = -1; float ly = float.MaxValue;
            for (int i = 0; i < toes.Length; i++) { var p = toes[i].position; if (p.y < ly) { ly = p.y; lo = i; } }
            var local = new Vector3[toes.Length];
            for (int i = 0; i < toes.Length; i++) local[i] = follow.InverseTransformPoint(toes[i].position);
            // planted = within 0.35 m of the lowest paw and under 1.2 m (paw rest height is ~0.5 m at boss scale)
            Vector3 v = Vector3.zero; int n = 0;
            for (int i = 0; i < toes.Length; i++)
                if (toes[i].position.y < ly + 0.35f && toes[i].position.y < follow.position.y + 1.2f && prevToeLocal[i] != Vector3.zero) { var d = -(local[i] - prevToeLocal[i]) / dt; d.y = 0; v += d; n++; }
            if (n > 0) vel = Vector3.Lerp(vel, v / n, 0.5f); else vel = Vector3.Lerp(vel, Vector3.zero, dt * 2f);
            if (vel.magnitude > 60f) vel = vel.normalized * 60f;   // a crossfade frame can jump; ignore spikes
            follow.position += follow.TransformDirection(new Vector3(vel.x, 0, vel.z)) * dt;
            for (int i = 0; i < toes.Length; i++) prevToeLocal[i] = local[i];
        }
        if (cam != null)
        {
            var target = follow.position + Vector3.up * 3f;
            var off = camOffset * camZoom;
            cam.transform.position = Vector3.Lerp(cam.transform.position, target + off, record ? 0.35f : dt * 8f);
            cam.transform.rotation = Quaternion.LookRotation(-off);
        }
    }

    bool done;
    IEnumerator Record()
    {
        var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", recordDir));
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        cam.targetTexture = rt;
        int frame = 0;
        yield return null;   // let the first cross-fade settle
        while (!done)
        {
            yield return new WaitForEndOfFrame();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0); tex.Apply(); RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(dir, $"f{frame:0000}_{currentMove}.png"), tex.EncodeToPNG());
            frame++;
        }
        cam.targetTexture = null;
        File.WriteAllText(Path.Combine(dir, "done.txt"), frame + " frames");
#if UNITY_EDITOR
        if (quitWhenDone) UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
