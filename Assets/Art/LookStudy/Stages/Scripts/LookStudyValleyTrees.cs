// Downloaded pines mixed into the valley's own pine belt (11-13 Kido: the trees were all the same).
// Lives in Resources so the runtime-built valley finds it without a scene reference.
using UnityEngine;

[CreateAssetMenu(fileName = "ValleyTrees", menuName = "LookStudy/Valley Trees")]
public class LookStudyValleyTrees : ScriptableObject
{
    public GameObject[] pines;    // TR_Pine / TR_Fir (Sketchfab "Coniferous Forest Assets Pack", Dari, CC BY 4.0)
    public GameObject[] heroes;   // TR_HeroPine_A/B (Sketchfab "Japanese Black Pine", matt z chan, CC BY 4.0), ~156k tris: few
    public Material[] swaps;      // valley copies (snow off): a renderer's TR_X material becomes LSV_TR_X
    public GameObject[] riverRocks;   // 11-14: Megascans water-worn stones (model roots), matched by index with riverMats
    public Material[] riverMats;
    public Material cascadeMat;   // LSV_Cascade (LookStudy/StageCascade): the pool waterfall sheets
    public GameObject cliff;      // Megascans Granite Cliff (Fab, free): the face behind the big fall
    public Material cliffMat;
}
