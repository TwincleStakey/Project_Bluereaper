// 심연 고원 (11-19) capture helper. Sits in the KillKnight scene and does nothing unless the file
// ArtSource/KillKnight/walk.trigger exists when the room loads (editor captures only): then it deletes the file and
// walks the player along a fixed route at normal speed: toward the south edge, then along it, round the south-east
// notch corner, then north along the east edge. Waits a few seconds first so the capture's stills come first.
// Never runs in a build (no ArtSource folder) or in normal play (no trigger file).
using System.IO;
using RoomRun;
using UnityEngine;

namespace KillKnight
{
    public sealed class KKEdgeWalk : MonoBehaviour
    {
        public Vector2[] route =
        {
            new Vector2(12f, -43f), new Vector2(26f, -43f), new Vector2(26f, -34f), new Vector2(37f, -34f),
            new Vector2(37f, -16f), new Vector2(43f, -16f), new Vector2(43f, 20f),
        };
        public float startDelay = 4.2f;

        static string Trigger => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "KillKnight", "walk.trigger");
        float armed = -1f;

        void Update()
        {
            if (armed >= 0f) return;
            if (!Application.isEditor || !File.Exists(Trigger)) { enabled = false; return; }
            var p = FindFirstObjectByType<PlayerController>();
            if (!p) return;
            try { File.Delete(Trigger); } catch { }
            armed = Time.unscaledTime;
            p.Source = new Walker(route, armed + startDelay);
            enabled = false;
        }

        sealed class Walker : ICommandSource
        {
            readonly Vector2[] pts; readonly float start; int k;
            public Walker(Vector2[] r, float s) { pts = r; start = s; }
            public PlayerCommand Read(PlayerController p, Camera cam)
            {
                if (Time.unscaledTime < start || k >= pts.Length) return new PlayerCommand();
                var pos = new Vector2(p.transform.position.x, p.transform.position.z);
                var d = pts[k] - pos;
                if (d.magnitude < 0.8f) { k++; return Read(p, cam); }
                var m = d.normalized;
                return new PlayerCommand { move = m, aimDir = new Vector3(m.x, 0f, m.y) };
            }
        }
    }
}
