// Downloaded pines mixed into the valley's own pine belt (11-13 Kido: the trees were all the same).
// Lives in Resources so the runtime-built valley finds it without a scene reference.
using UnityEngine;

[CreateAssetMenu(fileName = "ValleyTrees", menuName = "LookStudy/Valley Trees")]
public class LookStudyValleyTrees : ScriptableObject
{
    public GameObject[] pines;    // TR_Pine / TR_Fir (Sketchfab "Coniferous Forest Assets Pack", Dari, CC BY 4.0)
    // 11-14 (Kido: the Japanese black pine "looks like a bonsai, never use it"): the heroes are now bare dead broadleaf trees
    // from Poly Haven scans (Jacaranda Tree, Tree Small 02, CC0; leaves cut off in Blender, shared with 폐촌's
    // ThirdParty/PolyHaven/bare_trees). bareMats: bark (A), trunk (B), twigs (B's branches).
    public GameObject[] heroes;
    public Material[] bareMats;
    public Material[] swaps;      // valley copies (snow off): a renderer's TR_X material becomes LSV_TR_X
    public GameObject[] riverRocks;   // 11-14: Megascans water-worn stones (model roots), matched by index with riverMats
    public Material[] riverMats;
    public Material cascadeMat;   // LSV_Cascade (LookStudy/StageCascade): the pool waterfall sheets
    public GameObject cliff;      // Megascans Granite Cliff (Fab, free): the face behind the big fall
    public Material cliffMat;
}
