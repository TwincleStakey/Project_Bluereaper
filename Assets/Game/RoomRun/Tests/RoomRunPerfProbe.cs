// Dev-copy only: renders the grass room at 1080p and logs frame cost.
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RoomRun.Tests
{
    public class RoomRunPerfProbe
    {
        [UnityTest, Timeout(300000)]
        public IEnumerator Probe_GrassRoomFrameCost()
        {
            LogAssert.ignoreFailingMessages = true;
            if (RunDirector.Instance) Object.Destroy(RunDirector.Instance.gameObject);
            yield return null;
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Game/RoomRun/Scenes/RoomRun.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!RunDirector.Instance || RunDirector.Instance.Transitioning || RunDirector.Instance.Room == null) yield return null;
            var run = RunDirector.Instance;
            run.JumpTo("grass");
            yield return null;
            while (run.Transitioning) yield return null;
            for (int i = 0; i < 30; i++) yield return null;

            var cam = run.Rig.Cam;
            var rt = new RenderTexture(1920, 1080, 24);
            var tex = new Texture2D(8, 8, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            foreach (var spot in new[] { run.AreaCenter, run.AreaCenter + new Vector3(0, 0, -run.AreaSize.y / 2f + 4f) })
            {
                run.Player.PlaceAt(spot, run.AreaCenter.y);
                run.Rig.Snap();
                for (int i = 0; i < 5; i++) yield return null;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                const int n = 60;
                for (int i = 0; i < n; i++)
                {
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, 8, 8), 0, 0); // forces the GPU to finish
                    RenderTexture.active = null;
                }
                Debug.Log($"[RoomRun] PERF grass at {spot}: {sw.Elapsed.TotalMilliseconds / n:0.0} ms per 1080p frame, area {run.AreaSize}");
            }
            cam.targetTexture = null;
            Time.captureFramerate = 0;
            Object.Destroy(run.gameObject);
            Assert.Pass();
        }
    }
}
