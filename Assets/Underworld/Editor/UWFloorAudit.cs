// Diagnostics: list every renderer in the Underworld scene that uses UW_Floor (position, rotation, scale, bounds, mesh,
// vertex count) to ArtSource/uw_quality/floor_audit.txt. Opens the scene additively and closes it without saving.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Underworld
{
    public static class UWFloorAudit
    {
        [MenuItem("Tools/Underworld/Quality/Audit Floor Renderers")]
        public static void Run()
        {
            const string path = "Assets/Underworld/Scenes/Underworld.unity";
            var already = EditorSceneManager.GetSceneByPath(path);
            var sc = already.isLoaded ? already : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var root in sc.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    bool uses = false;
                    foreach (var m in r.sharedMaterials) if (m && m.name == "UW_Floor") uses = true;
                    if (!uses) continue;
                    var t = r.transform; var mf = r.GetComponent<MeshFilter>();
                    sb.AppendLine($"{GetPath(t)} | pos {t.position} rot {t.eulerAngles} scale {t.lossyScale} | bounds min {r.bounds.min} max {r.bounds.max} | mesh {(mf && mf.sharedMesh ? mf.sharedMesh.name + " v" + mf.sharedMesh.vertexCount : "-")} | static {r.gameObject.isStatic}");
                    n++;
                }
            sb.Insert(0, $"{n} renderers use UW_Floor\n");
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource/uw_quality/floor_audit.txt"), sb.ToString());
            if (!already.isLoaded) EditorSceneManager.CloseScene(sc, true);
        }

        static string GetPath(Transform t) { var s = t.name; while (t.parent) { t = t.parent; s = t.name + "/" + s; } return s; }
    }
}
