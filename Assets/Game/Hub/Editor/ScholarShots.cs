// Edit-mode stills of the scholar in Hub.unity from the game camera angle (pitch 49.5, FOV 30), closer zooms.
// Skinned meshes don't re-skin in edit mode, so each pose is baked into a temporary proxy mesh for the render.
// Run: rr_queue "call Hub.ScholarShots.Run". Output ArtSource/DeskMan/shots/<clip>_<t>_z<zoom>.png (local only).
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hub
{
    public static class ScholarShots
    {
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource/DeskMan/shots");
            Directory.CreateDirectory(outDir);
            var active = EditorSceneManager.GetActiveScene();
            if (active.path != HubBuilder.ScenePath)
            {
                if (active.isDirty) { File.WriteAllText(Path.Combine(outDir, "log.txt"), "active scene dirty, skipped"); return; }
                EditorSceneManager.OpenScene(HubBuilder.ScenePath, OpenSceneMode.Single);
            }
            var npc = Object.FindFirstObjectByType<ScholarNPC>();
            var log = new System.Text.StringBuilder();
            if (!npc) { File.WriteAllText(Path.Combine(outDir, "log.txt"), "no ScholarNPC in scene"); return; }
            var anim = npc.animator ? npc.animator : npc.GetComponentInChildren<Animator>();
            var smrs = anim.GetComponentsInChildren<SkinnedMeshRenderer>();
            var cam = Camera.main;
            var p0 = cam.transform.position; var r0 = cam.transform.rotation; float fov0 = cam.fieldOfView;
            bool async0 = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            var clips = anim.runtimeAnimatorController.animationClips.ToDictionary(c => c.name);
            var shots = new (string clip, float t)[] { ("Sit_Write", 0.1f), ("Sit_Write", 0.5f), ("Sit_Dip", 0.4f), ("Sit_Pause", 0.5f), ("Sit_Attend", 0.2f) };
            foreach (var (clipName, t) in shots)
            {
                if (!clips.TryGetValue(clipName, out var clip)) { log.AppendLine("missing " + clipName); continue; }
                clip.SampleAnimation(anim.gameObject, t * clip.length);
                var proxies = smrs.Select(s =>
                {
                    var m = new Mesh(); s.BakeMesh(m, true); m.RecalculateBounds();
                    var g = new GameObject("proxy") { hideFlags = HideFlags.HideAndDontSave };
                    g.transform.SetPositionAndRotation(s.transform.position, s.transform.rotation);
                    g.AddComponent<MeshFilter>().sharedMesh = m;
                    g.AddComponent<MeshRenderer>().sharedMaterials = s.sharedMaterials;
                    s.enabled = false;
                    return g;
                }).ToList();
                foreach (var zoom in new[] { 1f, 0.35f })
                {
                    var target = npc.transform.position; target.y = 0.6f;
                    var off = Quaternion.Euler(49.5f, 0f, 0f) * Vector3.back * (32.4f * zoom);
                    cam.transform.position = target + off; cam.transform.rotation = Quaternion.LookRotation(-off);
                    cam.fieldOfView = 30f;
                    cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply(); RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(outDir, $"{clipName}_{t:0.0}_z{zoom:0.00}.png"), tex.EncodeToPNG());
                }
                foreach (var g in proxies) { Object.DestroyImmediate(g.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(g); }
                foreach (var s in smrs) s.enabled = true;
                log.AppendLine("shot " + clipName + " " + t);
            }
            cam.transform.position = p0; cam.transform.rotation = r0; cam.fieldOfView = fov0;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            ShaderUtil.allowAsyncCompilation = async0;
            log.AppendLine("npc at " + npc.transform.position + " fwd " + npc.transform.forward);
            File.WriteAllText(Path.Combine(outDir, "log.txt"), log.ToString());
            // sampling posed the instance: drop those edits so the scene isn't left dirty for the hub builder/shots
            EditorSceneManager.OpenScene(HubBuilder.ScenePath, OpenSceneMode.Single);
        }
    }
}
