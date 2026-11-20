// Play-mode only helper for the 장산범 move-set showcase: loads the BossSeal arena additively (editor, no build settings
// needed), makes it the active scene so its light/fog/post drive the render, and hands its game camera to the showcase.
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class JSBQMapLoader : MonoBehaviour
{
    public string mapScene = "Assets/Game/BossSeal/BossSeal.unity";

    IEnumerator Start()
    {
        var sc = GetComponent<JSBQuadShowcase>();
#if UNITY_EDITOR
        var op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(mapScene, new LoadSceneParameters(LoadSceneMode.Additive));
        while (!op.isDone) yield return null;
        var map = SceneManager.GetSceneByPath(mapScene);
        SceneManager.SetActiveScene(map);
        Camera cam = null;
        foreach (var g in map.GetRootGameObjects())
            foreach (var c in g.GetComponentsInChildren<Camera>(true)) if (c.CompareTag("MainCamera")) cam = c;
        if (cam != null)
            foreach (var mb in cam.GetComponentsInParent<MonoBehaviour>(true))   // game camera rigs would fight the showcase camera
                if (!mb.GetType().Namespace?.StartsWith("UnityEngine") ?? true) mb.enabled = false;
        if (cam != null) { cam.gameObject.SetActive(true); cam.farClipPlane = Mathf.Max(cam.farClipPlane, 300f); cam.fieldOfView = 30f; }
        sc.cam = cam;
#else
        yield return null;
#endif
    }
}
