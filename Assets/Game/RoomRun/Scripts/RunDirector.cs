// Owns one run: which room you are in, loading stages, locking doors, waves, death and restart.
// Everything here lives across scene loads. Each room loads its scene in Single mode, so a stage's own
// lighting and sky apply exactly as its author set them.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoomRun
{
    public sealed class RunDirector : MonoBehaviour
    {
        public static RunDirector Instance { get; private set; }

        public RoomRunConfig config;

        public PlayerController Player { get; private set; }
        public CameraRig Rig { get; private set; }
        public BulletSystem Bullets { get; private set; }
        public Hud Hud { get; private set; }
        public readonly List<EnemyController> Enemies = new List<EnemyController>();
        public readonly List<Door> Doors = new List<Door>();
        public readonly HashSet<string> Visited = new HashSet<string>();
        public readonly HashSet<string> Cleared = new HashSet<string>();

        public RoomDef Room { get; private set; }
        public bool Transitioning { get; private set; }
        public bool PlayerDead { get; private set; }
        public bool RunComplete { get; private set; }
        public bool InCombat { get; private set; }

        Transform roomRoot;
        Coroutine encounter;
        bool usingStage;

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            var bulletsGo = new GameObject("Bullets");
            bulletsGo.transform.SetParent(transform, false);
            Bullets = bulletsGo.AddComponent<BulletSystem>();
            Bullets.Init(config);

            var fxGo = new GameObject("Fx");
            fxGo.transform.SetParent(transform, false);
            fxGo.AddComponent<Fx>().Init(config);

            var rigGo = new GameObject("CameraRig");
            rigGo.transform.SetParent(transform, false);
            Rig = rigGo.AddComponent<CameraRig>();

            var hudGo = new GameObject("Hud");
            hudGo.transform.SetParent(transform, false);
            Hud = hudGo.AddComponent<Hud>();
            Hud.Init(this);

            var playerGo = new GameObject("Player", typeof(CharacterController), typeof(Health), typeof(PlayerController));
            playerGo.transform.SetParent(transform, false);
            Player = playerGo.GetComponent<PlayerController>();
            Player.Init(config);
            Player.Source = new InputReader();
            Rig.Target = Player;
        }

        void Start() => BeginRun();

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void BeginRun()
        {
            Visited.Clear();
            Cleared.Clear();
            PlayerDead = false;
            RunComplete = false;
            Player.ResetForRun();
            StartCoroutine(EnterRoom(config.Room(config.startRoomId), null));
        }

        public void Restart()
        {
            if (Transitioning) return;
            BeginRun();
        }

        public void GoThrough(Door door)
        {
            if (Transitioning) return;
            StartCoroutine(EnterRoom(config.Room(door.Link.targetRoomId), door.Side.Opposite()));
        }

        // Jumps straight to a room, for tests and debugging.
        public void JumpTo(string roomId)
        {
            if (Transitioning) return;
            StartCoroutine(EnterRoom(config.Room(roomId), null));
        }

        public void OnPlayerDied()
        {
            PlayerDead = true;
            if (encounter != null) { StopCoroutine(encounter); encounter = null; }
            InCombat = false;
            Rig.Shake(1f);
            Fx.Burst(Player.transform.position + Vector3.up, Fx.Kind.Death, 40);
            Hud.ShowBanner("쓰러졌다", "Enter 키로 다시 시작");
        }

        IEnumerator EnterRoom(RoomDef next, DoorSide? entrySide)
        {
            if (next == null)
            {
                Debug.LogError("[RoomRun] Door points at a room that is not in the config.");
                yield break;
            }

            Transitioning = true;
            Player.Frozen = true;
            if (encounter != null) { StopCoroutine(encounter); encounter = null; }
            InCombat = false;
            Hud.HideBanner();
            Bullets.ClearAll();
            yield return Hud.Fade(1f, 0.22f);

            Fx.ClearAll();
            foreach (var e in Enemies) if (e) Destroy(e.gameObject);
            Enemies.Clear();
            Doors.Clear();
            if (roomRoot) Destroy(roomRoot.gameObject);

            ReleaseStageMeshes();
            yield return LoadStage(next);

            Room = next;
            Visited.Add(next.id);
            if (usingStage) yield return WaitForStageBuild();
            PrepareStage(next);
            AreaCenter = next.center;
            AreaSize = next.size;
            if (usingStage && next.fitToStage) FitToStage(next);
            roomRoot = RoomBuilder.Build(next, config, Doors, !usingStage, AreaCenter, AreaSize);

            Player.PlaceAt(SpawnPoint(entrySide), next.center.y);
            SpawnBoss(next);
            Rig.SetBounds(AreaCenter, AreaSize);
            Rig.Snap();
            Hud.RefreshMap();
            Hud.ShowRoomName(next.displayName);

            yield return Hud.Fade(0f, 0.22f);
            Player.Frozen = false;
            Transitioning = false;

            if (config.combat && next.IsCombat && !Cleared.Contains(next.id))
                encounter = StartCoroutine(RunEncounter(next));
            else
                CheckRunComplete();
        }

        // Room boss (e.g. the dokkaebi in the underworld): walks at the player and slams.
        void SpawnBoss(RoomDef room)
        {
            if (!room.bossPrefab) return;
            var pos = AreaCenter + room.bossOffset;
            pos.y = room.center.y;
            if (Physics.Raycast(pos + Vector3.up * 30f, Vector3.down, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore)) pos.y = hit.point.y;
            var face = Player.transform.position - pos; face.y = 0;
            var rot = face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : Quaternion.identity;
            var go = Instantiate(room.bossPrefab, pos, rot, roomRoot);
            var boss = go.GetComponent<Dokkaebi.DokkaebiBoss>();
            if (!boss) return;
            boss.target = Player.transform;
            if (boss.slamFx)
            {
                boss.slamFx.hitTarget = Player.transform;
                boss.slamFx.Impacted += _ => Rig.Shake(0.75f);
                boss.slamFx.ProjectileHitTarget += _ => { if (config.combat && Player.Health) Player.Health.Hit(1); };
            }
            if (!config.combat) return;
            // shootable: a body capsule plus enemy-team health
            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.45f, 0f); col.radius = 0.2f; col.height = 0.9f;   // model space, prefab is scaled ~3.2
            var hp = go.AddComponent<Health>();
            hp.Init(Team.Enemy, Mathf.Max(1, room.bossHp));
            hp.Died += () =>
            {
                Fx.Burst(go.transform.position + Vector3.up * 1.5f, Fx.Kind.Death, 60);
                Rig.Shake(1f);
                Destroy(go);
            };
        }

        IEnumerator LoadStage(RoomDef room)
        {
            usingStage = !string.IsNullOrEmpty(room.scenePath);
            var op = usingStage ? Load(room.scenePath) : null;
            if (usingStage && op == null)
            {
                Debug.LogWarning($"[RoomRun] Stage '{room.scenePath}' was not found. Using a placeholder floor.");
                usingStage = false;
            }
            if (!usingStage) op = Load(config.placeholderScenePath);
            if (op != null) yield return op;
            yield return null;
        }

        static AsyncOperation Load(string path)
        {
#if UNITY_EDITOR
            // The editor loads any scene by path, so stages work before they are in Build Settings.
            return System.IO.File.Exists(path)
                ? UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single))
                : null;
