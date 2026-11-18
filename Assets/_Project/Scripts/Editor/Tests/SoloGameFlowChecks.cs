using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BlueReaper.Core;
using BlueReaper.Network;
using BlueReaper.Player;
using BlueReaper.Stage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BlueReaper.GameFlow.Editor
{
    // Run with -batchmode -executeMethod BlueReaper.GameFlow.Editor.SoloGameFlowChecks.Run (without -quit).
    [InitializeOnLoad]
    public static class SoloGameFlowChecks
    {
        const string Running = "BlueReaper.SoloGameFlowChecks.Running";
        const string Menu = "Assets/_Project/Scenes/GameMenu.unity";
        const string Hub = "Assets/_Project/Scenes/Hub.unity";
        const string Stage = "Assets/_Project/Scenes/Stage.unity";
        const string Sandbox = "Assets/_Project/Scenes/_Sandbox/Seonggyu.unity";
        const string DynamicField = "StageField_Dynamic";
        static Stack<IEnumerator> checks;
        static double deadline;
        static int frame = -1;
        static string runtimeError;

        static SoloGameFlowChecks()
        {
            if (Application.isBatchMode && SessionState.GetBool(Running, false)) RegisterCallbacks();
        }

        public static void Run()
        {
            Require(Application.isBatchMode, "Run the checks in a separate batch editor.");
            EditorSceneManager.OpenScene(Menu);
            SessionState.SetBool(Running, true);
            RegisterCallbacks();
            EditorApplication.EnterPlaymode();
        }

        static void RegisterCallbacks()
        {
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        static void OnLog(string message, string stackTrace, LogType type)
        {
            // Unity Search can index on editor startup while the batch runner enters Play Mode.
            if (stackTrace.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup")) return;
            if (type is LogType.Error or LogType.Exception or LogType.Assert) runtimeError ??= message;
        }

        static void Tick()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Solo flow checks timed out.");
                if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
                frame = Time.frameCount;
                Require(runtimeError == null, runtimeError);
                checks ??= new Stack<IEnumerator>(new[] { CheckFlow() });
                while (checks.TryPeek(out var current))
                {
                    if (!current.MoveNext())
                    {
                        checks.Pop();
                        continue;
                    }
                    if (current.Current is IEnumerator nested)
                    {
                        checks.Push(nested);
                        continue;
                    }
                    return;
                }
                Require(runtimeError == null, runtimeError);
                Finish(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Finish(1);
            }
        }

        static IEnumerator CheckFlow()
        {
            while (!FindButton("New Game")) yield return null;
            Require(!FindButton("Continue").interactable, "Continue must be disabled without a save.");
            Submit("Settings");
            var volume = GameObject.Find("Master Volume").GetComponent<Slider>();
            float previousVolume = AudioListener.volume;
            volume.value = 0.5f;
            Require(Mathf.Approximately(AudioListener.volume, 0.5f), "Volume setting did not apply.");
            volume.value = previousVolume;
            Submit("Back");
            Submit("New Game");
            Require(!FindButton("Start Solo").interactable, "Solo started without a character.");
            Submit("Create Warrior");
            Submit("Create Warrior");
            Submit("Next Character");
            Submit("Start Solo");
            Require(SessionService.Instance.IsSolo && SessionService.Instance.InSession, "Local solo host did not start.");
            while (!FindButton("Depart") || !SoloPlayer()) yield return null;
            var originalInputSettings = InputSystem.settings;
            var batchInputSettings = Object.Instantiate(originalInputSettings);
            // Batch mode has no focused Game View to receive the simulated keyboard events.
            batchInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            batchInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = batchInputSettings;
            Application.runInBackground = true;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var firstHubPlayer = CheckReturnedToHub(null);
            var spawn = GameObject.Find("Player Spawn");
            Require(spawn && PlanarDistance(firstHubPlayer.transform.position, spawn.transform.position) < 0.2f,
                "Character did not spawn at the base entrance.");
            yield return MovePlayer(firstHubPlayer, keyboard);
            Require(!FindButton("Depart").interactable, "Depart was available before field selection.");
            Submit("Select Stage Dynamic");
            Submit("Depart");
            while (!FindField(DynamicField)) yield return null;
            var player = SoloPlayer();
            Require(firstHubPlayer == null && !SceneManager.GetSceneByPath(Hub).isLoaded,
                "Hub remained active after entering the stage.");
            Require(player && player.name == "Warrior 1" && player.Current == player.MaxHealth, "Selected player was not initialized.");
            var camera = Camera.main ? Camera.main.GetComponent<TopDownCamera>() : null;
            Require(camera && new SerializedObject(camera).FindProperty("target").objectReferenceValue == player.transform,
                "Stage camera is not following the selected player.");
            var entrance = FindField(DynamicField).GetComponentInChildren<PlayerSpawnEntry>();
            Vector3 offset = player.transform.position - entrance.transform.position;
            Require(new Vector2(offset.x, offset.z).sqrMagnitude < 0.1f, "Player did not enter at the field marker.");
            var hpText = GameObject.Find("Player HP")?.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            Require(hpText && hpText.text == $"{player.Current:0}/{player.MaxHealth:0}", "Player HUD is not bound.");
            Debug.Log("SOLO CHECK PASS 1/5: Title, controls, character selection, session and stage entry.");

            yield return PrepareDynamicCombat(FindField(DynamicField), player);
            yield return CheckDynamicClear(player);
            var afterClear = CheckReturnedToHub(player);
            yield return MovePlayer(afterClear, keyboard);
            Debug.Log("SOLO CHECK PASS 2/5: Cleared the dynamic field and returned through the portal to a playable hub.");

            Submit("Depart");
            while (!FindField(DynamicField)) yield return null;
            var retry = SoloPlayer();
            Require(afterClear == null && retry && retry.Current == retry.MaxHealth, "Clear retry did not create a fresh player.");
            yield return PrepareDynamicCombat(FindField(DynamicField), retry);
            Hit(retry);
            while (!HasText("Defeated. Ready to redeploy.")) yield return null;
            var afterDefeat = CheckReturnedToHub(retry);
            yield return MovePlayer(afterDefeat, keyboard);
            Debug.Log("SOLO CHECK PASS 3/5: Clear redeploy and HP-zero return to a playable hub.");

            Submit("Depart");
            while (!FindField(DynamicField)) yield return null;
            player = SoloPlayer();
            Require(afterDefeat == null && player && player.Current == player.MaxHealth, "Defeat retry did not create a fresh player.");
            yield return PrepareDynamicCombat(FindField(DynamicField), player);
            Hit(player);
            while (!HasText("Defeated. Ready to redeploy.")) yield return null;
            CheckReturnedToHub(player);
            Submit("Back to Lobby");
            while (!FindButton("Start Solo") || !FindButton("Start Solo").interactable) yield return null;
            InputSystem.RemoveDevice(keyboard);
            InputSystem.settings = originalInputSettings;
            Object.Destroy(batchInputSettings);
            Require(!SceneManager.GetSceneByPath(Hub).isLoaded && !SoloPlayer(), "Leaving the session retained the hub.");
            Require(!SessionService.Instance.InSession, "Returning to lobby did not stop the host.");
            Debug.Log("SOLO CHECK PASS 4/5: Defeat redeploy and clean session shutdown.");

            Object.Destroy(SessionService.Instance.gameObject);
            yield return null;
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode(Sandbox, new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone || !FindField(DynamicField)) yield return null;
            Require(SoloPlayer(), "Sandbox player was not initialized.");
            yield return PrepareDynamicCombat(FindField(DynamicField), SoloPlayer());
            Debug.Log("SOLO CHECK PASS 5/5: Sandbox auto-start, readiness and first dynamic wave.");
        }

        static IEnumerator PrepareDynamicCombat(StageField field, Health player)
        {
            var spawner = field.GetComponent<EnemySpawner>();
            spawner.SpawnInterval = 0.15f;
            var pads = field.GetComponentsInChildren<StageReadyPad>();
            Require(pads.Length == 3 && LiveEnemies().Length == 0,
                "Ready pads were missing or the field spawned enemies before readiness.");
            player.GetComponent<PlayerMotor>().Teleport(pads[0].transform.position, Quaternion.identity);
            while (pads[0].gameObject.activeSelf) yield return null;
            while (LiveEnemies().Length == 0) yield return null;
            Require(LiveEnemies().Length == 4, "The first dynamic wave should spawn 4 configured enemies.");
        }

        static IEnumerator CheckDynamicClear(Health player)
        {
            var field = FindField(DynamicField);
            var motor = player.GetComponent<PlayerMotor>();
            motor.Teleport(Vector3.zero, Quaternion.identity);
            player.Invincible = true;

            int remaining = new SerializedObject(field).FindProperty("purificationGoal").intValue;
            StageExitPortal portal;
            while (!(portal = Object.FindFirstObjectByType<StageExitPortal>()))
            {
                foreach (var enemy in LiveEnemies())
                {
                    if (remaining <= 0) break;
                    Hit(enemy.GetComponent<Health>());
                    remaining--;
                }
                yield return null;
            }
            player.Invincible = false;
            var before = player.Current;
            Require(player.TakeDamage(float.MaxValue) == DamageResult.Ignored, "Cleared stage still accepts damage.");
            player.Kill();
            Require(player.Current == before && Vector3.Distance(portal.transform.position, player.transform.position) < 0.3f,
                "Cleared stage still accepts death or the solo portal is off center.");
            yield return null;
            Require(FindField("StageField_Dynamic"), "Initial portal overlap should wait for a new entry.");
            motor.Teleport(portal.transform.position + Vector3.right * 5f, Quaternion.identity);
            float leftAt = Time.time;
            while (Time.time - leftAt < 0.1f) yield return null;
            motor.Teleport(portal.transform.position, Quaternion.identity);
            while (!HasText("Stage cleared.")) yield return null;
            Debug.Log("DYNAMIC SOLO PASS: pad, batch size, combat lock and portal return.");
        }

        static Button FindButton(string name) => GameObject.Find(name)?.GetComponent<Button>();
        static bool HasText(string value) => Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(text => text.text == value);
        static StageField FindField(string name) => Object.FindObjectsByType<StageField>(FindObjectsSortMode.None)
            .FirstOrDefault(field => field.name == name + "(Clone)");
        static Enemy[] LiveEnemies() => Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None)
            .Where(enemy => !enemy.GetComponent<Health>().IsDead).ToArray();
        static Health SoloPlayer() => Object.FindFirstObjectByType<PlayerLife>()?.GetComponent<Health>();

        static void Hit(Health health)
        {
            health.Invincible = false;
            float shield = health.TryGetComponent<Shield>(out var component) ? component.CurrentShield : 0f;
            Require(health.TakeDamage(health.MaxHealth + shield) == DamageResult.Killed, "Test damage did not reach HP zero.");
        }

        static void Submit(string name)
        {
            var button = FindButton(name);
            Require(button && button.interactable && EventSystem.current, "Could not submit " + name);
            button.OnSubmit(new BaseEventData(EventSystem.current));
        }

        static Health CheckReturnedToHub(Health previousPlayer)
        {
            var hubPlayer = SoloPlayer();
            var camera = Camera.main ? Camera.main.GetComponent<TopDownCamera>() : null;
            Require(previousPlayer == null && !SceneManager.GetSceneByPath(Stage).isLoaded
                && SceneManager.GetActiveScene().path == Hub && hubPlayer && hubPlayer.gameObject.scene.path == Hub
                && hubPlayer.name == "Warrior 1" && hubPlayer.Current == hubPlayer.MaxHealth
                && LiveEnemies().Length == 0 && SessionService.Instance.IsSolo && SessionService.Instance.InSession,
                "Hub transition left combat objects or lost the selected character or session.");
            Require(camera && new SerializedObject(camera).FindProperty("target").objectReferenceValue == hubPlayer.transform,
                "Hub camera is not following the selected player.");
            return hubPlayer;
        }

        static IEnumerator MovePlayer(Health player, Keyboard keyboard)
        {
            Vector3 start = player.transform.position;
            float until = Time.realtimeSinceStartup + 3;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            while (PlanarDistance(player.transform.position, start) < 0.25f && Time.realtimeSinceStartup < until)
                yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            Require(PlanarDistance(player.transform.position, start) >= 0.25f, "Keyboard input did not move the hub player.");
            yield return null;
        }

        static float PlanarDistance(Vector3 first, Vector3 second) =>
            Vector2.Distance(new Vector2(first.x, first.z), new Vector2(second.x, second.z));

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static void Finish(int code)
        {
            SessionState.EraseBool(Running);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            EditorApplication.Exit(code);
        }
    }
}
