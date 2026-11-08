// All tunable data for the room run: the room graph, encounters, enemies and the art each one uses.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoomRun
{
    public enum DoorSide { North, East, South, West }

    public static class DoorSideExt
    {
        public static Vector3 Dir(this DoorSide s) => s switch
        {
            DoorSide.North => Vector3.forward,
            DoorSide.East => Vector3.right,
            DoorSide.South => Vector3.back,
            _ => Vector3.left,
        };
        public static DoorSide Opposite(this DoorSide s) => (DoorSide)(((int)s + 2) % 4);
    }

    public enum EnemyBehaviour { Chaser, Shooter, Spinner, Dasher }

    [Serializable]
    public class EnemyDef
    {
        public string id;
        public GameObject prefab;
        public float scale = 1f;
        public int hp = 5;
        public float moveSpeed = 3f;
        public EnemyBehaviour behaviour;
        [Tooltip("Seconds between volleys. Unused by Chaser.")]
        public float fireInterval = 2f;
        public float bulletSpeed = 8f;
        public int bulletCount = 3;
        [Tooltip("Total fan angle for aimed shots.")]
        public float spreadDeg = 30f;
        [Tooltip("Spinner only: also fires an aimed fan with each ring.")]
        public bool alsoAimed;
    }

    [Serializable]
    public class SpawnEntry
    {
        public string enemyId;
        public int count = 1;
    }

    [Serializable]
    public class Wave
    {
        public List<SpawnEntry> spawns = new List<SpawnEntry>();
    }

    [Serializable]
    public class DoorLink
    {
        public DoorSide side;
        public string targetRoomId;
    }

    [Serializable]
    public class RoomDef
    {
        public string id;
        public string displayName;
        [Tooltip("Cell on the HUD map.")]
        public Vector2Int mapCell;
        [Tooltip("Stage scene to load, such as Assets/Scenes/LookStudy.unity. Empty means a placeholder room.")]
        public string scenePath;
        [Tooltip("Generated ground mesh in the stage that needs a collider, if it has none.")]
        public string groundMeshName;
        [Tooltip("World position of the room centre inside the stage. Y is the fallback ground height.")]
        public Vector3 center;
        public Vector2 size = new Vector2(26, 18);
        [Tooltip("Stage rooms only: grow the room to the whole environment the scene shows. Size above is then the minimum.")]
        public bool fitToStage = true;
        public Color floorColor = new Color(0.15f, 0.17f, 0.16f);
        public int pillarCount = 4;
        public List<DoorLink> doors = new List<DoorLink>();
        public List<Wave> waves = new List<Wave>();

        [Tooltip("Spawned at the room centre (plus offset) on entry. Gets the player as its target and takes player bullets.")]
        public GameObject bossPrefab;
        public Vector3 bossOffset;
        public int bossHp = 60;

        public bool IsCombat => waves != null && waves.Count > 0;
    }

    [CreateAssetMenu(menuName = "RoomRun/Config")]
    public class RoomRunConfig : ScriptableObject
    {
        public string startRoomId = "start";
        public string placeholderScenePath = "Assets/Game/RoomRun/Scenes/RoomRun_Placeholder.unity";
        [Tooltip("Off for art checks: enemies still spawn, but every door stays open so you can walk straight through.")]
        public bool lockDoors;
        [Tooltip("Off: explore only. No enemies, no shooting, no ammo on screen.")]
        public bool combat;
        [Tooltip("Adds the environment dev tools panel (light, colour grade, pixel) in every room, if the project has it.")]
        public bool devTools = true;
        public List<RoomDef> rooms = new List<RoomDef>();
        public List<EnemyDef> enemies = new List<EnemyDef>();

        [Header("Player")]
        public GameObject playerPrefab;
        public float playerScale = 1.15f;
        public int playerHp = 6;

        [Header("Materials")]
        public Material playerBullet;
        public Material enemyBullet;
        public Material enemyBulletCore;
        public Material ringPlayer;
        public Material ringEnemy;
        public Material hitFlash;
        public Material floor;
        public Material stone;
        public Material tile;
        public Material gate;
        public Material barrier;
        public Material lampFire;

        public RoomDef Room(string id) => rooms.Find(r => r.id == id);
        public EnemyDef Enemy(string id) => enemies.Find(e => e.id == id);
    }
}
