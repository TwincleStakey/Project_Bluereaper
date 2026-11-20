// 11-19 Kido: "게임 들어가면 기본 캐릭터 무기 양손검 어검 사복검 123키로 바꾸게 해줘 총게임처럼".
// 1 = 양손검 (GreatswordCombat), 2 = 이기어검 (EogeomCombat), 3 = 사복검 (WhipCombat). All three already ride on the same
// greatsword hero; this only flips their existing toggles (RR.Whip / RR.Eogeom, the same ones the dev panel uses) and asks
// PlayerController to apply them in the same frame instead of on its 0.5 s poll. No weapon's body clips or VFX change here.
// Dev only: if ArtSource/weapon_demo.txt exists when Play starts (editor), it records a moving 1 -> 2 -> 3 demo to
// ArtSource/weapon_demo/ (30 fps frames) and leaves Play. Start it with rr_cmd "call RoomRun.WeaponDemo.Begin".
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoomRun
{
    [DefaultExecutionOrder(-50)]   // before PlayerController reads input, so the new weapon takes this frame's attack
    public sealed class WeaponHotkeys : MonoBehaviour
    {
        public static readonly string[] Names = { "양손검", "이기어검", "사복검" };
        PlayerController pc;
        GameObject wrap, wrapLight;   // 이기어검 left-fist flame + light (EogeomCombat leaves them on when it is switched off)
        float findLeft;

        /// 0 = 양손검, 1 = 이기어검, 2 = 사복검 (from the toggles; 이기어검 wins over 사복검 like in PlayerController)
        public static int Current => EogeomCombat.UseEogeom ? 1 : WhipCombat.UseWhip ? 2 : 0;

        void Awake() { pc = GetComponent<PlayerController>(); }

        void Start()
        {
#if UNITY_EDITOR
            string flag = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "weapon_demo.txt");
            if (File.Exists(flag)) { File.Delete(flag); gameObject.AddComponent<WeaponDemo>(); }
#endif
        }

        public void Select(int i)
        {
            if (!pc || i < 0 || i > 2) return;
            if (!PlayerController.UseGreatsword) PlayerPrefs.SetInt(PlayerController.GreatswordKey, 1);
            if (i != Current)
            {
                PlayerPrefs.SetInt(EogeomCombat.Key, i == 1 ? 1 : 0);
                PlayerPrefs.SetInt(WhipCombat.Key, i == 2 ? 1 : 0);
            }
            pc.SyncWeaponNow();
            var run = RunDirector.Instance;
            if (run && run.Hud) run.Hud.ShowRoomName($"{i + 1}  {Names[i]}");
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !pc) return;
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) Select(0);
            else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) Select(1);
            else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) Select(2);
        }

        void LateUpdate()
        {
            bool eg = pc && pc.EogeomActive;
            if (eg && (!wrap || !wrapLight) && (findLeft -= Time.deltaTime) <= 0f)
            {
                findLeft = 0.5f;
                if (!wrap) wrap = GameObject.Find("EogeomHandWrap");
                if (!wrapLight) wrapLight = GameObject.Find("EogeomHandLight");
            }
            if (wrap && wrap.activeSelf != eg) wrap.SetActive(eg);
            if (wrapLight && wrapLight.activeSelf != eg) wrapLight.SetActive(eg);
        }
    }

