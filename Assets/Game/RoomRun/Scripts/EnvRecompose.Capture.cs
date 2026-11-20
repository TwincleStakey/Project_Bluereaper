// 11-20 Kido: "맵찍을때 왜 캐릭터 공격하고 다니면서 찍어?" - map / environment captures are walk-only: no attack, no weapon
// effects, no enemies (calm=1). Capture-only guard for this thread's shots, editor only: while
// ArtSource/EnvRecompose/nofire.trigger exists, the player's command source is wrapped so every command keeps its move
// and aim but never fires (greatsword, whip, flying sword, gun all read cmd.fire). Remove the trigger and commands pass
// through unchanged. Nothing in the other threads' files or settings is touched.
#if UNITY_EDITOR
using UnityEngine;

namespace RoomRun
{
    public sealed partial class EnvRecompose
    {
        const string NoFireTrigger = "ArtSource/EnvRecompose/nofire.trigger";
        static bool noFire; static float noFireCheckAt;

        sealed class NoFireSource : ICommandSource
        {
            public ICommandSource inner;
            public PlayerCommand Read(PlayerController p, Camera cam)
            {
                var c = inner != null ? inner.Read(p, cam) : default;
                if (noFire) c.fire = false;
                return c;
            }
        }

        void Update()
        {
            if (Time.unscaledTime >= noFireCheckAt) { noFireCheckAt = Time.unscaledTime + 0.5f; noFire = System.IO.File.Exists(NoFireTrigger); }
            if (!noFire) return;
            var run = RunDirector.Instance;
            var pl = run ? run.Player : null;
            if (pl && !(pl.Source is NoFireSource)) pl.Source = new NoFireSource { inner = pl.Source };
        }
    }
}
#endif
