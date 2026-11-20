using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlueReaper.Boss;
using BlueReaper.BulletPatterns;
using BlueReaper.Core;
using BlueReaper.Data;
using BlueReaper.HitBox;
using BlueReaper.Network;
using BlueReaper.Player;
using BlueReaper.Stage;
using BlueReaper.UI;
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
            CheckStageCatalog();
            CheckBossPartyHealth();
            yield return null;
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
            var hpBar = GameObject.Find("Player HP")?.GetComponent<BlueReaper.UI.HPBarUI>();
            var hpFill = hpBar ? hpBar.transform.Find("Gauge")?.GetComponent<Image>() : null;
            Require(hpFill && hpFill.fillMethod == Image.FillMethod.Vertical && Mathf.Approximately(hpFill.fillAmount, 1f),
                "Player HP globe is not bound.");
            Debug.Log("SOLO CHECK PASS 1/5: Title, controls, character selection, session and stage entry.");

            yield return PrepareDynamicCombat(FindField(DynamicField), player);
            yield return CheckHud(player);
            yield return CheckDynamicClear(player);
            var afterClear = CheckReturnedToHub(player);
            yield return MovePlayer(afterClear, keyboard);
            Debug.Log("SOLO CHECK PASS 2/5: Cleared the dynamic field and returned through the portal to a playable hub.");

            using var flow = new SerializedObject(Object.FindFirstObjectByType<GameFlow>());
            var catalogSlot = flow.FindProperty("stageCatalog");
            var originalCatalog = (StageCatalogData)catalogSlot.objectReferenceValue;
            var testCatalog = Object.Instantiate(originalCatalog);
            using var testCatalogData = new SerializedObject(testCatalog);
            var entries = testCatalogData.FindProperty("entries");
            var stageSlot = entries.GetArrayElementAtIndex(0).FindPropertyRelative("Stage");
            var originalStage = (StageData)stageSlot.objectReferenceValue;
            var twoFields = Object.Instantiate(originalStage);
            twoFields.name = originalStage.name;
            twoFields.Fields = new[] { originalStage.Fields[0], originalStage.Fields[0] };
            stageSlot.objectReferenceValue = twoFields;
            testCatalogData.ApplyModifiedPropertiesWithoutUndo();
            catalogSlot.objectReferenceValue = testCatalog;
            flow.ApplyModifiedPropertiesWithoutUndo();

            Submit("Depart");
            while (!FindField(DynamicField)) yield return null;
            var retry = SoloPlayer();
            Require(afterClear == null && retry && retry.Current == retry.MaxHealth, "Clear retry did not create a fresh player.");
            var firstField = FindField(DynamicField);
            yield return PrepareDynamicCombat(firstField, retry);
            yield return CheckNextFieldHud(firstField, retry);
            yield return CheckDynamicClear(retry, wipeAfterClear: true);
            var afterWipe = CheckReturnedToHub(retry);
            catalogSlot.objectReferenceValue = originalCatalog;
            flow.ApplyModifiedPropertiesWithoutUndo();
            Object.Destroy(testCatalog);
            Object.Destroy(twoFields);
            yield return MovePlayer(afterWipe, keyboard);
            Debug.Log("SOLO CHECK PASS 3/5: Redeployed a two-field hunt and returned after a post-clear wipe.");

            Submit("Depart");
            while (!FindField(DynamicField)) yield return null;
            player = SoloPlayer();
            Require(afterWipe == null && player && player.Current == player.MaxHealth, "Post-clear retry did not create a fresh player.");
            yield return PrepareDynamicCombat(FindField(DynamicField), player);
            Hit(player);
            while (!HasText("Defeated. Ready to redeploy.")) yield return null;
            CheckReturnedToHub(player);
            yield return CheckBossStage();
            yield return CheckRequiredElites(originalCatalog);
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

        static IEnumerator CheckRequiredElites(StageCatalogData originalCatalog)
        {
            const string elitePath = "Assets/BlueReaperEliteCheck.prefab";
            const string fieldPath = "Assets/BlueReaperEliteFieldCheck.prefab";
            Require(!AssetDatabase.LoadMainAssetAtPath(elitePath) && !AssetDatabase.LoadMainAssetAtPath(fieldPath),
                "An asset already exists at a temporary Elite fixture path.");
            var catalog = Object.Instantiate(originalCatalog);
            var stage = Object.Instantiate(originalCatalog.Entries[0].Stage);
            using var flow = new SerializedObject(Object.FindFirstObjectByType<GameFlow>());
            var slot = flow.FindProperty("stageCatalog");
            try
            {
                var elite = CreateSoloElitePrefab(elitePath);
                Require(elite, "Could not create the test Elite prefab.");
                Require(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(stage.Fields[0]), fieldPath),
                    "Could not create the Elite hunt field prefab.");
                var fieldPrefab = AssetDatabase.LoadAssetAtPath<StageField>(fieldPath);
                string fieldName = fieldPrefab.name;
                using var fieldSettings = new SerializedObject(fieldPrefab);
                fieldSettings.FindProperty("huntReferenceKills").intValue = 4;
                fieldSettings.FindProperty("eveningPercent").intValue = 25;
                fieldSettings.FindProperty("nightPercent").intValue = 75;
                fieldSettings.ApplyModifiedPropertiesWithoutUndo();
                using var spawnSettings = new SerializedObject(fieldPrefab.GetComponent<EnemySpawner>());
                var elites = spawnSettings.FindProperty("requiredElites");
                elites.arraySize = 1;
                var entry = elites.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("Prefab").objectReferenceValue = elite;
                entry.FindPropertyRelative("Count").intValue = 2;
                entry.FindPropertyRelative("StartPhase").enumValueIndex = (int)HuntPhase.Evening;
                spawnSettings.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(fieldPrefab);

                stage.Fields = new[] { fieldPrefab };
                using var serializedCatalog = new SerializedObject(catalog);
                serializedCatalog.FindProperty("entries").GetArrayElementAtIndex(0)
                    .FindPropertyRelative("Stage").objectReferenceValue = stage;
                serializedCatalog.ApplyModifiedPropertiesWithoutUndo();
                slot.objectReferenceValue = catalog;
                flow.ApplyModifiedPropertiesWithoutUndo();

                Submit("Select Stage Dynamic");
                Submit("Depart");
                StageField field;
                while (!(field = FindField(fieldName))) yield return null;
                var player = SoloPlayer();
                player.Invincible = true;
                yield return PrepareDynamicCombat(field, player);

                var spawner = field.GetComponent<EnemySpawner>();
                spawner.MaxAliveEnemiesPerPlayer = 1;
                Hit(LiveEnemies().First(enemy => enemy.Rank == EnemyRank.Normal).GetComponent<Health>());
                float retryAtCapacity = Time.time + spawner.SpawnInterval * 2f;
                while (Time.time < retryAtCapacity) yield return null;
                Require(!LiveEnemies().Any(enemy => enemy.Rank == EnemyRank.Elite)
                    && !Object.FindFirstObjectByType<StageExitPortal>(), "Required Elites bypassed quota or cleared at capacity.");

                spawner.MaxAliveEnemiesPerPlayer = 8;
                Enemy[] planned;
                while ((planned = LiveEnemies().Where(enemy => enemy.Rank == EnemyRank.Elite).ToArray()).Length < 2)
                    yield return null;
                Require(planned.Length == 2, "The field did not spawn the finite Elite quota.");
                Hit(planned[0].GetComponent<Health>());
                Require(!Object.FindFirstObjectByType<StageExitPortal>(), "The first Elite incorrectly cleared the field.");
                Hit(planned[1].GetComponent<Health>());
                Require(Object.FindFirstObjectByType<StageExitPortal>()
                    && LiveEnemies().Any(enemy => enemy.Rank == EnemyRank.Normal), "The last Elite did not open the portal with normals remaining.");

                player.Kill();
                while (!HasText("Stage cleared.")) yield return null;
                CheckReturnedToHub(player);
                Debug.Log("ELITE HUNT PASS: planned quota, spawn retry after capacity, and highest-rank portal clear.");
            }
            finally
            {
                slot.objectReferenceValue = originalCatalog;
                flow.ApplyModifiedPropertiesWithoutUndo();
                Object.Destroy(catalog);
                Object.Destroy(stage);
                AssetDatabase.DeleteAsset(fieldPath);
                AssetDatabase.DeleteAsset(elitePath);
            }
        }

        static Enemy CreateSoloElitePrefab(string path)
        {
            var contents = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Monsters/Monster_1.prefab");
            try
            {
                foreach (var networkBehaviour in contents.GetComponentsInChildren<Unity.Netcode.NetworkBehaviour>(true))
                    Object.DestroyImmediate(networkBehaviour);
                foreach (var networkObject in contents.GetComponentsInChildren<Unity.Netcode.NetworkObject>(true))
                    Object.DestroyImmediate(networkObject);

                using var enemyData = new SerializedObject(contents.GetComponent<Enemy>());
                enemyData.FindProperty("rank").enumValueIndex = (int)EnemyRank.Elite;
                enemyData.ApplyModifiedPropertiesWithoutUndo();
                var asset = PrefabUtility.SaveAsPrefabAsset(contents, path);
                return asset ? asset.GetComponent<Enemy>() : null;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static void CheckStageCatalog()
        {
            using var flow = new SerializedObject(Object.FindFirstObjectByType<GameFlow>());
            var authored = (StageCatalogData)flow.FindProperty("stageCatalog").objectReferenceValue;
            Require(authored && authored.GetValidationError() == null, "The menu catalog is missing or invalid.");
            var catalog = Object.Instantiate(authored);
            try
            {
                using var serialized = new SerializedObject(catalog);
                var entries = serialized.FindProperty("entries");
                Require(entries.arraySize > 1, "The catalog must include both battle fixtures.");
                var first = catalog.Entries[0].Stage;
                var second = catalog.Entries[1].Stage;
                Require(catalog.IsOpen(first.StageId) && catalog.IsOpen(second.StageId), "Both fixtures must be open.");
                entries.MoveArrayElement(0, 1);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Require(catalog.TryResolve(first.StageId, out var sameStage) && sameStage == first,
                    "Reordering the catalog changed the meaning of a stage ID.");

                var firstEntry = entries.GetArrayElementAtIndex(0);
                firstEntry.FindPropertyRelative("OpenByDefault").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Require(!catalog.IsOpen(second.StageId) && catalog.TryResolve(second.StageId, out var closedStage)
                    && closedStage == second, "Catalog visibility affects host selection and must preserve ID resolution.");

                firstEntry.FindPropertyRelative("Stage").objectReferenceValue = first;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Require(catalog.GetValidationError() != null && !catalog.TryResolve(first.StageId, out _),
                    "An ambiguous stage ID was resolved.");
                Require(!catalog.TryResolve(0, out _) && !catalog.TryResolve(int.MaxValue, out _),
                    "A missing or reserved stage ID was resolved.");
            }
            finally
            {
                Object.Destroy(catalog);
            }
            Debug.Log("STAGE CATALOG PASS: stable IDs, host availability, missing and duplicate IDs.");
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

        static IEnumerator CheckHud(Health player)
        {
            var controller = Object.FindFirstObjectByType<StageController>();
            controller.ShowDialogue(new StageDialogueLine
            {
                Audience = StageDialogueAudience.Individual,
                RecipientSlot = 2,
                Text = "Only slot two"
            });
            Require(!GameObject.Find("Dialogue Row"), "Dialogue reached a different party slot.");
            for (int i = 0; i < 4; i++)
                controller.ShowDialogue(new StageDialogueLine { SpeakerSlot = 1, Text = $"Line {i}" });

            var rows = Object.FindObjectsByType<TMPro.TextMeshProUGUI>(FindObjectsSortMode.None)
                .Where(label => label.name == "Line" && label.transform.parent.name == "Dialogue Row").ToArray();
            Require(rows.Length == 3 && rows.All(label => label.text != "Line 0")
                && rows.Any(label => label.text == "Line 3"), "Latest three dialogues were not retained.");

            var first = LiveEnemies()[0];
            var last = LiveEnemies()[1];
            first.GetComponent<UnityEngine.AI.NavMeshAgent>().Warp(player.transform.position + Vector3.right * 3f);
            last.GetComponent<UnityEngine.AI.NavMeshAgent>().Warp(player.transform.position + Vector3.left * 3f);
            float before = last.GetComponent<Health>().Current;
            ReportZeroDamageContact(first, player.GetComponent<Unit>());
            ReportZeroDamageContact(last, player.GetComponent<Unit>());
            yield return null;
            yield return null;
            Require(last.GetComponent<Health>().Current == before, "Zero-damage test changed HP.");
            var follow = GameObject.Find("Enemy HP Follow").transform;
            Require(follow.Cast<Transform>().Count(bar => bar.gameObject.activeSelf) == 1,
                "Recent hit HUD should retain one normal enemy even with zero damage.");
            var camera = Camera.main;
            var rect = (RectTransform)follow;
            Vector3 world = last.transform.position + Vector3.up
                * (last.GetComponent<UnityEngine.AI.NavMeshAgent>().height * Mathf.Abs(last.transform.lossyScale.y) + .3f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, camera.WorldToScreenPoint(world), null, out var expected);
            var visible = follow.Cast<RectTransform>().Single(bar => bar.gameObject.activeSelf);
            Require(Vector2.Distance(expected, visible.anchoredPosition) < 12f, "HUD did not follow the last enemy.");
            float lastHitDeadline = Time.unscaledTime + 3.05f;
            player.Invincible = true;
            while (Time.unscaledTime < lastHitDeadline) yield return null;
            Require(follow.Cast<Transform>().All(bar => !bar.gameObject.activeSelf), "Recent hit HUD did not expire.");
            player.Invincible = false;

            var bossPrefab = AssetDatabase.LoadAssetAtPath<Enemy>("Assets/_Project/Prefabs/Boss/Boss_1.prefab");
            var boss = Object.Instantiate(bossPrefab, player.transform.position + Vector3.forward * 15f, Quaternion.identity);
            boss.GetComponent<BlueReaper.Boss.EnemyAI>().enabled = false;
            var bossHud = Object.FindFirstObjectByType<BossHUD>();
            bossHud.Init(boss);
            bossHud.Show();
            var ghost = GameObject.Find("Boss HP Ghost").GetComponent<Image>();
            var health = boss.GetComponent<Health>();
            var shield = boss.GetComponent<Shield>();
            if (shield && shield.CurrentShield > 0f) health.TakeDamage(shield.CurrentShield);
            float damage = health.MaxHealth * .02f;
            for (int i = 0; i < 7; i++) health.TakeDamage(damage);
            Require(ghost.enabled && Mathf.Approximately(ghost.fillAmount, 1f), "Boss ghost did not begin at pre-hit HP.");
            float end = Time.unscaledTime + 1.05f;
            while (Time.unscaledTime < end) yield return null;
            Require(!ghost.enabled && Mathf.Approximately(ghost.fillAmount, health.Current / health.MaxHealth),
                "Boss ghost exceeded the one-second budget or lost the last hit.");
            health.TakeDamage(damage);
            Require(ghost.enabled, "Boss ghost did not restart.");
            bossHud.Hide();
            Require(!ghost.enabled, "Hiding the boss HUD did not immediately clear the ghost.");
            boss.gameObject.SetActive(false);
            Object.Destroy(boss.gameObject);
            Debug.Log("IN-GAME HUD PASS: dialogue routing and cap, zero-damage last target and expiry, boss ghost budget and reset.");
        }

        static void ReportZeroDamageContact(Enemy enemy, Unit source)
        {
            var collider = enemy.GetComponentsInChildren<Collider>()
                .First(c => c.gameObject.layer == LayerMask.NameToLayer("Hurtbox"));
            var core = typeof(Health).Assembly;
            var attack = Activator.CreateInstance(core.GetType("BlueReaper.Core.AttackDamage"),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { 0f, 1f }, null);
            var resolve = core.GetType("BlueReaper.HitBox.HurtboxTarget").GetMethod("TakeDamage", BindingFlags.NonPublic | BindingFlags.Static);
            var result = (DamageResult)resolve.Invoke(null, new[] { enemy.GetComponent<Health>(), (object)collider, attack, source });
            Require(result == DamageResult.Ignored, "Zero damage should keep the existing DamageResult.");
        }

        static IEnumerator CheckNextFieldHud(StageField first, Health player)
        {
            player.Invincible = true;
            int remaining = new SerializedObject(first).FindProperty("purificationGoal").intValue;
            while (remaining > 0)
            {
                foreach (var enemy in LiveEnemies().Take(remaining).ToArray())
                {
                    Hit(enemy.GetComponent<Health>());
                    remaining--;
                }
                yield return null;
            }
            while (!FindButton("Stage Progress") || !FindButton("Stage Progress").enabled) yield return null;
            Submit("Stage Progress");

            StageField second;
            while (!(second = FindField(DynamicField)) || second == first) yield return null;
            var controller = Object.FindFirstObjectByType<StageController>();
            controller.ShowDialogue(new StageDialogueLine { Text = "Second field" });
            Require(GameObject.Find("Dialogue Row"), "New field dialogue was suppressed by previous field progress.");
            yield return PrepareDynamicCombat(second, player);
            player.Invincible = false;
            Debug.Log("IN-GAME HUD FIELD PASS: second field HUD reopened after the first field ended.");
        }

        static void CheckBossPartyHealth()
        {
            var controller = new GameObject("Boss Party Check").AddComponent<StageController>();
            var config = ScriptableObject.CreateInstance<StageData>();
            config.SharedHealthMultiplier = 1.2f;
            config.LastBreathHealthMultiplier = .25f;

            var first = CreatePartyMember(100f, 20f);
            var second = CreatePartyMember(200f, 0f);

            try
            {
                const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
                var stageType = typeof(StageController);
                stageType.GetField("stageData", instance).SetValue(controller, config);
                var register = stageType.GetMethod("RegisterPlayer", instance);
                register.Invoke(controller, new object[] { first });
                register.Invoke(controller, new object[] { second });
                stageType.GetMethod("PrepareBossCombat", instance).Invoke(controller, new object[] { true });

                var firstLife = first.GetComponent<PlayerLife>();
                var secondLife = second.GetComponent<PlayerLife>();
                var shield = first.GetComponent<Shield>();
                int partyHits = 0;
                controller.SharedBossHealthDamaged += _ => partyHits++;
                Require(Mathf.Approximately(firstLife.SharedBossHealth.Maximum, 360f)
                    && firstLife.SharedBossHealth == secondLife.SharedBossHealth, "The team did not receive one shared HP pool.");
                Require(first.TakeDamage(10f) == DamageResult.Absorbed && shield.CurrentShield == 10f
                    && partyHits == 0, "Shield must precede shared HP.");
                Require(first.TakeDamage(30f) == DamageResult.Applied && partyHits == 1
                    && Mathf.Approximately(controller.SharedBossHealth.Current, 340f)
                    && first.Current == 100f, "Shared damage went through the wrong HP pool.");
                Require(first.TakeDamage(1000f) == DamageResult.Applied && partyHits == 2
                    && first.Current == 25f && second.Current == 50f && firstLife.CanAct,
                    "The hit that depleted shared HP leaked excess damage into Last Breath.");
                Require(second.TakeDamage(15f) == DamageResult.Applied && second.Heal(4f) == 4f
                    && second.Current == 39f && first.Current == 25f, "Last Breath was not a recoverable personal pool.");
                Require(first.TakeDamage(26f) == DamageResult.Killed && firstLife.State == LifeState.Ghost
                    && firstLife.CanAct && firstLife.OutgoingDamageMultiplier == .3f
                    && controller.AlivePlayers.Count == 1, "Ghost mode did not preserve actions or update the living roster.");

                stageType.GetMethod("RestoreBossParty", instance).Invoke(controller, null);
                Require(firstLife.State == LifeState.Alive && first.Current == 100f && second.Current == 200f
                    && shield.CurrentShield == 0f && controller.AlivePlayers.Count == 2
                    && Mathf.Approximately(controller.SharedBossHealth.Current, 360f),
                    "Boss phase recovery did not restore the roster and shared HP while preserving shield.");
                Debug.Log("BOSS PARTY HEALTH PASS: shield, shared HP, excess damage, Last Breath, ghost and phase recovery.");
            }
            finally
            {
                controller.gameObject.SetActive(false);
                Object.Destroy(controller.gameObject);
                Object.Destroy(first.GetComponent<Unit>().Data);
                Object.Destroy(second.GetComponent<Unit>().Data);
                Object.Destroy(first.gameObject);
                Object.Destroy(second.gameObject);
                Object.Destroy(config);
            }
        }

        static Health CreatePartyMember(float hp, float shieldPoints)
        {
            var member = new GameObject("Party Health Check");
            member.SetActive(false);
            var unit = member.AddComponent<Unit>();
            var stats = ScriptableObject.CreateInstance<UnitData>();
            stats.MaxHealth = hp;
            var unitData = new SerializedObject(unit);
            unitData.FindProperty("data").objectReferenceValue = stats;
            unitData.ApplyModifiedPropertiesWithoutUndo();
            var health = member.AddComponent<Health>();
            var shield = new SerializedObject(member.AddComponent<Shield>());
            shield.FindProperty("maxShield").floatValue = shieldPoints;
            shield.FindProperty("initialShield").floatValue = shieldPoints;
            shield.ApplyModifiedPropertiesWithoutUndo();
            member.AddComponent<Unity.Netcode.NetworkObject>();
            member.AddComponent<PlayerLife>();
            member.SetActive(true);
            return health;
        }

        static IEnumerator CheckBossStage()
        {
            Submit("Select Stage BossTest");
            Submit("Depart");
            StageField field;
            while (!(field = FindField("StageField_BossTest"))) yield return null;

            var boss = LiveEnemies().Single(enemy => enemy.Rank == EnemyRank.Boss);
            var marker = field.transform.Find("Boss Spawn Point");
            Require(marker && Vector3.Distance(boss.transform.position, marker.position) < .7f
                && !field.GetComponent<EnemySpawner>() && !field.GetComponentsInChildren<StageReadyPad>().Any(),
                "Boss did not spawn once at the authored point without waves or readiness pads.");
            Require(GameObject.Find("HPBG") && GameObject.Find("Boss HP Ghost"), "Spawned boss HUD was not bound.");

            var player = SoloPlayer();
            float playerShield = player.TryGetComponent<Shield>(out var shield) ? shield.CurrentShield : 0f;
            Require(player.TakeDamage(playerShield + player.MaxHealth * .1f) == DamageResult.Applied,
                "Boss phase fixture could not lower the player's HP.");
            player.Invincible = true;
            var ai = boss.GetComponent<EnemyAI>();
            ai.Release();
            var bullet = ScriptableObject.CreateInstance<BulletPatternData>();
            bullet.BulletPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Projectiles/ExampleBullet.prefab");
            bullet.MovementType = BulletMovementType.Linear;
            bullet.LayoutCenter = Vector3.up * 6f;
            bullet.LinearSpeed = 0f;
            bullet.LinearLifetime = 2f;
            bullet.AimAtTargetEachEmission = true;
            bullet.RepeatCount = 2;
            bullet.RepeatInterval = .4f;
            var step = ScriptableObject.CreateInstance<EnemyPatternData>();
            step.Implementations = new PatternImplData[] { bullet };
            var sequence = ScriptableObject.CreateInstance<EnemyPatternSequenceData>();
            sequence.Steps = new[] { step, step };
            var execute = typeof(EnemyAI).GetMethod("ExecuteAction", BindingFlags.Instance | BindingFlags.NonPublic);
            ai.StartCoroutine((IEnumerator)execute.Invoke(ai, new object[] { sequence, new[] { player.transform } }));
            while (Object.FindObjectsByType<ProjectileHitBox>(FindObjectsSortMode.None).Length == 0) yield return null;

            var bossHealth = boss.GetComponent<Health>();
            Require(bossHealth.TakeDamage(float.MaxValue) == DamageResult.Applied
                && Mathf.Approximately(bossHealth.Current, bossHealth.MaxHealth * .5f)
                && boss.IsPhaseTransitioning, "A lethal first-phase hit bypassed the boss HP boundary.");
            Require(bossHealth.TakeDamage(float.MaxValue) == DamageResult.Ignored,
                "The boss accepted damage during the phase transition.");
            player.Kill();
            Require(player.IsDead, "Could not remove the reserved volley target during the phase transition.");
            while (boss.IsPhaseTransitioning || boss.Phase != 2) yield return null;
            Require(Mathf.Approximately(player.Current, player.MaxHealth), "Phase completion did not heal the solo player.");
            yield return null;
            Require(Object.FindObjectsByType<ProjectileHitBox>(FindObjectsSortMode.None).Length == 2,
                "Phase transition cancelled the reserved shot, returned an existing bullet or started a later step.");
            Hit(bossHealth);
            Object.Destroy(sequence);
            Object.Destroy(step);
            Object.Destroy(bullet);
            while (!Object.FindFirstObjectByType<StageExitPortal>()) yield return null;
            var portal = Object.FindFirstObjectByType<StageExitPortal>();
            Require(!GameObject.Find("HPBG"), "Boss HUD remained active after the field cleared.");
            var motor = player.GetComponent<PlayerMotor>();
            motor.Teleport(portal.transform.position + Vector3.right * 5f, Quaternion.identity);
            float outsideUntil = Time.time + .15f;
            while (Time.time < outsideUntil) yield return null;
            motor.Teleport(portal.transform.position, Quaternion.identity);
            while (!HasText("Stage cleared.")) yield return null;
            CheckReturnedToHub(player);
            Debug.Log("BOSS STAGE PASS: spawn, HP boundary, immunity, active step bullets, skipped future step, recovery, HUD and portal return.");
        }

        static IEnumerator CheckDynamicClear(Health player, bool wipeAfterClear = false)
        {
            var field = FindField(DynamicField);
            var motor = player.GetComponent<PlayerMotor>();
            motor.Teleport(Vector3.zero, Quaternion.identity);
            player.Invincible = true;

            int remaining = new SerializedObject(field).FindProperty("purificationGoal").intValue;
            var controller = Object.FindFirstObjectByType<StageController>();
            var phaseChanges = new List<HuntPhase>();
            controller.HuntPhaseChanged += phaseChanges.Add;
            Require(controller.CurrentHuntPhase == HuntPhase.Afternoon, "Hunting did not begin in the afternoon.");
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
            controller.HuntPhaseChanged -= phaseChanges.Add;
            Require(phaseChanges.SequenceEqual(new[] { HuntPhase.Evening, HuntPhase.Night }),
                "Kill progress did not cross the evening and night thresholds exactly once.");
            player.Invincible = false;
            float shield = player.TryGetComponent<Shield>(out var component) ? component.CurrentShield : 0f;
            Require(player.TakeDamage(shield + player.MaxHealth * .1f) == DamageResult.Applied,
                "Portal opening unexpectedly locked combat damage.");
            Require(Vector3.Distance(portal.transform.position, player.transform.position) < 0.3f,
                "The solo portal is off center.");
            yield return null;
            Require(FindField("StageField_Dynamic"), "Initial portal overlap should wait for a new entry.");
            if (wipeAfterClear)
            {
                player.Kill();
                while (!HasText("Stage cleared.")) yield return null;
                Debug.Log("DYNAMIC SOLO PASS: portal opened, damage accepted and post-clear wipe preserved the clear.");
                yield break;
            }
            motor.Teleport(portal.transform.position + Vector3.right * 5f, Quaternion.identity);
            float leftAt = Time.time;
            while (Time.time - leftAt < 0.1f) yield return null;
            motor.Teleport(portal.transform.position, Quaternion.identity);
            while (!HasText("Stage cleared.")) yield return null;
            Debug.Log("DYNAMIC SOLO PASS: pad, batch size, continued combat and portal return.");
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
