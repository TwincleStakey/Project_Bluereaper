// Soft see-through near the player (11-13, Kido: "the big hard circle cut is unnatural; make it soft like other top-down
// games"). Feeds the globals of Shaders/RRSeeThrough.hlsl every frame in every RoomRun stage:
//   _PlayerScreenPos[3]  each player's chest on screen (viewport xy, eye depth z, w = 1)
//   _RR_SeeShape         inner/outer radius as a fraction of screen height, min visibility, depth margin
//   _RR_SeeShape2        player feet height, height ramp start/full above the feet, temporal dither frame
// Radii are set in metres and converted with the camera distance, so the soft ring keeps its size around the player at
// any zoom. Read by LookStudy foliage/props/stage rocks+houses, reeds, bamboo and the SnowPlague SP_SEETHROUGH path
// (snow and 폐촌 trees), all as a dithered clip in the opaque queue.
// Dev tools panel (LookStudyDevTools, "가림 투명"): PlayerPrefs RR.SeeThrough 1 = soft (default), 0 = the previous behaviour
// (snow/폐촌 hard hole, bamboo's own hole, nothing in the other fields) for before/after; RR.SeeThroughTemporal 1 = the
// dither pattern moves every frame.
using UnityEngine;

namespace RoomRun
{
    [DefaultExecutionOrder(1000)]   // after SPField / AVSeeThrough / BambooField, which write the same player globals
    public sealed class SeeThroughFX : MonoBehaviour
    {
        public const string ModeKey = "RR.SeeThrough", TemporalKey = "RR.SeeThroughTemporal";
        public static int Mode => PlayerPrefs.GetInt(ModeKey, 1);

        [Tooltip("Inside this radius (m around the player) the occluder is at its thinnest.")]
        public float innerRadius = 1.3f;
        [Tooltip("Beyond this radius (m) the occluder is untouched; between inner and outer it fades smoothly.")]
        public float outerRadius = 4.2f;
        [Tooltip("How much of the occluder stays at the centre (0 = hollow hole, 1 = no see-through).")]
        [Range(0f, 1f)] public float minVisibility = 0.22f;
        [Tooltip("Only what is at least this much closer to the camera than the player's chest fades (m).")]
        public float depthMargin = 0.8f;
        [Tooltip("Fade starts this high above the player's feet and is full at heightFull (m): grass and low rocks stay.")]
        public float heightStart = 0.45f, heightFull = 1.4f;
        public float chestHeight = 1.0f;

        static readonly int ID_Players = Shader.PropertyToID("_PlayerScreenPos"),
            ID_Shape = Shader.PropertyToID("_RR_SeeShape"), ID_Shape2 = Shader.PropertyToID("_RR_SeeShape2");
        readonly Vector4[] players = new Vector4[3];
        PlayerController[] found = new PlayerController[0];
        float nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("SeeThroughFX");
            DontDestroyOnLoad(go);
            go.AddComponent<SeeThroughFX>();
        }

        void LateUpdate()
        {
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 0.5f; found = FindObjectsByType<PlayerController>(FindObjectsSortMode.None); }
            var cam = Camera.main;
            int mode = Mode;
            for (int k = 0; k < 3; k++) players[k] = Vector4.zero;
            float feet = float.MaxValue, nearest = 0f;
            int n = 0;
            if (cam && mode != 0)
                foreach (var pc in found)
                {
                    if (!pc || !pc.isActiveAndEnabled || n >= 3) continue;
                    var p = pc.transform.position;
                    var vp = cam.WorldToViewportPoint(p + Vector3.up * chestHeight);
                    if (vp.z <= 0) continue;
                    players[n++] = new Vector4(vp.x, vp.y, vp.z, 1);
                    feet = Mathf.Min(feet, p.y);
                    nearest = nearest <= 0 ? vp.z : Mathf.Min(nearest, vp.z);
                }
            if (n == 0) { Shader.SetGlobalVector(ID_Shape, Vector4.zero); return; }
            Shader.SetGlobalVectorArray(ID_Players, players);

            // metres -> fraction of screen height at the player's distance
            float h = cam.orthographic ? cam.orthographicSize * 2f : 2f * nearest * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            h = Mathf.Max(h, 0.01f);
            bool temporal = PlayerPrefs.GetInt(TemporalKey, 0) != 0;
            Shader.SetGlobalVector(ID_Shape, new Vector4(innerRadius / h, Mathf.Max(outerRadius, innerRadius + 0.1f) / h, minVisibility, depthMargin));
            Shader.SetGlobalVector(ID_Shape2, new Vector4(feet, heightStart, Mathf.Max(heightFull, heightStart + 0.05f), temporal ? 1 + (Time.frameCount & 63) : 0));
        }

        void OnDisable() => Shader.SetGlobalVector(ID_Shape, Vector4.zero);
    }
}
