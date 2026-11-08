// Records a short preview clip from the game camera while an automatic walker drives the player.
// Started by the editor command file (see Editor/RoomRunRemote.cs); not used in normal play.
using System.IO;
using UnityEngine;

namespace RoomRun
{
    public sealed class ClipRecorder : MonoBehaviour, ICommandSource
    {
        public string folder;
        public float seconds = 10f;
        public System.Action Finished;

        const int W = 1280, H = 720;
        RenderTexture rt;
        Texture2D tex;
        int frame;
        Vector3 origin;
        float t;

        void Start()
        {
            Directory.CreateDirectory(folder);
            foreach (var f in Directory.GetFiles(folder, "*.jpg")) File.Delete(f);
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            Time.captureFramerate = 30;
            var run = RunDirector.Instance;
            origin = run.Player.transform.position;
            run.Player.Source = this;
            run.Player.DebugInvulnerable = true; // A clip is for looking, so enemies can't end it early.
        }

        // Walks a figure eight around where the clip started, so fresh and recovering trails both show.
        public PlayerCommand Read(PlayerController p, Camera cam)
        {
            var target = origin + new Vector3(Mathf.Sin(t * 0.55f) * 7f, 0, Mathf.Sin(t * 1.1f) * 3.5f);
            var d = target - p.transform.position; d.y = 0;
            return new PlayerCommand { move = d.sqrMagnitude > 0.04f ? new Vector2(d.x, d.z).normalized : Vector2.zero, aimDir = d.normalized };
        }

        void LateUpdate()
        {
            t += Time.deltaTime;
            var cam = RunDirector.Instance.Rig.Cam;
            cam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(folder, $"f{frame++:D6}.jpg"), tex.EncodeToJPG(88));
            if (frame >= seconds * 30f)
            {
                Time.captureFramerate = 0;
                Finished?.Invoke();
                Destroy(this);
            }
        }

        void OnDestroy()
        {
            if (rt) { rt.Release(); Destroy(rt); }
            if (tex) Destroy(tex);
        }
    }
}
