// Dev-copy only: hops between stage rooms and logs graphics memory, to look for leaks across loads.
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RoomRun.Tests
{
    public class RoomRunLeakProbe
    {
        [UnityTest, Timeout(600000)]
        public IEnumerator Probe_MemoryAcrossRoomLoads()
        {
            LogAssert.ignoreFailingMessages = true;
            if (RunDirector.Instance) Object.Destroy(RunDirector.Instance.gameObject);
            yield return null;
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Game/RoomRun/Scenes/RoomRun.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!RunDirector.Instance || RunDirector.Instance.Transitioning || RunDirector.Instance.Room == null) yield return null;
            var run = RunDirector.Instance;
            run.Player.DebugInvulnerable = true;
            var cam = run.Rig.Cam;
            var rt = new RenderTexture(1280, 720, 24);
            string[] order = { "valley", "palace", "underworld", "palace", "underworld", "valley", "village", "grass", "underworld", "palace", "underworld", "grass" };
            for (int i = 0; i < order.Length; i++)
            {
                run.JumpTo(order[i]);
                yield return null;
                while (run.Transitioning) yield return null;
                for (int f = 0; f < 20; f++) { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; yield return null; }
                Resources.UnloadUnusedAssets();
                System.GC.Collect();
                yield return null;
                Debug.Log($"[RoomRun] MEM {i} {order[i]}: gfx {Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576} MB, " +
                          $"RTs {Resources.FindObjectsOfTypeAll<RenderTexture>().Length}, meshes {Resources.FindObjectsOfTypeAll<Mesh>().Length}, " +
                          $"mats {Resources.FindObjectsOfTypeAll<Material>().Length}, tex {Resources.FindObjectsOfTypeAll<Texture2D>().Length}, " +
                          $"total {Profiler.GetTotalAllocatedMemoryLong() / 1048576} MB");
            }
            Object.Destroy(run.gameObject);
            Assert.Pass();
        }
    }
}

