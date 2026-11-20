using System;
using System.IO;
using System.Reflection;
using BlueReaper.Boss;
using BlueReaper.BulletPatterns;
using BlueReaper.BulletPatterns.Editor;
using BlueReaper.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BlueReaper.Data.Editor
{
    public static class MonsterPatternAuthoringChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Tools/BlueReaper/Check Monster Pattern Authoring")]
        public static void Run()
        {
            foreach (var rank in new[] { EnemyRank.Normal, EnemyRank.Elite, EnemyRank.Boss })
                CheckRank(rank);
        }

        private static void CheckRank(EnemyRank rank)
        {
            string name = "MonsterPatternCheck_" + Guid.NewGuid().ToString("N");
            string directory = rank == EnemyRank.Boss ? "Assets/_Project/Data/Bosses" : "Assets/_Project/Data/Monsters";
            string prefab = MonsterEditorWindow.DuplicateMonster(MonsterEditorWindow.TemplatePath, name, rank);
            var window = ScriptableObject.CreateInstance<MonsterEditorWindow>();
            string PathFor(string part) => $"{directory}/{name}_{part}.asset";
            try
            {
                var ai = Load(window, prefab);
                var sequence = (EnemyPatternSequenceData)window.StageAction(ai, MonsterEditorWindow.ActionSlot.Spawn, true, name + "_Sequence");
                var first = (EnemyPatternData)window.StageChild(sequence, typeof(EnemyPatternData), name + "_First");
                var second = (EnemyPatternData)window.StageChild(sequence, typeof(EnemyPatternData), name + "_Second");
                var melee = (MeleeAttackPatternData)window.StageChild(first, typeof(MeleeAttackPatternData), name + "_Melee");
                var bullet = (BulletPatternData)window.StageChild(first, typeof(BulletPatternData), name + "_Bullet");
                var area = (AreaAttackPatternData)window.StageChild(second, typeof(AreaAttackPatternData), name + "_Area");
                var summon = (SummonPatternData)window.StageChild(second, typeof(SummonPatternData), name + "_Summon");
                Must(Call(window, "PreparePatternEditor", bullet) is BulletPatternEditor { Embedded: true }, "Bullet editor was not reused");
                Must(Call(window, "PreparePatternEditor", area) is AreaAttackPatternEditor { Embedded: true } editor &&
                    MonsterEditorWindow.ActivePatternPreview == editor, "Area editor did not become the active preview");
                first.RestTime = 0.35f;
                area.Radius = 5f;
                Must(!File.Exists(PathFor("Sequence")) && !File.Exists(PathFor("Area")), "Draft was written before save");
                window.SaveChanges();
                Must(!MonsterEditorWindow.ActivePatternPreview && !melee && !summon, "Save did not release drafts and preview");

                AssetDatabase.ImportAsset(prefab, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                ai = Load(window, prefab);
                var saved = AssetDatabase.LoadAssetAtPath<EnemyPatternSequenceData>(PathFor("Sequence"));
                using (var serialized = new SerializedObject(ai))
                    Must(saved && serialized.FindProperty("spawnPattern").objectReferenceValue == saved, "Prefab lost the sequence link");
                Must(saved.Steps.Length == 2 && AssetDatabase.GetAssetPath(saved.Steps[0]) == PathFor("First") &&
                    AssetDatabase.GetAssetPath(saved.Steps[1]) == PathFor("Second"), "Steps were reordered");
                Must(saved.Steps[0].RestTime == 0.35f, "Edited step value was not saved");
                var firstEffects = saved.Steps[0].Implementations;
                var secondEffects = saved.Steps[1].Implementations;
                Must(firstEffects.Length == 2 && firstEffects[0] is MeleeAttackPatternData && firstEffects[1] is BulletPatternData &&
                    AssetDatabase.GetAssetPath(firstEffects[0]) == PathFor("Melee") &&
                    AssetDatabase.GetAssetPath(firstEffects[1]) == PathFor("Bullet"), "Melee or bullet link was lost");
                Must(secondEffects.Length == 2 && secondEffects[0] is AreaAttackPatternData { Radius: 5f } &&
                    secondEffects[1] is SummonPatternData && AssetDatabase.GetAssetPath(secondEffects[0]) == PathFor("Area") &&
                    AssetDatabase.GetAssetPath(secondEffects[1]) == PathFor("Summon"), "Area or summon link was lost");

                var areaDraft = (AreaAttackPatternData)Call(window, "BeginAssetEdit", secondEffects[0]);
                areaDraft.HitStartSeconds = 0.4f;
                var patternEditor = (EnemyPatternEditor)Call(window, "PreparePatternEditor", saved.Steps[1]);
                Must(EnemyPatternEditor.OrderedEffects(saved.Steps[1], patternEditor.EmbeddedEffectView)[0] == areaDraft,
                    "Pattern timing preview ignored the staged area edit");
                var cancelled = (EnemyPatternData)window.StageAction(ai, MonsterEditorWindow.ActionSlot.Spawn, false, name + "_Cancelled");
                window.StageChild(cancelled, typeof(AreaAttackPatternData), name + "_CancelledArea");
                window.DiscardChanges();
                using (var serialized = new SerializedObject(Root(window).GetComponent<EnemyAI>()))
                    Must(serialized.FindProperty("spawnPattern").objectReferenceValue == saved &&
                        ((AreaAttackPatternData)secondEffects[0]).HitStartSeconds == 0f &&
                        !File.Exists(PathFor("Cancelled")) && !File.Exists(PathFor("CancelledArea")), "Discard changed saved data");
                Debug.Log($"MONSTER PATTERN AUTHORING PASS {rank}");
            }
            finally
            {
                Object.DestroyImmediate(window);
                AssetDatabase.DeleteAsset(prefab);
                foreach (var part in new[] { "Stats", "Sequence", "First", "Second", "Melee", "Bullet", "Area", "Summon", "Cancelled", "CancelledArea" })
                    if (File.Exists(PathFor(part))) AssetDatabase.DeleteAsset(PathFor(part));
            }
        }

        private static EnemyAI Load(MonsterEditorWindow window, string path)
        {
            Call(window, "Load", path);
            return Root(window).GetComponent<EnemyAI>();
        }

        private static GameObject Root(MonsterEditorWindow window) =>
            (GameObject)typeof(MonsterEditorWindow).GetField("root", Private).GetValue(window);

        private static object Call(MonsterEditorWindow window, string method, params object[] args) =>
            typeof(MonsterEditorWindow).GetMethod(method, Private).Invoke(window, args);

        private static void Must(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
