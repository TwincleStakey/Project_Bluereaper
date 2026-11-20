// Applies the stage mood at startup and runs the build-only capture route.
using System.Collections;
using System.IO;
using UnityEngine;

public class LookStudyValleyBoot : MonoBehaviour
{
    public LookStudyWalker walker;

    void OnEnable()
    {
        LookStudyValley.ApplyMood(RenderSettings.sun);
        if (walker) { walker.heightAt = LookStudyValley.Height; walker.surfaceAt = LookStudyValley.SurfaceAt; walker.trailCenter = new Vector2(0, 0); walker.trailSize = 96f; walker.bounds = new Rect(-20, -14, 40, 30); }
    }

#if !UNITY_EDITOR
    IEnumerator Start()
    {
        var args = System.Environment.GetCommandLineArgs();
        int t = System.Array.IndexOf(args, "-lsvalleytest");
        if (t < 0 || t + 1 >= args.Length) yield break;
        string dir = args[t + 1];
        Directory.CreateDirectory(dir);
        Application.runInBackground = true;
        var log = new System.Collections.Generic.List<string>();
        var cam = Camera.main;
        var route = new[] { new Vector3(14, 0, 6), new Vector3(8, 0, 2), new Vector3(4, 0, 10), new Vector3(10, 0, 12), new Vector3(16, 0, 15), new Vector3(9, 0, 11) };
        walker.transform.position = new Vector3(19, LookStudyValley.Height(19, 5), 5);
        yield return new WaitForSeconds(3f);
        yield return Shot(dir, "01_start_walker", log);
        int idx = 0;
        walker.autoTarget = () => route[idx];
        for (idx = 0; idx < route.Length; idx++)
        {
            float wait = 0;
            bool took = false;
            while ((walker.transform.position - new Vector3(route[idx].x, walker.transform.position.y, route[idx].z)).magnitude > 0.5f && wait < 14f)
            {
                wait += Time.deltaTime;
                if (idx == 1 && !took && wait > 0.8f) { took = true; ScreenCapture.CaptureScreenshot(Path.Combine(dir, "03_wading.png")); }
                yield return null;
            }
            log.Add($"waypoint {idx} at {walker.transform.position} after {wait:0.0}s");
            if (idx == 2) yield return Shot(dir, "02_grass_trail", log);
            if (idx == 4) yield return Shot(dir, "04_after_wading", log);
        }
        walker.autoTarget = null;
        walker.enabled = false;
        var views = new[]
        {
            ("10_arena", new Vector3(1, 44, -33), new Vector3(1, 0, 3), 40f),
            ("11_upper_pool", new Vector3(24, 20, 8), new Vector3(27, 0, 26), 38f),
            ("12_fall", new Vector3(27, 7, 18), new Vector3(36, 5, 38), 48f),
            ("13_gourd_pool", new Vector3(-4, 17, -22), new Vector3(0, 0, -3), 38f),
            ("14_shelf", new Vector3(-22, 14, -8), new Vector3(-18, 0, 8), 40f),
            ("15_low_stream", new Vector3(-13, 1.4f, -23), new Vector3(3, 1.5f, -7), 52f),
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
        yield return new WaitForSeconds(0.4f);
        float t = 0; int f = 0;
        while (t < 0.7f) { t += Time.unscaledDeltaTime; f++; yield return null; }
        log.Add($"{name}: {f / t:0} fps");
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
        yield return null; yield return null;
    }
#endif
}
