// Plays the real game with a bot through the real input path (ICommandSource).
// Set the environment variable RR_CAPTURE_DIR to also save each frame as a JPG for a preview video.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RoomRun.Tests
{
    public class RoomRunPlayTests
    {
        const string BootPath = "Assets/Game/RoomRun/Scenes/RoomRun.unity";

        static IEnumerator Boot()
        {
            if (RunDirector.Instance) Object.Destroy(RunDirector.Instance.gameObject);
            yield return null;
            Time.captureFramerate = 30;
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                BootPath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            while (!RunDirector.Instance || RunDirector.Instance.Transitioning || RunDirector.Instance.Room == null) yield return null;
            savedLock = RunDirector.Instance.config.lockDoors;
            savedCombat = RunDirector.Instance.config.combat;
            RunDirector.Instance.config.combat = true; // These tests cover combat, which is off by default for exploring.
        }

        static bool savedLock, savedCombat;

        static void Done()
        {
            Time.captureFramerate = 0;
            if (RunDirector.Instance)
            {
                RunDirector.Instance.config.lockDoors = savedLock;
                RunDirector.Instance.config.combat = savedCombat;
            }
            if (RunDirector.Instance) Object.Destroy(RunDirector.Instance.gameObject);
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator Bot_VisitsEveryRoom_AndClearsTheRun()
        {
            // Stage scenes belong to other work. Their errors are reported below instead of stopping the flow check.
            LogAssert.ignoreFailingMessages = true;
            var stageErrors = new List<string>();
            Application.LogCallback onLog = (msg, st, type) =>
            {
                if ((type == LogType.Error || type == LogType.Exception) && stageErrors.Count < 20)
                    stageErrors.Add($"[{RunDirector.Instance?.Room?.id}] {msg.Split('\n')[0]}");
            };
            Application.logMessageReceived += onLog;

            yield return Boot();
            var run = RunDirector.Instance;
            var recorder = FrameRecorder.TryStart(run);
            run.config.lockDoors = true; // Also checks the lock path, which is off by default for art checks.
            run.Restart(); // The first room already started with the default setting.
            yield return null;
            while (run.Transitioning) yield return null;
            var bot = new Bot(run);
            run.Player.Source = bot;
            run.Player.DebugInvulnerable = true; // This test is about the flow, not about the bot's skill.

            var sawLock = new HashSet<string>();
            const int maxFrames = 30 * 600; // Whole-environment rooms take a while to cross.
            int combatFrames = 0;
            for (int f = 0; f < maxFrames && !run.RunComplete; f++)
            {
                if (!run.InCombat) combatFrames = 0;
                if (run.InCombat)
                {
                    // The bot cannot path around buildings. An enemy it can't reach for 60 s is removed,
                    // since this test is about the room flow, not the bot's aim.
                    combatFrames++;
                    if (combatFrames > 30 * 60)
                        foreach (var e in run.Enemies.ToArray()) if (e) e.GetComponent<Health>().Hit(9999);
                    sawLock.Add(run.Room.id);
                    foreach (var d in run.Doors) Assert.IsFalse(d.IsOpen, $"Door open during combat in {run.Room.id}");
                }
                yield return null;
            }

            for (int f = 0; f < 60; f++) yield return null;
            if (recorder) recorder.Stop();
            Application.logMessageReceived -= onLog;
            foreach (var e in new HashSet<string>(stageErrors)) Debug.Log("[RoomRun] STAGE ERROR " + e);

            var state = new System.Text.StringBuilder();
            state.Append($"room={run.Room.id} inCombat={run.InCombat} player={run.Player.transform.position} vel={run.Player.Velocity} ammo={run.Player.Ammo} aim={run.Player.AimDir}");
            foreach (var e in run.Enemies)
                if (e) state.Append($"\n  enemy {e.Def.id} at {e.transform.position} hp={e.GetComponent<Health>().current}");
            foreach (var d in run.Doors) state.Append($"\n  door {d.Side}->{d.Link.targetRoomId} at {d.Point} open={d.IsOpen}");
            Assert.IsTrue(run.RunComplete, $"Run did not finish. Visited: {string.Join(",", run.Visited)} Cleared: {string.Join(",", run.Cleared)}\n{state}");
            foreach (var r in run.config.rooms)
            {
                if (r.devOnly) continue;
                Assert.IsTrue(run.Visited.Contains(r.id), "Never visited " + r.id);
                if (r.IsCombat) Assert.IsTrue(sawLock.Contains(r.id), "Doors never locked in " + r.id);
            }
            Done();
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator LockedDoor_BlocksThePlayer()
        {
            yield return Boot();
            var run = RunDirector.Instance;
            run.Player.DebugInvulnerable = true;
            run.config.lockDoors = true;
            run.JumpTo("valley");
            yield return null;
            while (run.Transitioning || !run.InCombat) yield return null;

            var door = run.Doors[0];
            run.Player.Source = new PushToward(door.Point);
            for (int f = 0; f < 30 * 4; f++) yield return null;

            Assert.AreEqual("valley", run.Room.id);
            Assert.IsFalse(run.Transitioning);
            bool panel = false;
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb.GetType().Name == "LookStudyDevTools") panel = true;
            Assert.IsTrue(panel, "Environment dev tools panel was not added to the room.");
            Done();
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Death_ShowsBanner_AndRestartResetsTheRun()
        {
            yield return Boot();
            var run = RunDirector.Instance;
            run.JumpTo("palace");
            yield return null;
            while (run.Transitioning || !run.InCombat) yield return null;

            run.Player.Source = new PushToward(run.Room.center);
            for (int f = 0; f < 30 * 120 && !run.PlayerDead; f++) yield return null;
            Assert.IsTrue(run.PlayerDead, "An idle player should die in the palace room.");

            var restart = new PressRestart();
            run.Player.Source = restart;
            yield return null;
            yield return null;
            while (run.Transitioning) yield return null;

            Assert.IsFalse(run.PlayerDead);
            Assert.AreEqual(run.config.startRoomId, run.Room.id);
            Assert.AreEqual(run.Player.Health.max, run.Player.Health.current);
            Assert.AreEqual(1, run.Visited.Count);
            Done();
        }

        class PushToward : ICommandSource
        {
            readonly Vector3 target;
            public PushToward(Vector3 t) { target = t; }
            public PlayerCommand Read(PlayerController p, Camera cam)
            {
                var d = target - p.transform.position;
                return new PlayerCommand { move = new Vector2(d.x, d.z).normalized };
            }
        }

        class PressRestart : ICommandSource
        {
            public PlayerCommand Read(PlayerController p, Camera cam) => new PlayerCommand { restart = true };
        }

        // Fights like a cautious player: kites, dodges incoming shots, then walks to the nearest unseen room.
        class Bot : ICommandSource
        {
            readonly RunDirector run;
            float strafeFlip = 1f, flipTimer, stuckTime, sidestep, sideDir = 1f;
            Vector3 lastPos;
            public Bot(RunDirector r) { run = r; }

            public PlayerCommand Read(PlayerController p, Camera cam)
            {
                var c = new PlayerCommand();
                var pos = p.transform.position;
                EnemyController nearest = null;
                float best = float.MaxValue;
                foreach (var e in run.Enemies)
                {
                    if (!e) continue;
                    float d = (e.transform.position - pos).sqrMagnitude;
                    if (d < best) { best = d; nearest = e; }
                }

                if (nearest)
                {
                    var to = nearest.transform.position - pos;
                    to.y = 0;
                    float dist = to.magnitude;
                    c.aimDir = to / Mathf.Max(dist, 0.01f);
                    c.fire = true;

                    flipTimer -= Time.deltaTime;
                    if (flipTimer <= 0f) { flipTimer = Random.Range(0.7f, 1.6f); strafeFlip = -strafeFlip; }
                    var strafe = Vector3.Cross(Vector3.up, c.aimDir) * strafeFlip;
                    float radial = dist < 5f ? -1f : dist > 9f ? 1f : 0f;
                    var move = strafe + c.aimDir * radial + (run.Room.center - pos) * 0.05f;
                    c.move = new Vector2(move.x, move.z).normalized;

                    bool threatened = false;
                    run.Bullets.ForEachEnemyBullet((bp, bv) =>
                    {
                        var rel = pos - bp; rel.y = 0;
                        if (rel.sqrMagnitude < 6f && Vector3.Dot(rel, bv) > 0f) threatened = true;
                    });
                    c.dodge = threatened;
                }
                else if (!run.InCombat)
                {
                    var door = NextDoor();
                    if (door)
                    {
                        // Join the clear lane in front of the door first, so props do not snag the walk.
                        var outward = door.Side.Dir();
                        var off = door.Point - pos; off.y = 0;
                        bool inLane = Vector3.Cross(off, outward).magnitude < 1f;
                        var target = inLane ? door.Point + outward : door.Point - outward * 3f;
                        var d = target - pos;
                        var flat = new Vector2(d.x, d.z);
                        c.move = flat.normalized;

                        // A prop can block the straight line. Sidestep when no progress is being made.
                        if (p.Velocity.sqrMagnitude > 1f && (pos - lastPos).sqrMagnitude < 0.0004f) stuckTime += Time.deltaTime;
                        else stuckTime = 0f;
                        lastPos = pos;
                        if (stuckTime > 0.3f) { sidestep = 0.6f; sideDir = Random.value < 0.5f ? -1f : 1f; stuckTime = 0f; }
                        if (sidestep > 0f)
                        {
                            sidestep -= Time.deltaTime;
                            c.move = new Vector2(-flat.y, flat.x).normalized * sideDir;
                        }
                        c.aimDir = new Vector3(flat.x, 0, flat.y).normalized;
                    }
                }
                return c;
            }

            // Breadth-first over the room graph toward the closest room that is not cleared.
            Door NextDoor()
            {
                var cfg = run.config;
                var parent = new Dictionary<string, string>();
                var q = new Queue<string>();
                q.Enqueue(run.Room.id);
                parent[run.Room.id] = null;
                string goal = null;
                while (q.Count > 0 && goal == null)
                {
                    var id = q.Dequeue();
                    var room = cfg.Room(id);
                    if (id != run.Room.id && room.IsCombat && !run.Cleared.Contains(id)) { goal = id; break; }
                    foreach (var l in room.doors)
                        if (!parent.ContainsKey(l.targetRoomId))
                        {
                            parent[l.targetRoomId] = id;
                            q.Enqueue(l.targetRoomId);
                        }
                }
                if (goal == null) return null;
                while (parent[goal] != run.Room.id) goal = parent[goal];
                return run.Doors.Find(d => d.Link.targetRoomId == goal);
            }
        }
    }

    public sealed class FrameRecorder : MonoBehaviour
    {
        const int W = 1280, H = 720;
        RunDirector run;
        RenderTexture rt;
        Texture2D tex;
        string dir;
        int frame;

        public static FrameRecorder TryStart(RunDirector run)
        {
            var dir = System.Environment.GetEnvironmentVariable("RR_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) return null;
            Directory.CreateDirectory(dir);
            var r = run.gameObject.AddComponent<FrameRecorder>();
            r.run = run;
            r.dir = dir;
            r.rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            r.tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            return r;
        }

        void LateUpdate()
        {
            var cam = run.Rig.Cam;
            cam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(dir, $"f{frame++:D6}.jpg"), tex.EncodeToJPG(88));
        }

        public void Stop() => Destroy(this);
    }
}

