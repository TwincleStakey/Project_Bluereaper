// Scripted check run: drives the player through idle twist, 8-direction walking under a fixed aim, large twist
// with hip turn, and attacks, and saves frames from the game camera. Started by the editor remote (ls_cmd.txt).
using System.Collections;
using System.IO;
using UnityEngine;

namespace LocoSplit
{
    public class LSDemo : MonoBehaviour
    {
        // The editor remote drops ArtSource/LocoSplit/demo_request.txt (static flags do not survive the domain
        // reload on entering play mode); this run deletes it and writes demo_done.txt at the end.
        static string Dir => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "LocoSplit");
        static string OutDir => Path.Combine(Dir, "shots");
        public static float FrameEvery = 1f / 30f;
        int seq;

        public LSPlayer player;
        public Camera cam;
        float aimYaw;

        void Start()
        {
            string req = Path.Combine(Dir, "demo_request.txt");
            if (!Application.isEditor || !File.Exists(req)) return;
            File.Delete(req);
            if (Directory.Exists(OutDir)) foreach (var f in Directory.GetFiles(OutDir, "*.png")) File.Delete(f);
            StartCoroutine(Run());
        }

        const float Base = 160f;   // face the camera a little from the side
        void Aim(float yaw)
        {
            yaw += Base;
            aimYaw = yaw;
            player.extAimPoint = player.transform.position + Quaternion.Euler(0, yaw, 0) * Vector3.forward * 5f;
        }

        IEnumerator Phase(string name, float secs, Vector2 move, float aimFrom, float aimTo, float attackEvery = 0)
        {
            float t = 0, nextShot = 0, nextAtk = 0.2f;
            int n = 0;
            while (t < secs)
            {
                player.extMove = Rot(move);
                Aim(Mathf.Lerp(aimFrom, aimTo, t / secs));
                if (attackEvery > 0 && t >= nextAtk) { player.extAttack = true; nextAtk += attackEvery; }
                if (t >= nextShot) { Shot($"{seq++:0000}_{name}_{n++:000}"); nextShot += FrameEvery; }
                t += Time.deltaTime;
                yield return null;
            }
        }

        static Vector2 Rot(Vector2 m)
        {
            var v = Quaternion.Euler(0, Base, 0) * new Vector3(m.x, 0, m.y);
            return new Vector2(v.x, v.z);
        }

        IEnumerator Run()
        {
            player.external = true;
            Time.captureFramerate = 30;   // fixed steps so every frame is captured at real speed
            // lower, closer camera for the check shots so the body is visible under the hat brim
            var rigCam = cam.GetComponent<LSCamera>();
            if (rigCam) { rigCam.pitch = 32f; rigCam.distance = 5f; rigCam.height = 0.7f; }
            Directory.CreateDirectory(OutDir);
            Aim(0);
            yield return new WaitForSeconds(0.5f);
            yield return Phase("a_idle_aim_sweep", 3f, Vector2.zero, 0, 270);
            yield return Phase("b_walk_fwd", 1.5f, new Vector2(0, 1), 0, 0);
            yield return Phase("c_strafe_right", 1.5f, new Vector2(1, 0), 0, 0);
            yield return Phase("d_walk_back", 1.5f, new Vector2(0, -1), 0, 0);
            yield return Phase("e_strafe_left", 1.5f, new Vector2(-1, 0), 0, 0);
            yield return Phase("f_diag_back", 1.5f, new Vector2(-0.7f, -0.7f), 0, 0);
            yield return Phase("g_circle_aim_sweep", 3f, new Vector2(1, 0), 0, 360);
            yield return Phase("h_attack_walking", 3f, new Vector2(0, 1), 0, 60, 1.0f);
            yield return Phase("i_attack_standing", 2.5f, Vector2.zero, -40, -40, 1.2f);
            player.extMove = Vector2.zero;
            player.external = false;
            Time.captureFramerate = 0;
            File.WriteAllText(Path.Combine(Dir, "demo_done.txt"), System.DateTime.Now.ToString());
        }

        void Shot(string name)
        {
            var rt = RenderTexture.GetTemporary(960, 540, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
        }
    }
}