#else
            return Application.CanStreamedLevelBeLoaded(path) ? SceneManager.LoadSceneAsync(path, LoadSceneMode.Single) : null;
#endif
        }

        // Turns a scene built for looking at into one built for playing in.
        void PrepareStage(RoomDef room)
        {
            Rig.ResetClear();
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam == Rig.Cam) continue;
                Rig.CopyClearFrom(cam);
                cam.enabled = false;
            }
            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (l.gameObject != Rig.gameObject) l.enabled = false;

            // The environment panel lives in the art scripts, which this assembly cannot reference, so it is found by name.
            // A stage loaded in Single mode takes the previous panel with it, so each room gets a fresh one.
            if (config.devTools)
            {
                var devTools = FindType("LookStudyDevTools");
                if (devTools != null && !FindAnyObjectByType(devTools))
                    new GameObject("LookStudyDevTools").AddComponent(devTools);
            }

            if (!string.IsNullOrEmpty(room.groundMeshName))
            {
                // Generated stage objects are often DontSave, which FindObjectsByType skips, so walk the scene instead.
                foreach (var mf in Collect<MeshFilter>(SceneManager.GetActiveScene().GetRootGameObjects()))
                {
                    if (mf.name != room.groundMeshName || mf.GetComponent<Collider>()) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                }
                Physics.SyncTransforms();
            }
        }

        // Stage scripts build their meshes at runtime and flag them DontSave, which scene unloads and
        // UnloadUnusedAssets never free. Without this, every room change kept ~100 meshes and the GPU
        // eventually ran out of memory. Anything DontSave made during the last visit is released here.
        readonly HashSet<int> meshBaseline = new HashSet<int>();

        void ReleaseStageMeshes()
        {
            var all = Resources.FindObjectsOfTypeAll<Mesh>();
            if (meshBaseline.Count > 0)
            {
                var keep = new HashSet<Mesh>();
                foreach (var mf in GetComponentsInChildren<MeshFilter>(true)) keep.Add(mf.sharedMesh);
                foreach (var pr in GetComponentsInChildren<ParticleSystemRenderer>(true)) keep.Add(pr.mesh);
                foreach (var m in all)
                {
                    if (!m || meshBaseline.Contains(m.GetInstanceID()) || keep.Contains(m)) continue;
                    if ((m.hideFlags & HideFlags.DontSave) == 0) continue;
#if UNITY_EDITOR
                    if (UnityEditor.EditorUtility.IsPersistent(m)) continue;
#endif
                    Destroy(m);
                }
                all = Resources.FindObjectsOfTypeAll<Mesh>();
            }
            meshBaseline.Clear();
            foreach (var m in all) if (m) meshBaseline.Add(m.GetInstanceID());
        }

        // The art scripts can move between assemblies (they now have their own asmdef), so search all of them.
        static System.Type devToolsType;
        static System.Type FindType(string name)
        {
            if (devToolsType != null) return devToolsType;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(name, false);
                if (t != null && typeof(MonoBehaviour).IsAssignableFrom(t)) return devToolsType = t;
            }
            Debug.LogWarning($"[RoomRun] {name} not found, so the environment panel is not shown.");
            return null;
        }

        static IEnumerable<T> Collect<T>(GameObject[] roots) where T : Component
        {
            foreach (var root in roots)
                foreach (var c in root.GetComponentsInChildren<T>(false)) yield return c;
        }

        // Stages generate their meshes and ground colliders over their first frames. Wait until that settles.
        IEnumerator WaitForStageBuild()
        {
            int last = -1, stable = 0;
            for (int frame = 0; frame < 120 && stable < 3; frame++)
            {
                int count = 0;
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                    count += root.GetComponentsInChildren<Renderer>().Length + root.GetComponentsInChildren<Collider>().Length;
                stable = count == last && count > 0 ? stable + 1 : 0;
                last = count;
                yield return null;
            }
        }

        // The playable area this visit: the configured box, or the whole stage when fitToStage is on.
        public Vector3 AreaCenter { get; private set; }
        public Vector2 AreaSize { get; private set; }

        // Grows the room to everything the stage shows, so no part of the environment is cut off.
        void FitToStage(RoomDef room)
        {
            var scene = SceneManager.GetActiveScene();
            bool any = false;
            var b = new Bounds();
            var roots = scene.GetRootGameObjects();
            foreach (var r in Collect<Renderer>(roots))
            {
                if (r is ParticleSystemRenderer || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                // The ground often runs past the dressed area (bare margin out to the horizon). Size by what sits on it.
                if (r.GetComponent<MeshCollider>() || r.name == room.groundMeshName) continue;
                var e = r.bounds;
                if (e.size.x > 300f || e.size.z > 300f) continue; // Sky domes and far backdrops.
                if (any) b.Encapsulate(e); else { b = e; any = true; }
            }
            // Things drawn without a Renderer (GPU-instanced plants) can report their extent through a public
            // Bounds property named WorldBounds on any component in the stage.
            foreach (var mb in Collect<MonoBehaviour>(roots))
            {
                var prop = mb.GetType().GetProperty("WorldBounds");
                if (prop == null || prop.PropertyType != typeof(Bounds)) continue;
                var e = (Bounds)prop.GetValue(mb);
                if (e.size.sqrMagnitude < 1f) continue;
                if (any) b.Encapsulate(e); else { b = e; any = true; }
            }

            // Keep the walkable box on the ground the stage provides.
            bool hasGround = false;
            var g = new Bounds();
            foreach (var c in Collect<Collider>(roots))
            {
                if (c.isTrigger || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                // Ground is a mesh or terrain collider. Props use primitive colliders.
                if (c is BoxCollider || c is SphereCollider || c is CapsuleCollider || c is CharacterController) continue;
                var e = c.bounds;
                if (e.size.x < 10f || e.size.z < 10f) continue;
                if (hasGround) g.Encapsulate(e); else { g = e; hasGround = true; }
            }
            Debug.Log($"[RoomRun] {room.id} stage bounds {(any ? b.ToString() : "none")} ground {(hasGround ? g.ToString() : "none")} scene {scene.name}");
            if (!any) return;
            float x0 = b.min.x, x1 = b.max.x, z0 = b.min.z, z1 = b.max.z;
            if (hasGround)
            {
                x0 = Mathf.Max(x0, g.min.x); x1 = Mathf.Min(x1, g.max.x);
                z0 = Mathf.Max(z0, g.min.z); z1 = Mathf.Min(z1, g.max.z);
            }
            // Mountains and cliffs stay in view but are not walkable: keep the ground near the stage's floor height.
            float wx0 = float.MaxValue, wx1 = float.MinValue, wz0 = float.MaxValue, wz1 = float.MinValue;
            for (float x = x0; x <= x1; x += 2f)
            for (float z = z0; z <= z1; z += 2f)
            {
                if (!Physics.Raycast(new Vector3(x, room.center.y + 200f, z), Vector3.down, out var hit, 400f, Ground.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(hit.point.y - room.center.y) > 4f) continue;
                wx0 = Mathf.Min(wx0, x); wx1 = Mathf.Max(wx1, x); wz0 = Mathf.Min(wz0, z); wz1 = Mathf.Max(wz1, z);
            }
            if (wx0 < wx1 && wz0 < wz1) { x0 = wx0; x1 = wx1; z0 = wz0; z1 = wz1; }

            const float inset = 1.5f;
            var size = new Vector2(x1 - x0 - inset * 2f, z1 - z0 - inset * 2f);
            if (size.x < room.size.x || size.y < room.size.y) return; // Never smaller than the configured arena.
            AreaCenter = new Vector3((x0 + x1) / 2f, room.center.y, (z0 + z1) / 2f);
            AreaSize = size;
            Debug.Log($"[RoomRun] {room.id} fitted to stage: centre {AreaCenter}, size {AreaSize}");
        }

        Vector3 SpawnPoint(DoorSide? side)
        {
            if (side == null) return AreaCenter;
            var dir = side.Value.Dir();
            float reach = dir.x != 0f ? AreaSize.x / 2f : AreaSize.y / 2f;
            // Gates can sit inside the boundary (pulled off a hillside), so start from the gate itself.
            var gate = Doors.Find(d => d.Side == side.Value);
            var origin = gate ? gate.Point : AreaCenter + dir * reach;
            // Step inward from the door until there is room to stand, in case a prop sits in front of it.
            for (float inward = 2.2f; inward < reach * 2f; inward += 1f)
            {
                var p = origin - dir * inward;
                p.y = Ground.Sample(p, AreaCenter.y);
                if (!Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.5f, 0.45f, ~(1 << Ground.ActorLayer), QueryTriggerInteraction.Ignore))
                    return p;
            }
            return AreaCenter;
        }

        IEnumerator RunEncounter(RoomDef room)
        {
            InCombat = true;
            if (config.lockDoors) foreach (var d in Doors) d.SetOpen(false);
            yield return new WaitForSeconds(0.45f);

            foreach (var wave in room.waves)
            {
                var telegraphs = new List<(Vector3 pos, EnemyDef def, GameObject mark)>();
                foreach (var entry in wave.spawns)
                {
                    var def = config.Enemy(entry.enemyId);
                    if (def == null) { Debug.LogError($"[RoomRun] Unknown enemy '{entry.enemyId}'."); continue; }
                    for (int i = 0; i < entry.count; i++)
                    {
                        var pos = FindSpawn(room);
                        var mark = MeshLib.Part("SpawnMark", MeshLib.Ring, config.ringEnemy, roomRoot, Vector3.zero, Vector3.one);
                        mark.transform.position = pos + Vector3.up * 0.08f;
                        telegraphs.Add((pos, def, mark));
                    }
                }

                // A ring closes in before anything appears, so no enemy spawns unannounced.
                const float telegraphTime = 0.8f;
                for (float t = 0f; t < telegraphTime; t += Time.deltaTime)
                {
                    float s = Mathf.Lerp(2.4f, 0.7f, t / telegraphTime);
                    foreach (var tg in telegraphs) if (tg.mark) tg.mark.transform.localScale = Vector3.one * s;
                    yield return null;
                }

                foreach (var tg in telegraphs)
                {
                    if (tg.mark) Destroy(tg.mark);
                    SpawnEnemy(tg.def, tg.pos, room);
                }

                while (Enemies.Count > 0) yield return null;
                yield return new WaitForSeconds(0.45f);
            }

            InCombat = false;
            encounter = null;
            Cleared.Add(room.id);
            Bullets.ClearAll();
            foreach (var d in Doors) d.SetOpen(true);
            Player.Health.Heal(1);
            Hud.RefreshMap();
            CheckRunComplete();
        }

        void CheckRunComplete()
        {
            if (RunComplete || !config.combat) return;
            foreach (var r in config.rooms)
                if (r.IsCombat && !Cleared.Contains(r.id)) return;
            RunComplete = true;
            Hud.ShowBanner("모든 방을 정리했다", "Enter 키로 처음부터");
        }

        Vector3 FindSpawn(RoomDef room)
        {
            // In a big stage, spawn around the player rather than across the whole map.
            float hx = Mathf.Min(AreaSize.x / 2f - 2f, 13f), hz = Mathf.Min(AreaSize.y / 2f - 2f, 9f);
            var mid = Player.transform.position;
            mid.x = Mathf.Clamp(mid.x, AreaCenter.x - AreaSize.x / 2f + hx + 2f, AreaCenter.x + AreaSize.x / 2f - hx - 2f);
            mid.z = Mathf.Clamp(mid.z, AreaCenter.z - AreaSize.y / 2f + hz + 2f, AreaCenter.z + AreaSize.y / 2f - hz - 2f);
            var best = mid;
            for (int tries = 0; tries < 24; tries++)
            {
                var p = mid + new Vector3(Random.Range(-hx, hx), 0, Random.Range(-hz, hz));
                if ((p - Player.transform.position).sqrMagnitude < 36f) continue;
                p.y = Ground.Sample(p, room.center.y);
                best = p;
                // Stage props (houses, rocks) are Default-layer colliders, so check everything but actors.
                if (!Physics.CheckCapsule(p + Vector3.up * 0.8f, p + Vector3.up * 1.6f, 0.7f, ~(1 << Ground.ActorLayer), QueryTriggerInteraction.Ignore)) break;
            }
            best.y = Ground.Sample(best, room.center.y);
            return best;
        }

        void SpawnEnemy(EnemyDef def, Vector3 pos, RoomDef room)
        {
            var go = new GameObject("Enemy_" + def.id, typeof(CharacterController), typeof(Health), typeof(EnemyController));
            go.transform.position = pos;
            var e = go.GetComponent<EnemyController>();
            e.Init(def, config, this, room.center.y);
            Enemies.Add(e);
        }
    }
}
