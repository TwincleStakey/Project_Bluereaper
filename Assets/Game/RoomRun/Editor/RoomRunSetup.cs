// Menu: RoomRun > Build Scenes creates the config, materials, bootstrap scene and placeholder scene.
// RoomRun > Add Scenes To Build Settings is for making a standalone build later.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RoomRun
{
    public static class RoomRunSetup
    {
        const string Root = "Assets/Game/RoomRun";
        const string ConfigPath = Root + "/RoomRunConfig.asset";
        const string BootPath = Root + "/Scenes/RoomRun.unity";
        const string PlaceholderPath = Root + "/Scenes/RoomRun_Placeholder.unity";
        const string DD = "Assets/Art/DDConcept";

        [MenuItem("Tools/RoomRun/Build Scenes")]
        public static void BuildKeepConfig() => Build(false);

        // Batch entry point. Rewrites the config from the defaults below.
        public static void BuildFresh() => Build(true);

        static void Build(bool overwriteConfig)
        {
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Scenes");
            AssetDatabase.Refresh();

            var config = AssetDatabase.LoadAssetAtPath<RoomRunConfig>(ConfigPath);
            if (!config)
            {
                config = ScriptableObject.CreateInstance<RoomRunConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
                overwriteConfig = true;
            }
            if (overwriteConfig) Fill(config);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            BuildPlaceholderScene();
            BuildBootScene(config);
            Debug.Log("[RoomRun] Built. Open " + BootPath + " and press Play.");
        }

        static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{DD}/Materials/{name}.mat");
        static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{DD}/Prefabs/{name}.prefab");

        static Material OwnGlow(string name, Color color, float intensity, string shader = "DD/Glow")
        {
            var path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Intensity", intensity);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Fill(RoomRunConfig c)
        {
            c.playerPrefab = Prefab("Hunter");
            c.playerBullet = Mat("DD_PlayerBullet");
            c.enemyBullet = Mat("DD_EnemyBullet");
            c.enemyBulletCore = Mat("DD_EnemyBulletCore");
            c.ringPlayer = OwnGlow("RR_RingPlayer", new Color(0.37f, 0.85f, 1f), 1.1f, "RoomRun/RingOverlay");
            c.ringEnemy = OwnGlow("RR_RingEnemy", new Color(1f, 0.29f, 0.16f), 1.0f, "RoomRun/RingOverlay");
            c.floor = Mat("DD_Ground");
            c.stone = Mat("DD_Stone");
            c.tile = Mat("DD_Tile");
            c.gate = Mat("DD_Hongsal");
            c.barrier = Mat("DD_Ember");
            c.lampFire = Mat("DD_LampFire");
            c.hitFlash = OwnGlow("RR_HitFlash", Color.white, 1.4f);
            c.placeholderScenePath = PlaceholderPath;
            c.startRoomId = "grass";

            c.enemies = new List<EnemyDef>
            {
                new EnemyDef { id = "shade", prefab = Prefab("Eodukshini"), scale = 0.85f, hp = 3, moveSpeed = 3.4f, behaviour = EnemyBehaviour.Chaser },
                new EnemyDef { id = "ghost", prefab = Prefab("Gwisin"), scale = 1.0f, hp = 5, moveSpeed = 2.4f, behaviour = EnemyBehaviour.Shooter,
                    fireInterval = 2.1f, bulletSpeed = 7.5f, bulletCount = 3, spreadDeg = 34f },
                new EnemyDef { id = "fox", prefab = Prefab("Gumiho"), scale = 1.15f, hp = 7, moveSpeed = 3.2f, behaviour = EnemyBehaviour.Dasher,
                    bulletSpeed = 8f, bulletCount = 5, spreadDeg = 70f },
                new EnemyDef { id = "dokkaebi", prefab = Prefab("Dokkaebi"), scale = 1.15f, hp = 12, moveSpeed = 1.5f, behaviour = EnemyBehaviour.Spinner,
                    fireInterval = 1.9f, bulletSpeed = 5.5f, bulletCount = 14 },
                new EnemyDef { id = "dokkaebi_elite", prefab = Prefab("Dokkaebi"), scale = 1.9f, hp = 70, moveSpeed = 1.4f, behaviour = EnemyBehaviour.Spinner,
                    fireInterval = 1.5f, bulletSpeed = 5.5f, bulletCount = 20, alsoAimed = true },
            };

            Wave W(params (string id, int n)[] spawns)
            {
                var w = new Wave();
                foreach (var s in spawns) w.spawns.Add(new SpawnEntry { enemyId = s.id, count = s.n });
                return w;
            }
            DoorLink L(DoorSide side, string to) => new DoorLink { side = side, targetRoomId = to };

            c.rooms = new List<RoomDef>
            {
                // The run starts in the grass field (Kido, 2026-11-08: no separate entrance room).
                new RoomDef { id = "grass", displayName = "풀밭", mapCell = new Vector2Int(1, 1), size = new Vector2(34, 24),
                    scenePath = "Assets/Scenes/LookStudy.unity", groundMeshName = "RGround", center = new Vector3(-400, 0.3f, 3),
                    doors = { L(DoorSide.North, "valley") },
                    waves = { W(("shade", 4)), W(("ghost", 2), ("shade", 3)) } },

                new RoomDef { id = "valley", displayName = "산속 계곡", mapCell = new Vector2Int(1, 0), size = new Vector2(30, 20),
                    floorColor = new Color(0.11f, 0.17f, 0.16f), pillarCount = 5,
                    doors = { L(DoorSide.South, "grass"), L(DoorSide.East, "palace"), L(DoorSide.West, "village") },
                    waves = { W(("fox", 2), ("shade", 2)), W(("ghost", 3), ("fox", 1)) } },

                new RoomDef { id = "village", displayName = "민가", mapCell = new Vector2Int(0, 0), size = new Vector2(28, 20),
                    floorColor = new Color(0.17f, 0.14f, 0.12f), pillarCount = 6,
                    doors = { L(DoorSide.East, "valley") },
                    waves = { W(("shade", 5), ("ghost", 2)), W(("dokkaebi", 1), ("fox", 1)) } },

                new RoomDef { id = "palace", displayName = "대전 앞", mapCell = new Vector2Int(2, 0), size = new Vector2(34, 24),
                    floorColor = new Color(0.16f, 0.13f, 0.13f), pillarCount = 4,
                    doors = { L(DoorSide.West, "valley") },
                    waves = { W(("dokkaebi", 1), ("ghost", 2)), W(("dokkaebi_elite", 1)) } },
            };
        }

        static void BuildPlaceholderScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var moon = new GameObject("Moon").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.77f, 0.82f, 1f);
            moon.intensity = 1.5f;
            moon.shadows = LightShadows.Soft;
            moon.transform.rotation = Quaternion.Euler(52f, -38f, 0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.1f, 0.125f, 0.19f);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.sun = moon;

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>($"{DD}/DDConcept_Volume.asset");
            if (profile)
            {
                var vol = new GameObject("Global Volume").AddComponent<Volume>();
                vol.isGlobal = true;
                vol.sharedProfile = profile;
            }

            EditorSceneManager.SaveScene(scene, PlaceholderPath);
        }

        static void BuildBootScene(RoomRunConfig config)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("RunDirector");
            go.AddComponent<RunDirector>().config = config;
            EditorSceneManager.SaveScene(scene, BootPath);
        }

        [MenuItem("Tools/RoomRun/Add Scenes To Build Settings")]
        public static void AddToBuild()
        {
            var config = AssetDatabase.LoadAssetAtPath<RoomRunConfig>(ConfigPath);
            var wanted = new List<string> { BootPath, PlaceholderPath };
            foreach (var r in config.rooms)
                if (!string.IsNullOrEmpty(r.scenePath) && !wanted.Contains(r.scenePath)) wanted.Add(r.scenePath);

            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var w in wanted)
                if (!list.Exists(s => s.path == w)) list.Add(new EditorBuildSettingsScene(w, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[RoomRun] Scenes added to Build Settings.");
        }
    }
}
