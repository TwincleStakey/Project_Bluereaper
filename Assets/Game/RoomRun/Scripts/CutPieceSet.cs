// Flying pieces for greatsword cuts, baked from the real reed models (ls_cmd `cutpieces`): the top part of each reed clump
// above the cut height, split into single stalks/leaves, pivot at the cut. Keyed by the source mesh name so a cut reed
// throws pieces of its own model. Lives in Resources/GS_CutPieces.asset.
using UnityEngine;

namespace RoomRun
{
    public sealed class CutPieceSet : ScriptableObject
    {
        [System.Serializable]
        public struct Entry { public string source; public Mesh[] pieces; }
        public Entry[] entries;

        public Mesh[] For(Mesh source)
        {
            if (!source || entries == null) return null;
            string n = source.name;
            foreach (var e in entries) if (e.source == n) return e.pieces;
            return null;
        }
    }
}
