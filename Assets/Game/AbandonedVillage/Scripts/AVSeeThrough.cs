// 11-12 (Kido: trees in front of the player must go see-through): feeds the snow field's dither-hole globals
// (_PlayerScreenPos, _SP_SeeThrough, read by SnowPlague/Vegetation materials with SP_SEETHROUGH) while 폐촌 is loaded.
// Only the 폐촌 tree materials have the keyword on, so this changes nothing in other fields.
using UnityEngine;

namespace AbandonedVillage
{
    public sealed class AVSeeThrough : MonoBehaviour
    {
        [Range(0.02f, 0.3f)] public float holeRadius = 0.13f;   // fraction of screen height
        [Range(0.005f, 0.1f)] public float holeEdge = 0.045f;
        public float depthMargin = 1.0f;

        static readonly int ID_Players = Shader.PropertyToID("_PlayerScreenPos"), ID_See = Shader.PropertyToID("_SP_SeeThrough");
        readonly Vector4[] players = new Vector4[3];
        Transform player;
        float nextFind;

        void LateUpdate()
        {
            if (!player && Time.unscaledTime >= nextFind)
            {
                nextFind = Time.unscaledTime + 1f;
                var go = GameObject.Find("Player");
                player = go ? go.transform : null;
            }
            var cam = Camera.main;
            for (int k = 0; k < 3; k++) players[k] = Vector4.zero;
            if (cam && player)
            {
                var vp = cam.WorldToViewportPoint(player.position + Vector3.up);
                if (vp.z > 0) players[0] = new Vector4(vp.x, vp.y, vp.z, 1);
            }
            Shader.SetGlobalVectorArray(ID_Players, players);
            Shader.SetGlobalVector(ID_See, new Vector4(holeRadius, holeEdge, depthMargin, 1));
        }

        void OnDisable() => Shader.SetGlobalVector(ID_See, Vector4.zero);
    }
}
