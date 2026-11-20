// 이기어검 capture helper (11-18): while EogeomCapture.Frames > 0 (RoomRunRemote shots token "egcap=N"), the hero commands
// one volley every ShowGap seconds at the enemies placed ahead of him, and every frame is rendered from three cameras into
// ArtSource/Eogeom/out/frames: game camera at zoom 1.6 (the real game framing), 0.8 (closer), and a near 3/4 front-left view
// where the left-hand 검결 reads. Fixed 30 fps game time (Time.captureFramerate). frames.csv logs the attack time per frame
// so the comparison sheet can pick the same moments (검결 / thrust / hold / recall). Dev only; nothing else uses it.
using System.IO;
using UnityEngine;

namespace RoomRun
{
    [DefaultExecutionOrder(5000)]   // after the camera rig, the body poses and the flying segments
    public sealed class EogeomCapture : MonoBehaviour
    {
        public static int Frames;
        public static float ShowGap = 1.3f;
        public static string OutDir = Path.Combine(Application.dataPath, "..", "ArtSource", "Eogeom", "out", "v12", "frames");
        // v4: 1 = moving show (strafe R/L, back, forward, circle) with fire held
        public static int Mode;
        // v5: "egfps=S" = no rendering, S seconds of the show (fire held) with the timing of every frame, appended to out/v5_vfx/fps.txt
        public static float FpsSeconds;
        float fpsT0, fpsWorst; int fpsFrames;
        static readonly float[] Zooms = { 1.6f, 0.8f };
        const int W = 1280, H = 720;

        sealed class Show : ICommandSource
        {
            readonly float t0 = Time.time;
            public PlayerCommand Read(PlayerController p, Camera cam)
            {
                float t = Time.time - t0;
                var aim = Vector3.forward; float bd = float.MaxValue;
                var run = RunDirector.Instance;
                if (run) foreach (var e in run.Enemies)
                {
                    if (!e) continue;
                    var d = e.transform.position - p.transform.position; d.y = 0f;
                    if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; aim = d.normalized; }
                }
                if (Mode == 2)   // v12: standing, attack held 1.9 s of every 2.4 s (hold pose + release)
                    return new PlayerCommand { aimDir = aim, fire = t > 0.3f && ((t - 0.3f) % 2.4f) < 1.9f };
                if (Mode == 1)
                {
                    // aim stays on the nearest enemy; legs: strafe right, strafe left, backpedal, forward, then circle the group
                    // v8: attack held 1.9 s of every 2.4 s, so the pieces stay out (interceptor) and the release / rejoin shows too
                    var c = new PlayerCommand { aimDir = aim, fire = t > 0.3f && ((t - 0.3f) % 2.4f) < 1.9f };
                    var R = Vector3.Cross(Vector3.up, aim);
                    Vector3 m;
                    if (t < 0.3f) m = Vector3.zero;
                    else if (t < 1.8f) m = R;
                    else if (t < 3.3f) m = -R;
                    else if (t < 4.6f) m = -aim;
                    else if (t < 5.6f) m = aim;
                    else
                    {
                        var ctr = Vector3.zero; int n = 0;
                        if (run) foreach (var e in run.Enemies) if (e) { ctr += e.transform.position; n++; }
                        ctr = n > 0 ? ctr / n : p.transform.position + aim * 6f;
                        var to = ctr - p.transform.position; to.y = 0f; float dist = to.magnitude; to /= Mathf.Max(dist, 0.01f);
                        m = (Vector3.Cross(to, Vector3.up) + to * Mathf.Clamp((dist - 5f) * 0.6f, -0.6f, 0.6f)).normalized;
                    }
                    c.move = new Vector2(m.x, m.z);
                    return c;
                }
                float ph = (t - 0.4f) % ShowGap;
                return new PlayerCommand { aimDir = aim, fire = t > 0.4f && ph < 0.08f };
            }
        }

        int frame = -1; ICommandSource keep; RenderTexture rt; Texture2D tex; StreamWriter log;
        EogeomCombat eg; PlayerController pc;

