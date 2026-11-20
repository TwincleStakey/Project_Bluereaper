// Stage startup: mood, player height, and the build-only capture route. "-lsscene Name" picks a stage.
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LookStudyRuinsBoot : MonoBehaviour
{
    public LookStudyRuins stage;
    public LookStudyWalker walker;

    void OnEnable()
    {
        if (!stage) return;
        LookStudyRuins.ApplyMood(stage.kind, RenderSettings.sun);
        if (walker)
        {
            bool palace = stage.kind == LookStudyRuins.Kind.Palace;
            walker.heightAt = palace ? (System.Func<float, float, float>)LookStudyRuins.PalaceH : LookStudyRuins.VillageH;
            walker.surfaceAt = palace ? (System.Func<float, float, LookStudyWalker.Surface>)LookStudyRuins.PalaceSurface : LookStudyRuins.VillageSurface;
            walker.trailCenter = palace ? new Vector2(0, 2) : Vector2.zero; walker.trailSize = palace ? 90f : 96f;
            walker.bounds = palace ? new Rect(-25, -24, 50, 56) : new Rect(-20, -16, 40, 32);
        }
    }

#if !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Pick()
    {
        var args = System.Environment.GetCommandLineArgs();
        int t = System.Array.IndexOf(args, "-lsscene");
        if (t >= 0 && t + 1 < args.Length && SceneManager.GetActiveScene().name != args[t + 1]) SceneManager.LoadScene(args[t + 1]);
    }

    IEnumerator Start()
    {
        var args = System.Environment.GetCommandLineArgs();
        int t = System.Array.IndexOf(args, "-lsruinstest");
        if (t < 0 || t + 1 >= args.Length || !stage) yield break;
        string dir = args[t + 1];
        Directory.CreateDirectory(dir);
        Application.runInBackground = true;
        var log = new System.Collections.Generic.List<string>();
        var cam = Camera.main;
        walker.enabled = true;
        yield return new WaitForSeconds(3f);
        var palaceRoute = stage.kind == LookStudyRuins.Kind.Palace;
        var route = palaceRoute ? new[] { new Vector3(-6, 0, -12), new Vector3(4, 0, -16), new Vector3(9, 0, -9) }
                                : new[] { new Vector3(-2, 0, -14), new Vector3(-8, 0, -20), new Vector3(-14, 0, -15), new Vector3(-8, 0, -9), new Vector3(2, 0, -6) };
        int idx = 0;
        walker.autoTarget = () => route[idx];
        for (idx = 0; idx < route.Length; idx++)
        {
            float w = 0;
            while ((walker.transform.position - new Vector3(route[idx].x, walker.transform.position.y, route[idx].z)).magnitude > 0.5f && w < 12f) { w += Time.deltaTime; yield return null; }
            if (idx == 2) ScreenCapture.CaptureScreenshot(Path.Combine(dir, "02_walking.png"));
        }
        walker.autoTarget = null;
        yield return Shot(dir, "01_play", log);
        var cam0 = Camera.main;
        var pp = walker.transform.position;
        walker.enabled = false;
        cam0.transform.position = pp + new Vector3(0, 7.5f, -5.5f); cam0.transform.LookAt(pp + new Vector3(0, 0, -3f)); cam0.fieldOfView = 46;
        yield return Shot(dir, "03_trail_close", log);
        var palace = stage.kind == LookStudyRuins.Kind.Palace;
        var views = palace ? new[]
        {
            ("10_arena", new Vector3(0, 46, -34), new Vector3(0, 0, 6), 46f),
            ("11_hall_fire", new Vector3(0, 16, 6), new Vector3(0, 6, 36), 50f),
            ("12_gallery", new Vector3(-12, 13, -8), new Vector3(-26, 1, 6), 44f),
            ("13_court_low", new Vector3(3, 2, -26), new Vector3(0, 6, 38), 52f),
        } : new[]
        {
            ("10_arena", new Vector3(0, 46, -34), new Vector3(0, 0, 3), 46f),
            ("11_burning_house", new Vector3(-6, 12, -2), new Vector3(10, 2, 21), 42f),
            ("12_lane", new Vector3(-26, 11, -22), new Vector3(-20, 1, -12), 42f),
            ("13_low", new Vector3(-4, 1.8f, -24), new Vector3(2, 3, 18), 54f),
        };
        foreach (var v in views)
        {
            cam.transform.position = v.Item2; cam.transform.LookAt(v.Item3); cam.fieldOfView = v.Item4;
            yield return Shot(dir, v.Item1, log);
        }
        File.WriteAllLines(Path.Combine(dir, "log.txt"), log);
        Application.Quit();
    }

    IEnumerator Shot(string dir, string name, System.Collections.Generic.List<string> log)
    {
        yield return new WaitForSeconds(0.5f);
        float t = 0; int f = 0;
        while (t < 0.7f) { t += Time.unscaledDeltaTime; f++; yield return null; }
        log.Add($"{name}: {f / t:0} fps");
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
        yield return null; yield return null;
    }
#endif
}
