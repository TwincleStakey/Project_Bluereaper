// 심연 고원 (11-20): the room's spawn box is the 90 x 90 m square, but the plateau has notches over the void. RunDirector
// picks enemy spawn points inside the square, so some landed over the abyss. This keeps every enemy on the plateau:
// one that stands outside the outline (or has dropped below the top) is put back on the nearest point 1.5 m inside.
// Lives in this map's scene only; nothing else changes.
using RoomRun;
using UnityEngine;

namespace KillKnight
{
    public static class KKOutline
    {
        // the plateau outline (x, z), metres, shared with KKBuilder
        public static readonly Vector2[] Points =
        {
            new Vector2(-38, 45), new Vector2(12, 45), new Vector2(12, 39), new Vector2(34, 39), new Vector2(34, 30),
            new Vector2(45, 30), new Vector2(45, -18), new Vector2(39, -18), new Vector2(39, -36), new Vector2(28, -36),
            new Vector2(28, -45), new Vector2(-20, -45), new Vector2(-20, -39), new Vector2(-40, -39), new Vector2(-40, -24),
            new Vector2(-45, -24), new Vector2(-45, 16), new Vector2(-38, 16),
        };
        public static bool Inside(Vector2 p)
        {
            bool c = false;
            for (int i = 0, j = Points.Length - 1; i < Points.Length; j = i++)
            {
                var a = Points[i]; var b = Points[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) c = !c;
            }
            return c;
        }
        public static float EdgeDist(Vector2 p, out Vector2 closest)
        {
            float best = float.MaxValue; closest = p;
            for (int i = 0; i < Points.Length; i++)
            {
                var a = Points[i]; var b = Points[(i + 1) % Points.Length];
                var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                var q = a + ab * t; float d = (q - p).magnitude;
                if (d < best) { best = d; closest = q; }
            }
            return best;
        }
    }

    public sealed class KKSpawnGuard : MonoBehaviour
    {
        void LateUpdate()
        {
            var run = RunDirector.Instance;
            if (!run || run.Enemies == null) return;
            foreach (var e in run.Enemies)
            {
                if (!e) continue;
                var t = e.transform; var p2 = new Vector2(t.position.x, t.position.z);
                bool inside = KKOutline.Inside(p2);
                if (inside && t.position.y > -0.5f && KKOutline.EdgeDist(p2, out _) >= 0.6f) continue;
                KKOutline.EdgeDist(p2, out var q);
                var dirIn = Vector2.zero;
                for (int k = 0; k < 8 && dirIn == Vector2.zero; k++)
                {
                    float a = k * Mathf.PI / 4f; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    if (KKOutline.Inside(q + d * 1.5f)) dirIn = d;
                }
                var np = q + dirIn * 1.5f;
                var cc = e.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
                t.position = new Vector3(np.x, 0.05f, np.y);
                if (cc) cc.enabled = true;
            }
        }
    }
}
