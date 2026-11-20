// Map capture helper (11-20, Kido: "맵 찍을 때 왜 캐릭터 공격하고 다니면서 찍어?"): environment clips must show the
// player only walking, no attacks, no weapon VFX. Does nothing unless ArtSource/SeeWide/walk.trigger exists when a player
// appears (editor captures only): then deletes the file and, after a short delay (stills first), walks the player along
// the route written in the trigger ("x,z x,z ..." metres relative to where the player stands when it starts, restarted
// after every capture teleport; empty = a small loop). Never fires.
// Never runs in a build (no ArtSource folder) or in normal play (no trigger file).
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace RoomRun
{
    public sealed class SeeWideWalk : MonoBehaviour
    {
        static string Trigger => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "SeeWide", "walk.trigger");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Application.isEditor) return;
            var go = new GameObject("SeeWideWalk");
            DontDestroyOnLoad(go);
            go.AddComponent<SeeWideWalk>();
        }

        void OnDestroy() { if (walker != null) Debug.Log("[SeeWideWalk] destroyed while walking"); }

        float delay = 1.5f;
        Walker walker;
        PlayerController owner;
        Vector3 lastPos;

        void Update()
        {
            // the capture jumps rooms / respawns or teleports the player after the trigger is read: follow the current
            // player and restart the route whenever it is replaced or moved by the capture (not by walking)
            if (walker != null)
            {
                var cur = RunDirector.Instance ? RunDirector.Instance.Player : null;   // the run's player (other PlayerControllers may exist)
                if (!cur) return;
                bool teleported = cur == owner && (cur.transform.position - lastPos).sqrMagnitude > 4f;
                if (cur != owner || teleported || cur.Source != walker)
                {
                    walker = new Walker(walker.Points, Time.unscaledTime + delay, Flat(cur.transform.position));
                    cur.Source = walker; owner = cur;
                    Debug.Log($"[SeeWideWalk] route (re)started on {cur.name} at {cur.transform.position}");
                }
                lastPos = cur.transform.position;
                if (walker.Done) walker = null;
                return;
            }
            if (!File.Exists(Trigger)) return;
            var p = RunDirector.Instance ? RunDirector.Instance.Player : null;
            if (!p) return;
            var pts = new List<Vector2>();
            try
            {
                foreach (var tok in File.ReadAllText(Trigger).Split(new[] { ' ', '\n', '\r', '\t' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    if (tok.StartsWith("delay=")) { float.TryParse(tok.Substring(6), NumberStyles.Float, CultureInfo.InvariantCulture, out delay); continue; }
                    var xz = tok.Split(',');
                    if (xz.Length == 2 && float.TryParse(xz[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                        && float.TryParse(xz[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) pts.Add(new Vector2(x, z));
                }
                File.Delete(Trigger);
            }
            catch { return; }
            if (pts.Count == 0) pts.AddRange(new[] { new Vector2(8, 0), new Vector2(8, 7), new Vector2(-6, 7), new Vector2(-6, 0), Vector2.zero });
            walker = new Walker(pts.ToArray(), Time.unscaledTime + delay, Flat(p.transform.position));
            p.Source = walker; owner = p; lastPos = p.transform.position;
        }

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        sealed class Walker : ICommandSource
        {
            readonly Vector2[] pts; readonly float start; readonly Vector2 origin; int k;
            public Walker(Vector2[] r, float s, Vector2 o) { pts = r; start = s; origin = o; }
            public Vector2[] Points => pts;
            public bool Done => k >= pts.Length;
            public PlayerCommand Read(PlayerController p, Camera cam)
            {
                if (Time.unscaledTime < start || k >= pts.Length) return new PlayerCommand { aimDir = Vector3.back };
                var pos = new Vector2(p.transform.position.x, p.transform.position.z);
                var d = origin + pts[k] - pos;
                if (d.magnitude < 0.8f) { k++; return Read(p, cam); }
                var m = d.normalized;
                return new PlayerCommand { move = m, aimDir = new Vector3(m.x, 0f, m.y), fire = false };
            }
        }
    }
}