#if UNITY_EDITOR
    // Dev capture: walks (strafe / circle) with the attack held most of the time, presses 1 -> 2 -> 3 -> 1, game camera, 30 fps.
    [DefaultExecutionOrder(5000)]
    public sealed class WeaponDemo : MonoBehaviour, ICommandSource
    {
        public static float Seg = 4.2f;   // seconds per weapon
        /// rr_cmd "call RoomRun.WeaponDemo.Begin": drop the flag and enter Play (the capture leaves Play by itself)
        public static void Begin()
        {
            var root = Directory.GetParent(Application.dataPath).FullName;
            File.WriteAllText(Path.Combine(root, "ArtSource", "weapon_demo.txt"), "1");
            UnityEditor.EditorApplication.isPlaying = true;
        }
        const int W = 1280, H = 720;
        PlayerController pc; WeaponHotkeys hk; ICommandSource keep;
        RenderTexture rt; Texture2D tex; StreamWriter log;
        string dir, lockPath;
        int frame = -1, shown = -1; float t; Vector3 origin;

        System.Collections.IEnumerator Start()
        {
            pc = GetComponent<PlayerController>(); hk = GetComponent<WeaponHotkeys>();
            var root = Directory.GetParent(Application.dataPath).FullName;
            dir = Path.Combine(root, "ArtSource", "weapon_demo");
            lockPath = Path.Combine(root, "ArtSource", "remote_play.lock");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.jpg")) File.Delete(f);
            log = new StreamWriter(Path.Combine(dir, "frames.csv"));
            try { File.WriteAllText(lockPath, "weapondemo " + System.DateTime.Now.ToString("HH:mm:ss")); } catch (IOException) { }
            var run = RunDirector.Instance;
            while (!run || run.Room == null || run.Transitioning) { yield return null; run = RunDirector.Instance; }
            yield return new WaitForSeconds(2.5f);
            keep = pc.Source; pc.Source = this; pc.DebugInvulnerable = true;
            origin = pc.transform.position;
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            Time.captureFramerate = 30;
            frame = 0;
        }

        public PlayerCommand Read(PlayerController p, Camera cam)
        {
            float local = t % Seg;
            // aim: nearest enemy within 12 m, else the walking direction turned 60 deg
            var path = new Vector3(Mathf.Cos(t * 0.5f), 0f, Mathf.Sin(t * 0.5f) * 0.6f);   // slow loop around the start
            var target = origin + path * 5f;
            var d = target - p.transform.position; d.y = 0f;
            var mv = d.sqrMagnitude > 0.04f ? d.normalized : Vector3.zero;
            var tan = new Vector3(-Mathf.Sin(t * 0.5f), 0f, Mathf.Cos(t * 0.5f) * 0.6f).normalized;
            mv = (mv * 0.35f + tan).normalized;
            Vector3 aim = Quaternion.Euler(0f, -50f, 0f) * mv; float best = 144f;
            var run = RunDirector.Instance;
            if (run) foreach (var e in run.Enemies)
            {
                if (!e) continue;
                var to = e.transform.position - p.transform.position; to.y = 0f;
                if (to.sqrMagnitude < best) { best = to.sqrMagnitude; aim = to.normalized; }
            }
            bool fire = local > 0.5f && (local % 1.9f) < 1.5f;
            return new PlayerCommand { move = new Vector2(mv.x, mv.z), aimDir = aim, fire = fire };
        }

        void LateUpdate()
        {
            if (frame < 0) return;
            int w = Mathf.Min(2, (int)(t / Seg)) ;
            int want = t >= Seg * 3f ? 0 : w;
            if (want != shown) { shown = want; hk.Select(want); }
            var cam = RunDirector.Instance.Rig.Cam;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(dir, $"f{frame:D5}.jpg"), tex.EncodeToJPG(90));
            log.WriteLine($"{frame},{t:0.000},{shown + 1}");
            frame++;
            t += Time.deltaTime;
            if (t >= Seg * 3f + 1.5f) Finish();
        }

        void Finish()
        {
            frame = -1;
            Time.captureFramerate = 0;
            log?.Close(); log = null;
            if (pc) { pc.Source = keep; pc.DebugInvulnerable = false; }
            try { if (File.Exists(lockPath) && File.ReadAllText(lockPath).StartsWith("weapondemo ")) File.Delete(lockPath); } catch (IOException) { }
            File.WriteAllText(Path.Combine(dir, "done.txt"), System.DateTime.Now.ToString("HH:mm:ss"));
            UnityEditor.EditorApplication.isPlaying = false;
        }

        void OnDestroy()
        {
            log?.Close();
            if (rt) { rt.Release(); Destroy(rt); }
            if (tex) Destroy(tex);
        }
    }
#endif
}
