// 11-19 대전 앞 court capture helper (editor only). Added by EnvRecompose.Court when ArtSource/PalaceCourt/walk.trigger
// exists as the palace room builds (the file is deleted then): after a short wait it walks the player round the court at
// normal speed along a fixed route, past the pillar groups, the north front and the south edge. Never runs in a build or
// in normal play (no trigger file). Same pattern as KillKnight.KKEdgeWalk.
using UnityEngine;

namespace RoomRun
{
    public sealed class PalaceCourtWalk : MonoBehaviour
    {
        public Vector2 offset;
        public float startDelay = 4f;
        static readonly Vector2[] Route =
        {
            new Vector2(0f, -4f), new Vector2(-4f, 12f), new Vector2(-16f, 16f), new Vector2(-16f, 32f), new Vector2(-4f, 37f),
            new Vector2(4f, 30f), new Vector2(17f, 30f), new Vector2(18f, 12f), new Vector2(16f, -12f), new Vector2(16f, -24f),
            new Vector2(2f, -36f), new Vector2(-17f, -30f), new Vector2(-18f, -12f), new Vector2(-4f, -2f), new Vector2(0f, 8f),
        };
        float armed = -1f;

        void Update()
        {
            if (armed >= 0f) return;
            var p = FindFirstObjectByType<PlayerController>();
            if (!p) return;
            armed = Time.unscaledTime;
            var pts = new Vector2[Route.Length];
            for (int i = 0; i < Route.Length; i++) pts[i] = Route[i] + offset;
            p.Source = new Walker(pts, armed + startDelay);
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
                if (d.magnitude < 0.9f) { k++; return Read(p, cam); }
                var m = d.normalized;
                return new PlayerCommand { move = m, aimDir = new Vector3(m.x, 0f, m.y) };
            }
        }
    }
}
