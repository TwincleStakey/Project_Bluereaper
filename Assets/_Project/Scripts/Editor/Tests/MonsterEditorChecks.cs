using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BlueReaper.Boss;
using BlueReaper.BulletPatterns.Editor;
using BlueReaper.Core;
using BlueReaper.HitBox;
using BlueReaper.Network;
using BlueReaper.Network.Editor;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BlueReaper.Data.Editor
{
    public static class MonsterEditorChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Tools/BlueReaper/Check Monster Editor")]
        public static void Run()
        {
            string directory = "Assets/MonsterEditorCheck_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(directory));
            var window = ScriptableObject.CreateInstance<MonsterEditorWindow>();
            string name = Path.GetFileName(directory);
            var created = new List<string>();
            try
            {
                CheckNetworkAuthoring(directory, name, created);
                CheckRankPaths(window, name, created);
                var template = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterEditorWindow.TemplatePath);
                string path = MonsterEditorWindow.DuplicateMonster(MonsterEditorWindow.TemplatePath, name, EnemyRank.Normal);
                string statsPath = "Assets/_Project/Data/Monsters/" + name + "_Stats.asset";
                created.Add(path);
                created.Add(statsPath);
                Must(path == "Assets/_Project/Prefabs/Monsters/" + name + ".prefab", "Wrong normal prefab path");
                string guid = AssetDatabase.AssetPathToGUID(path);
                Must(guid != AssetDatabase.AssetPathToGUID(MonsterEditorWindow.TemplatePath), "Prefab must have an independent GUID");
                var stats = AssetDatabase.LoadAssetAtPath<UnitData>(statsPath);
                float originalAttack = template.GetComponent<Unit>().Data.Attack;
                var duplicate = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Must(stats && stats != template.GetComponent<Unit>().Data && stats.Attack == originalAttack,
                    "New monster needs independent stats");
                Must(duplicate.GetComponent<EnemyAggro>() && duplicate.GetComponent<Shield>() &&
                    !duplicate.GetComponent<Posture>(), "Expected Aggro and Shield by default with Posture optional");
                Debug.Log("MONSTER EDITOR PASS 1/5: template and independent stats");

                var phases = ScriptableObject.CreateInstance<EnemyPhaseData>();
                phases.PhaseTrigger_HPRatio_Permil = new uint[] { 1000, 650 };
                AssetDatabase.CreateAsset(phases, directory + "/Phase.asset");
                var pattern = ScriptableObject.CreateInstance<EnemyPatternData>();
                pattern.RequirePhase = new uint[] { 1, 2 };
                var melee = ScriptableObject.CreateInstance<MeleeAttackPatternData>();
                AssetDatabase.CreateAsset(melee, directory + "/Melee.asset");
                pattern.Implementations = new PatternImplData[] { melee };
                AssetDatabase.CreateAsset(pattern, directory + "/Pattern.asset");
                var sequence = ScriptableObject.CreateInstance<EnemyPatternSequenceData>();
                sequence.Steps = new[] { pattern };
                sequence.RequirePhase = new uint[] { 2 };
                AssetDatabase.CreateAsset(sequence, directory + "/Sequence.asset");

                Call(window, "Load", path);
                var root = Root(window);
                var unit = root.GetComponent<Unit>();
                Undo.AddComponent<Posture>(root);
                Edit(root.GetComponent<Shield>(), data =>
                {
                    data.FindProperty("maxShield").floatValue = 100f;
                    data.FindProperty("initialShield").floatValue = 40f;
                });
                var ai = root.GetComponent<EnemyAI>();
                var enemy = root.GetComponent<Enemy>();
                var draft = (UnitData)Call(window, "BeginAssetEdit", stats);
                draft.Defense = 12f;
                draft.Attack = originalAttack + 19;
                Must(stats.Attack == originalAttack, "Draft already affected the saved asset");
                Edit(enemy, data => data.FindProperty("phaseInfo").objectReferenceValue = phases);
                Edit(ai, data =>
                {
                    var patterns = data.FindProperty("patterns");
                    patterns.arraySize = 2;
                    patterns.GetArrayElementAtIndex(0).objectReferenceValue = pattern;
                    patterns.GetArrayElementAtIndex(1).objectReferenceValue = sequence;
                    data.FindProperty("spawnPattern").objectReferenceValue = sequence;
                    data.FindProperty("deathPattern").objectReferenceValue = pattern;
                    var rule = data.FindProperty("chaseTargetRule");
                    rule.FindPropertyRelative("SelectionMode").enumValueIndex = (int)TargetSelectionMode.Top;
                    var sort = rule.FindPropertyRelative("SortBy");
                    sort.arraySize = 1;
                    sort.GetArrayElementAtIndex(0).FindPropertyRelative("Metric").enumValueIndex = (int)TargetMetric.Distance;
                });
                var patternDraft = (EnemyPatternData)Call(window, "BeginAssetEdit", pattern);
                patternDraft.TargetMode = ActionTargetMode.Select;
                patternDraft.TargetRule.Count = 3;
                var mainScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                bool mainSceneDirty = mainScene.isDirty;
                var attack = MonsterEditorWindow.AddCollider(root, true, 1);
                var hurt = MonsterEditorWindow.AddCollider(root, false, 2);
                Must(mainScene.isDirty == mainSceneDirty, "Hitbox editing dirtied the main scene");
                string attackPath = AnimationUtility.CalculateTransformPath(attack.transform, root.transform);
                string hurtPath = AnimationUtility.CalculateTransformPath(hurt.transform, root.transform);
                window.SaveChanges();
                Must(unit.Data == stats && stats.Attack == originalAttack + 19, "Staged stats were not applied");
                Debug.Log("MONSTER EDITOR PASS 2/5: phase, target rule, action slots and separate hitboxes saved");

                Call(window, "Load", path);
                root = Root(window);
                var prototype = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(prototype.GetComponent<Collider>());
                prototype.name = "Replacement Visual";
                string visualPath = directory + "/Visual.prefab";
                var visual = PrefabUtility.SaveAsPrefabAsset(prototype, visualPath);
                Object.DestroyImmediate(prototype);
                foreach (var oldRenderer in root.GetComponentsInChildren<Renderer>(true)) oldRenderer.enabled = false;
                var added = MonsterEditorWindow.AddVisualModel(root, visual);
                string visualChildPath = AnimationUtility.CalculateTransformPath(added.transform, root.transform);
                var animator = root.GetComponent<Animator>();
                var controller = AnimatorController.CreateAnimatorControllerAtPath(directory + "/Replacement.controller");
                var clip = new AnimationClip { name = "ReplacementAttack" };
                clip.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0, 0, 0.5f, 0.1f));
                AssetDatabase.CreateAsset(clip, directory + "/ReplacementAttack.anim");
                var machine = controller.layers[0].stateMachine;
                machine.AddState("Idle");
                machine.AddState("Move");
                machine.AddState("Died");
                machine.AddState(clip.name).motion = clip;
                controller.AddParameter("MoveSpeed", AnimatorControllerParameterType.Float);
                animator.runtimeAnimatorController = controller;
                ((EnemyPatternData)Call(window, "BeginAssetEdit", pattern)).MotionClip = clip;
                AssetDatabase.SaveAssets();
                window.SaveChanges();
                Must(added && added.GetComponent<MeshFilter>(), "Replacement model did not load");
                Debug.Log("MONSTER EDITOR PASS 3/5: replacement visual, controller and attack clip saved");

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                Call(window, "Load", path);
                root = Root(window);
                ai = root.GetComponent<EnemyAI>();
                enemy = root.GetComponent<Enemy>();
                Must(AssetDatabase.AssetPathToGUID(path) == guid, "Re-save changed prefab GUID");
                Must(root.GetComponent<Unit>().Data == stats && stats.Attack == originalAttack + 19
                    && template.GetComponent<Unit>().Data.Attack == originalAttack, "Stat references changed with visual");
                Must(Reference<EnemyPhaseData>(enemy, "phaseInfo") == phases && phases.PhaseTrigger_HPRatio_Permil[1] == 650,
                    "Phase was lost after replacement");
                Must(Reference<EnemyActionData>(ai, "spawnPattern") == sequence
                    && Reference<EnemyPatternData>(ai, "deathPattern") == pattern, "Lifecycle actions changed");
                using (var aiData = new SerializedObject(ai))
                {
                    var patterns = aiData.FindProperty("patterns");
                    Must(patterns.arraySize == 2 && patterns.GetArrayElementAtIndex(0).objectReferenceValue == pattern
                        && patterns.GetArrayElementAtIndex(1).objectReferenceValue == sequence, "Normal actions changed");
                    Must(aiData.FindProperty("chaseTargetRule").FindPropertyRelative("SortBy").GetArrayElementAtIndex(0)
                        .FindPropertyRelative("Metric").enumValueIndex == (int)TargetMetric.Distance, "Chase rule changed");
                }
                Must(pattern.TargetMode == ActionTargetMode.Select && pattern.TargetRule.Count == 3 && pattern.MotionClip == clip
                    && pattern.Implementations.Single() == melee && sequence.Steps.Single() == pattern, "Action data changed");
                Must(root.transform.Find(attackPath).GetComponent<SphereCollider>().isTrigger
                    && root.transform.Find(attackPath).GetComponent<MeleeHitBox>()
                    && root.transform.Find(hurtPath).GetComponent<CapsuleCollider>().gameObject.layer == LayerMask.NameToLayer("Hurtbox"),
                    "Attack or Hurtbox linkage changed");
                Must(stats.Defense == 12f && root.GetComponent<EnemyAggro>() && root.GetComponent<Posture>()
                    && root.GetComponent<Shield>() is { MaxShield: 100f, InitialShield: 40f },
                    "Defense and stat components did not survive reimport");
                var modelInstance = root.transform.Find(visualChildPath);
                Must(root.GetComponent<Animator>().runtimeAnimatorController == controller
                    && modelInstance && modelInstance.GetComponent<MeshRenderer>().enabled
                    && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(modelInstance.gameObject) == visualPath,
                    "Replacement visual or Animator was not serialized");
                Debug.Log("MONSTER EDITOR PASS 4/5: GUID, gameplay settings and replacement survived reimport");

                var savedStatsDraft = (UnitData)Call(window, "BeginAssetEdit", stats);
                savedStatsDraft.Attack = originalAttack + 25;
                stats.Attack = originalAttack + 28;
                bool blocked = false;
                try { window.SaveChanges(); }
                catch (IOException) { blocked = true; }
                Must(blocked && stats.Attack == originalAttack + 28, "External data edits were overwritten");
                Debug.Log("MONSTER EDITOR PASS 5/5: concurrent asset edit is preserved");
            }
            finally
            {
                Object.DestroyImmediate(window);
                foreach (string path in created) AssetDatabase.DeleteAsset(path);
                AssetDatabase.DeleteAsset(directory);
            }
        }

        private static void CheckNetworkAuthoring(string directory, string name, List<string> created)
        {
            string templatePath = directory + "/LocalTemplate.prefab";
            var root = PrefabUtility.LoadPrefabContents(MonsterEditorWindow.TemplatePath);
            try
            {
                foreach (var component in new Component[]
                {
                    root.GetComponent<NetworkAnimator>(), root.GetComponent<EnemyNetworkState>(),
                    root.GetComponent<NetworkTransform>(), root.GetComponent<NetworkObject>()
                })
                    if (component) Object.DestroyImmediate(component);
                PrefabUtility.SaveAsPrefabAsset(root, templatePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            string monsterName = name + "_Network";
            string monsterPath = MonsterEditorWindow.DuplicateMonster(templatePath, monsterName, EnemyRank.Normal);
            created.Add(monsterPath);
            created.Add($"Assets/_Project/Data/Monsters/{monsterName}_Stats.asset");
            const ImportAssetOptions reimport = ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate;
            AssetDatabase.ImportAsset(monsterPath, reimport);
            var monster = AssetDatabase.LoadAssetAtPath<GameObject>(monsterPath);
            var list = NetworkPrefabAuthoring.LoadList();
            Must(!AssetDatabase.LoadAssetAtPath<GameObject>(templatePath).GetComponent<NetworkObject>(),
                "Monster duplication edited the source template");
            Must(monster.GetComponents<NetworkObject>().Length == 1
                && monster.GetComponent<NetworkObject>().PrefabIdHash != 0
                && monster.GetComponents<NetworkTransform>().Length == 1
                && monster.GetComponents<EnemyNetworkState>().Length == 1
                && monster.GetComponents<NetworkAnimator>().Single().Animator == monster.GetComponent<Animator>()
                && list.PrefabList.Count(entry => entry.Prefab == monster) == 1,
                "Monster network components or registration did not survive reimport");
            Debug.Log("MONSTER NETWORK PASS 1/2: local template duplicated with network components and unique registration");

            string bulletPath = directory + "/Bullet.prefab";
            var bulletRoot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                bulletRoot.AddComponent<ProjectileHitBox>();
                PrefabUtility.SaveAsPrefabAsset(bulletRoot, bulletPath);
            }
            finally { Object.DestroyImmediate(bulletRoot); }

            var bullet = AssetDatabase.LoadAssetAtPath<GameObject>(bulletPath);
            string bulletGuid = AssetDatabase.AssetPathToGUID(bulletPath);
            Must(!list.Contains(bullet), "Local projectile was registered before setting it up");
            BulletPatternEditor.SetupNetworkPrefab(bullet);
            bullet = AssetDatabase.LoadAssetAtPath<GameObject>(bulletPath);
            list.Remove(list.PrefabList.Single(entry => entry.Prefab == bullet));
            BulletPatternEditor.SetupNetworkPrefab(bullet);
            BulletPatternEditor.SetupNetworkPrefab(bullet);
            AssetDatabase.ImportAsset(bulletPath, reimport);
            AssetDatabase.ImportAsset(NetworkPrefabAuthoring.ListPath, reimport);
            bullet = AssetDatabase.LoadAssetAtPath<GameObject>(bulletPath);
            list = NetworkPrefabAuthoring.LoadList();
            Must(AssetDatabase.AssetPathToGUID(bulletPath) == bulletGuid
                && bullet.GetComponents<NetworkObject>().Length == 1
                && bullet.GetComponent<NetworkObject>().PrefabIdHash != 0
                && bullet.GetComponents<NetworkTransform>().Length == 1 && bullet.GetComponent<ProjectileHitBox>()
                && bullet.GetComponent<SphereCollider>() && list.PrefabList.Count(entry => entry.Prefab == bullet) == 1,
                "Projectile setup changed the asset or duplicated components or registration");
            Debug.Log("MONSTER NETWORK PASS 2/2: projectile setup and repeated registration preserve one entry");
        }

        private static void CheckRankPaths(MonsterEditorWindow window, string name, List<string> created)
        {
            string bossName = name + "_Boss";
            string path = MonsterEditorWindow.DuplicateMonster(MonsterEditorWindow.TemplatePath, bossName, EnemyRank.Boss);
            const string bosses = "Assets/_Project/Data/Bosses/";
            const string monsters = "Assets/_Project/Data/Monsters/";
            string statsPath = bosses + bossName + "_Stats.asset";
            created.Add(path);
            created.Add(statsPath);
            Must(path == "Assets/_Project/Prefabs/Boss/" + bossName + ".prefab", "Wrong boss prefab path");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Must(prefab.GetComponent<Enemy>().Rank == EnemyRank.Boss &&
                AssetDatabase.GetAssetPath(prefab.GetComponent<Unit>().Data) == statsPath, "Boss rank or stats reference was lost");
            string originalGuid = AssetDatabase.AssetPathToGUID(path);
            MustReject(() => MonsterEditorWindow.DuplicateMonster(MonsterEditorWindow.TemplatePath, bossName, EnemyRank.Boss));
            Must(AssetDatabase.AssetPathToGUID(path) == originalGuid, "Duplicate creation overwrote a prefab");
            Debug.Log("MONSTER PATHS PASS 1/3: boss routing and duplicate protection");

            Call(window, "Load", path);
            var root = Root(window);
            var unit = root.GetComponent<Unit>();
            string copyPath = bosses + bossName + "_Copy.asset";
            using (var owner = new SerializedObject(unit))
            {
                var slot = owner.FindProperty("data");
                MustReject(() => MonsterEditorWindow.CopyDataAndConnect(owner, slot,
                    monsters + bossName + "_Invalid.asset", EnemyRank.Boss));
                Must(AssetDatabase.GetAssetPath(unit.Data) == statsPath, "Rejected copy changed the reference");
                MonsterEditorWindow.CopyDataAndConnect(owner, slot, copyPath, EnemyRank.Boss);
                created.Add(copyPath);
            }
            window.SaveChanges();
            Call(window, "Load", path);
            root = Root(window);
            Must(AssetDatabase.GetAssetPath(root.GetComponent<Unit>().Data) == copyPath, "Copied data link was not persisted");
            Debug.Log("MONSTER PATHS PASS 2/3: copy and connect enforces rank path and persists reference");

            Edit(root.GetComponent<Enemy>(), editor => editor.FindProperty("rank").intValue = (int)EnemyRank.Elite);
            var warnings = ((IEnumerable<string>)Call(window, "GetIssues")).ToArray();
            Must(warnings.Any(warning => warning.Contains(path)) && warnings.Any(warning => warning.Contains(copyPath)),
                "Changing rank must warn about both the prefab and data paths");
            window.SaveChanges();
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Must(prefab.GetComponent<Enemy>().Rank == EnemyRank.Elite && AssetDatabase.AssetPathToGUID(path) == originalGuid
                && AssetDatabase.GetAssetPath(prefab.GetComponent<Unit>().Data) == copyPath, "Saving a rank change moved existing assets");
            string eliteName = name + "_Elite";
            string elitePath = MonsterEditorWindow.DuplicateMonster(MonsterEditorWindow.TemplatePath, eliteName, EnemyRank.Elite);
            string eliteStats = monsters + eliteName + "_Stats.asset";
            created.Add(elitePath);
            created.Add(eliteStats);
            var elite = AssetDatabase.LoadAssetAtPath<GameObject>(elitePath);
            Must(elitePath == "Assets/_Project/Prefabs/Monsters/" + eliteName + ".prefab"
                && elite.GetComponent<Enemy>().Rank == EnemyRank.Elite
                && AssetDatabase.GetAssetPath(elite.GetComponent<Unit>().Data) == eliteStats, "Wrong elite routing");
            Debug.Log("MONSTER PATHS PASS 3/3: elite routing and rank mismatch warnings without moving assets");
        }

        private static void MustReject(Action action)
        {
            try { action(); }
            catch (IOException) { return; }
            throw new InvalidOperationException("An invalid destination was accepted");
        }

        private static GameObject Root(MonsterEditorWindow window) =>
            (GameObject)typeof(MonsterEditorWindow).GetField("root", Private).GetValue(window);

        private static object Call(MonsterEditorWindow window, string name, params object[] args) =>
            typeof(MonsterEditorWindow).GetMethod(name, Private).Invoke(window, args);

        private static void Edit(Object owner, Action<SerializedObject> edit)
        {
            using var data = new SerializedObject(owner);
            edit(data);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T Reference<T>(Object owner, string property) where T : Object
        {
            using var data = new SerializedObject(owner);
            return (T)data.FindProperty(property).objectReferenceValue;
        }

        private static void Must(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException(message);
        }
    }
}