        void Start()
        {
            pc = GetComponent<PlayerController>(); eg = GetComponent<EogeomCombat>();
            var run = RunDirector.Instance;
            if (!pc || !eg || !run) { Destroy(this); return; }
            Directory.CreateDirectory(OutDir);
            if (FpsSeconds <= 0f) foreach (var f in Directory.GetFiles(OutDir, "*.jpg")) File.Delete(f);   // (an fps run used to wipe the last capture)
            keep = pc.Source; pc.Source = new Show();
            // enemies: up to 3 in a shallow arc 5.5-6.5 m ahead (+Z), so the volley has room to arc out and loop back
            int k = 0; var p0 = pc.transform.position;
            foreach (var e in run.Enemies)
            {
                if (!e || k >= 3) continue;
                float ang = (k - 1) * 22f * Mathf.Deg2Rad, r = 5.5f + (k % 2);
                var cc = e.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
                e.enabled = false;
                var p = p0 + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * r;
                p.y = Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var gh, 60f, Ground.GroundMask) ? gh.point.y : p0.y;
                e.transform.position = p; e.transform.rotation = Quaternion.LookRotation(p0 - p, Vector3.up);
                var hh = e.GetComponent<Health>(); if (hh) hh.max = hh.current = 99999;
                k++;
            }
            if (FpsSeconds > 0f) { fpsT0 = Time.realtimeSinceStartup; frame = 0; StartCoroutine(FpsLoop()); return; }
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            log = new StreamWriter(Path.Combine(OutDir, "frames.csv"), false);
            log.WriteLine("frame,t,kind,flying,clipT,moveW,upperYaw,speed,legClear,pelvisVsMove,aimVsPelvis,headClear,torsoClear,shoulderClear");
            Time.captureFramerate = 30;
            frame = 0;
            StartCoroutine(Loop());
        }

        // v4b: render at the end of the frame (a Camera.Render from LateUpdate drew last frame's skinning, so anything drawn
        // from this frame's bones, like the finger glow, looked offset from the moving hand)
        System.Collections.IEnumerator Loop()
        {
            var eof = new WaitForEndOfFrame();
            while (frame >= 0) { yield return eof; CaptureFrame(); }
        }

        System.Collections.IEnumerator FpsLoop()
        {
            float last = Time.realtimeSinceStartup;
            yield return null; yield return null;
            fpsT0 = last = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - fpsT0 < FpsSeconds)
            {
                yield return null;
                float now = Time.realtimeSinceStartup, dt = now - last; last = now;
                fpsFrames++; fpsWorst = Mathf.Max(fpsWorst, dt);
            }
            float span = Time.realtimeSinceStartup - fpsT0;
            string line = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:HH:mm:ss} trail={1} fx={2} scale={3} moving={4}: {5} frames in {6:0.00}s = {7:0} fps avg, worst frame {8:0.0} ms ({9:0} fps)",
                System.DateTime.Now, EogeomCombat.TrailStyle, System.IO.Path.GetFileNameWithoutExtension(EogeomCombat.TrailPrefabPath), EogeomCombat.FlyScale, Mode, fpsFrames, span, fpsFrames / span, fpsWorst * 1000f, 1f / Mathf.Max(fpsWorst, 1e-4f));
            var fp = Path.Combine(Application.dataPath, "..", "ArtSource", "Eogeom", "out", "v6_cosmos", "fps.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(fp));
            File.AppendAllText(fp, line + "\n");
            Debug.Log("[RoomRun] eogeom fps: " + line);
            FpsSeconds = 0f; Frames = 0;
            if (pc) pc.Source = keep;
            frame = -1;
            Destroy(this);
        }

        void CaptureFrame()
        {
            if (frame < 0) return;
            var run = RunDirector.Instance;
            var rig = run ? run.Rig : null;
            if (!rig || frame >= Frames) { Finish(); return; }
            var cam = rig.Cam;
            float keepZoom = rig.Zoom;
            foreach (var z in Zooms)
            {
                rig.Zoom = z; rig.Snap();
                Grab(cam, $"z{z:0.0}_{frame:000}.jpg");
            }
            // near 3/4 front-left (the character faces +Z at the enemies): the left hand and the hilt side both show
            var pp = pc.transform.position;
            // (v2: from the left side, a little in front, wider lens, so the hand, the hilt and the targets share the frame)
            var look = pp + Vector3.up * 1.1f + Vector3.forward * 2.3f;
            cam.transform.position = pp + new Vector3(-4.6f, 2.0f, 1.3f);
            cam.transform.rotation = Quaternion.LookRotation(look - cam.transform.position);
            float keepFov = cam.fieldOfView; cam.fieldOfView = 46f;
            Grab(cam, $"close_{frame:000}.jpg");
            // v4: hand close-up for the two-finger glow, from the hero's front-left, following the left hand
            var an = pc.GetComponentInChildren<Animator>();
            var lh = an ? an.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            if (lh)
            {
                var fr = an.transform;
                var hp = lh.position;
                cam.transform.position = hp - fr.right * 1.1f + fr.forward * 1.0f + Vector3.up * 0.35f;
                cam.transform.rotation = Quaternion.LookRotation(hp + fr.forward * 0.15f - cam.transform.position);
                cam.fieldOfView = 34f;
                Grab(cam, $"hand_{frame:000}.jpg");
            }
            cam.fieldOfView = keepFov;
            rig.Zoom = keepZoom; rig.Snap();
            log.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1:0.000},{2},{3},{4:0.000},{5:0.00},{6:0.0},{7:0.00},{8:0.000},{9:0},{10:0},{11:0.000},{12:0.000},{13:0.000}", frame, eg ? eg.T : 0f, eg ? eg.Kind : 0, eg ? eg.Flying : 0, eg ? eg.ClipT : 0f, eg ? eg.MoveW : 0f, eg ? eg.UpperYaw : 0f, new Vector3(pc.Velocity.x, 0f, pc.Velocity.z).magnitude, eg ? eg.LegClear : 9f, PelvisVsMove(), AimVsPelvis(), eg ? eg.HeadClear : 9f, eg ? eg.TorsoClear : 9f, eg ? eg.ShoulderClear : 9f));
            frame++;
        }

        float PelvisVsMove()
        {
            var v = pc.Velocity; v.y = 0f; var an = pc.GetComponentInChildren<Animator>();
            if (v.sqrMagnitude < 0.25f || !an) return 0f;
            var f = an.transform.forward; f.y = 0f; return Vector3.SignedAngle(f, v, Vector3.up);
        }
        float AimVsPelvis()
        {
            var an = pc.GetComponentInChildren<Animator>(); if (!an || !eg) return 0f;
            var f = an.transform.forward; f.y = 0f; return Vector3.SignedAngle(f, eg.CursorAim, Vector3.up);
        }

        void Grab(Camera cam, string name)
        {
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(OutDir, name), tex.EncodeToJPG(90));
        }

        void Finish()
        {
            Frames = 0;
            Time.captureFramerate = 0;
            if (pc) pc.Source = keep;
            log?.Close(); log = null;
            if (rt) Destroy(rt); if (tex) Destroy(tex);
            Debug.Log($"[RoomRun] eogeom capture: {frame} frames -> {OutDir}");
            frame = -1;
            Destroy(this);
        }

        void OnDestroy() { if (log != null) { log.Close(); log = null; } if (Time.captureFramerate == 30 && frame >= 0) Time.captureFramerate = 0; }
    }
}
