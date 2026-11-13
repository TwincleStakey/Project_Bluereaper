// Yokai behaviours. All movement is steering inside the room, so stages need no navmesh.
using System.Collections.Generic;
using UnityEngine;

namespace RoomRun
{
    [RequireComponent(typeof(CharacterController), typeof(Health))]
    public sealed class EnemyController : MonoBehaviour
    {
        const float MuzzleHeight = 1.05f;
        const float DashMul = 3.4f;
        const float CastRecover = 0.7f; // seconds an animated shooter stays planted after its volley
        float castHold;

        EnemyDef def;
        RunDirector run;
        CharacterController cc;
        Health health;
        Transform visual;
        Renderer[] renderers;
        Material[][] originalMats, flashMats, baseMats;
        public const string ClassicKey = "RR.ClassicEnemies";
        GameObject model, modelPrefab;
        Animator anim;
        EnemyGait gait;
        float hipWorld;
        static readonly int PRunMul = Animator.StringToHash("RunMul");
        bool dead;
        static readonly int StMove = Animator.StringToHash("Move"), StRun = Animator.StringToHash("Run"),
            StAttack = Animator.StringToHash("Attack"), StDie = Animator.StringToHash("Die"), PMoveMul = Animator.StringToHash("MoveMul");
        int animState;
        Material flash;
        float fallbackY, fireTimer, telegraph, flashLeft, flashCool, contactCool, phase, ringAngle;
        float dashWait, dashLeft;
        Vector3 dashDir;
        int strafeSign;

        public EnemyDef Def => def;

        public void Init(EnemyDef d, RoomRunConfig cfg, RunDirector director, float groundFallback)
        {
            def = d;
            run = director;
            fallbackY = groundFallback;
            gameObject.layer = Ground.ActorLayer;

            health = GetComponent<Health>();
            health.Init(Team.Enemy, d.hp);
            // QA 11-13: under steady fire the full-white material swap kept enemies white most of the time and read as a
            // pale silhouette over the player; now a short blink at most ~3 times a second
            health.Damaged += _ => { if (flashCool > 0f) return; flashLeft = 0.05f; flashCool = 0.3f; ApplyFlash(true); };
            health.Died += Die;

            cc = GetComponent<CharacterController>();
            float s = d.scale;
            cc.height = 1.8f * s; cc.center = new Vector3(0, 0.95f * s, 0); cc.radius = 0.5f * s;
            cc.skinWidth = 0.03f; cc.stepOffset = 0.3f; cc.minMoveDistance = 0f;
            // 11-11 perf: in a crowd the controllers overlap each other all the time and the depenetration pass was
            // most of cc.Move's cost (150 enemies: 3.8 ms); separation steering already keeps them apart
            cc.enableOverlapRecovery = false;

            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            // dev panel toggle (LookStudyDevTools, 몬스터 > 기존 몬스터): spawn the old code-made yokai instead
            modelPrefab = PlayerPrefs.GetInt(ClassicKey, 0) != 0 && d.classicPrefab ? d.classicPrefab : d.prefab;
            if (modelPrefab)
            {
                // 11-13 dark monsters: skinned + Animator models are pooled, since instantiating one is far dearer than the old meshes
                model = VisualPool.Take(modelPrefab, visual);
                model.transform.localScale = Vector3.one * s;
                anim = model.GetComponentInChildren<Animator>();
                if (anim) { anim.Rebind(); anim.Play(StMove, 0, Random.value); animState = StMove; }
                // 11-13 Kido: feet slid. Walk/run clips play at the rate the body moves: stride (hip heights per second) x hip height in the world
                gait = model.GetComponentInChildren<EnemyGait>();
                if (anim && gait && anim.isHuman) hipWorld = anim.humanScale * anim.transform.lossyScale.y;
            }
            else
            {
                MeshLib.Part("Stand-in", MeshLib.Capsule, cfg.ringEnemy, visual, Vector3.up * s, Vector3.one * s);
            }
            renderers = visual.GetComponentsInChildren<Renderer>();
            originalMats = new Material[renderers.Length][];
            baseMats = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                originalMats[i] = baseMats[i] = renderers[i].sharedMaterials;
                // dark fields: an extra rim slot so the enemy reads against the dark and catches nearby light
                // 11-13: not on the skinned dark monsters; seen from above their tall sides are all grazing angles and the rim
                // washed them to grey silhouettes
                if (FieldLighting.EnemyRim && !(renderers[i] is ParticleSystemRenderer) && !(renderers[i] is SkinnedMeshRenderer))
                {
                    var m = new Material[originalMats[i].Length + 1];
                    originalMats[i].CopyTo(m, 0); m[m.Length - 1] = FieldLighting.EnemyRim;
                    originalMats[i] = m; renderers[i].sharedMaterials = m;
                }
            }
            flash = cfg.hitFlash;

            // 11-12 Kido: no red rings under enemies (every field)
            // MeshLib.Part("FootRing", MeshLib.Ring, cfg.ringEnemy, transform, Vector3.up * 0.06f, Vector3.one * (0.85f * s));

            fireTimer = Random.Range(0.6f, 1f) * d.fireInterval;
            dashWait = Random.Range(0.8f, 1.4f);
            strafeSign = Random.value < 0.5f ? -1 : 1;
            phase = Random.value * 10f;
        }

        void Update()
        {
            if (dead) return;
            var player = run.Player;
            if (!player || !player.Health.Alive || run.Transitioning) { Animate(Vector3.zero); return; }

            float dt = Time.deltaTime;
            var to = player.transform.position - transform.position;
            to.y = 0;
            float dist = to.magnitude;
            var dirTo = dist > 0.001f ? to / dist : transform.forward;

            Vector3 move = Vector3.zero;
            switch (def.behaviour)
            {
                case EnemyBehaviour.Chaser:
                    move = dirTo * def.moveSpeed;
                    break;
                case EnemyBehaviour.Shooter:
                    float want = 8f;
                    float radial = Mathf.Clamp(dist - want, -1f, 1f);
                    var strafe = Vector3.Cross(Vector3.up, dirTo) * strafeSign;
                    move = (dirTo * radial + strafe * 0.55f).normalized * def.moveSpeed;
                    TickFire(dt, dirTo, Aimed);
                    break;
                case EnemyBehaviour.Spinner:
                    move = dirTo * def.moveSpeed;
                    TickFire(dt, dirTo, Ring);
                    break;
            }
            // 11-13 Kido: casters slid while shooting. Animated shooters plant their feet from the wind-up until the cast
            // has played out, then walk on; the separation push is weakened meanwhile so the crowd does not shove them.
            bool planted = anim && castHold > 0f;
            if (planted) { castHold -= dt; move = Vector3.zero; }
            switch (def.behaviour)
            {
                case EnemyBehaviour.Dasher:
                    move = TickDash(dt, dirTo);
                    break;
            }

            using (mSep.Auto()) move += Separation() * (planted ? 0.6f : 2.5f);
            Vector3 delta;
            using (mGround.Auto())
            {
                delta = Ground.LimitClimb(transform.position, move * dt, fallbackY);
                float groundY = Ground.Sample(transform.position + delta, fallbackY);
                delta.y = groundY - transform.position.y;
            }
            using (mMove.Auto()) MoveCheap(delta);

            if (dist < 1.0f + cc.radius && contactCool <= 0f && player.Health.Hit(1)) { contactCool = 0.8f; PlayAnim(StAttack, true); }
            contactCool -= dt;

            // an animated shooter walks the way it goes (it has no sidestep clip, so strafing while facing the player slid);
            // it turns to the player when it plants to cast
            var flat = new Vector3(move.x, 0f, move.z);
            bool walkFacing = anim && def.behaviour == EnemyBehaviour.Shooter && !planted && telegraph <= 0f && flat.sqrMagnitude > 0.25f;
            Face(walkFacing ? flat.normalized : dirTo, dt);
            Animate(move);

            if (flashCool > 0f) flashCool -= dt;
            if (flashLeft > 0f)
            {
                flashLeft -= dt;
                if (flashLeft <= 0f) ApplyFlash(false);
            }
        }

        // 11-11 perf: CharacterController.Move cost ~25 us per enemy in a crowd (3.8 ms for 150), most of it resolving
        // contacts with the other enemies, which separation steering already keeps apart. Enemies now move by one
        // sphere cast against everything except actors (walls, props, stage colliders) and slide along what they hit;
        // the controller stays on as their collider for the player and bullets.
        static readonly int SolidMask = ~(1 << Ground.ActorLayer);
        void MoveCheap(Vector3 delta)
        {
            var pos = transform.position;
            var flat = new Vector3(delta.x, 0f, delta.z);
            float len = flat.magnitude;
            if (len > 1e-5f)
            {
                float r = cc.radius * 0.9f;
                var origin = pos + Vector3.up * (cc.center.y);
                if (Physics.SphereCast(origin, r, flat / len, out var hit, len + cc.skinWidth, SolidMask, QueryTriggerInteraction.Ignore))
                {
                    // stop at the contact and slide the rest along the surface
                    float go = Mathf.Max(0f, hit.distance - cc.skinWidth);
                    var n = hit.normal; n.y = 0f;
                    var slide = n.sqrMagnitude > 1e-6f ? Vector3.ProjectOnPlane(flat * ((len - go) / len), n.normalized) : Vector3.zero;
                    flat = flat / len * go;
                    if (slide.sqrMagnitude > 1e-8f && !Physics.SphereCast(origin + flat, r, slide.normalized, out _, slide.magnitude + cc.skinWidth, SolidMask, QueryTriggerInteraction.Ignore))
                        flat += slide;
                }
            }
            transform.position = pos + flat + Vector3.up * delta.y;
        }

        void TickFire(float dt, Vector3 dirTo, System.Action<Vector3> volley)
        {
            fireTimer -= dt;
            if (telegraph > 0f)
            {
                telegraph -= dt;
                if (telegraph <= 0f)
                {
                    volley(dirTo);
                    fireTimer = def.fireInterval;
                }
            }
            else if (fireTimer <= 0f)
            {
                telegraph = 0.32f; // Swell before firing, so the volley can be read.
                PlayAnim(StAttack, true);
                castHold = telegraph + CastRecover;
            }
        }

        Vector3 TickDash(float dt, Vector3 dirTo)
        {
            if (dashLeft > 0f)
            {
                dashLeft -= dt;
                if (dashLeft <= 0f)
                {
                    Aimed(dirTo);
                    dashWait = Random.Range(1.1f, 1.8f);
                    PlayAnim(StMove, false);
                }
                return dashDir * def.moveSpeed * DashMul;
            }
            if (telegraph > 0f)
            {
                telegraph -= dt;
                dashDir = dirTo;
                if (telegraph <= 0f) { dashLeft = 0.42f; PlayAnim(StRun, false); }
                return Vector3.zero;
            }
            dashWait -= dt;
            if (dashWait <= 0f) { telegraph = 0.55f; PlayAnim(StAttack, true); }
            return dirTo * def.moveSpeed;
        }

        void Aimed(Vector3 dirTo)
        {
            var pos = transform.position + Vector3.up * MuzzleHeight;
            int n = Mathf.Max(1, def.bulletCount);
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0f : i / (n - 1f) - 0.5f;
                var d = Quaternion.Euler(0, t * def.spreadDeg, 0) * dirTo;
                run.Bullets.Fire(Team.Enemy, pos + d * (cc.radius + 0.25f), d, def.bulletSpeed);
            }
        }

        void Ring(Vector3 dirTo)
        {
            var pos = transform.position + Vector3.up * MuzzleHeight;
            int n = Mathf.Max(1, def.bulletCount);
            for (int i = 0; i < n; i++)
            {
                var d = Quaternion.Euler(0, ringAngle + i * 360f / n, 0) * Vector3.forward;
                run.Bullets.Fire(Team.Enemy, pos + d * (cc.radius + 0.25f), d, def.bulletSpeed);
            }
            ringAngle += 360f / n * 0.5f;
            if (def.alsoAimed)
            {
                float aimedSpeed = def.bulletSpeed * 1.35f;
                for (int i = -2; i <= 2; i++)
                {
                    var d = Quaternion.Euler(0, i * 9f, 0) * dirTo;
                    run.Bullets.Fire(Team.Enemy, pos + d * (cc.radius + 0.25f), d, aimedSpeed);
                }
            }
        }

        // 11-11 perf (150 enemies: 174 -> 64 fps): separation compared every enemy with every other one, reading both
        // transforms and colliders each time (N^2 native calls). Now positions and radii are read once a frame into
        // arrays and bucketed into a 2 m grid; each enemy looks only at its own and the 8 neighbouring cells.
        // read by PerfProbe
        static readonly Unity.Profiling.ProfilerMarker mSep = new Unity.Profiling.ProfilerMarker("Enemy.Separation"),
            mGround = new Unity.Profiling.ProfilerMarker("Enemy.Ground"), mMove = new Unity.Profiling.ProfilerMarker("Enemy.Move");
        static int sepFrame = -1;
        static Vector2[] sepPos = new Vector2[256];
        static float[] sepRad = new float[256];
        static readonly Dictionary<long, List<int>> sepGrid = new Dictionary<long, List<int>>();
        static readonly Stack<List<int>> sepPool = new Stack<List<int>>();
        const float SepCell = 3f; // >= the largest separation reach, (r1 + r2) * 1.3
        int sepIndex = -1;

        static long Cell(int x, int z) => ((long)x << 32) ^ (uint)z;

        void BuildSeparation()
        {
            sepFrame = Time.frameCount;
            foreach (var l in sepGrid.Values) { l.Clear(); sepPool.Push(l); }
            sepGrid.Clear();
            var list = run.Enemies;
            if (sepPos.Length < list.Count) { sepPos = new Vector2[list.Count * 2]; sepRad = new float[list.Count * 2]; }
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (!e) { sepRad[i] = -1f; continue; }
                var p = e.transform.position;
                sepPos[i] = new Vector2(p.x, p.z); sepRad[i] = e.cc.radius; e.sepIndex = i;
                long key = Cell(Mathf.FloorToInt(p.x / SepCell), Mathf.FloorToInt(p.z / SepCell));
                if (!sepGrid.TryGetValue(key, out var cell)) { cell = sepPool.Count > 0 ? sepPool.Pop() : new List<int>(); sepGrid[key] = cell; }
                cell.Add(i);
            }
        }

        Vector3 Separation()
        {
            if (sepFrame != Time.frameCount) BuildSeparation();
            int me = sepIndex;
            if (me < 0 || me >= run.Enemies.Count || run.Enemies[me] != this) return Vector3.zero; // spawned this frame
            Vector2 mp = sepPos[me]; float mr = sepRad[me];
            int cx = Mathf.FloorToInt(mp.x / SepCell), cz = Mathf.FloorToInt(mp.y / SepCell);
            Vector2 push = Vector2.zero;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!sepGrid.TryGetValue(Cell(cx + dx, cz + dz), out var cell)) continue;
                foreach (int j in cell)
                {
                    if (j == me || sepRad[j] < 0f) continue;
                    var d = mp - sepPos[j];
                    float m = d.magnitude;
                    float r = (mr + sepRad[j]) * 1.3f;
                    if (m > 0.001f && m < r) push += d / m * (1f - m / r);
                }
            }
            return new Vector3(push.x, 0f, push.y);
        }

        void Face(Vector3 dirTo, float dt)
        {
            visual.rotation = Quaternion.Slerp(visual.rotation, Quaternion.LookRotation(dirTo), 1f - Mathf.Exp(-10f * dt));
        }

        // Animated models: the clip carries the motion; only the walk speed follows the steering speed.
        void PlayAnim(int state, bool restart)
        {
            if (!anim || (!restart && animState == state)) return;
            animState = state;
            anim.CrossFadeInFixedTime(state, 0.12f, 0, 0f);
        }

        void Animate(Vector3 move)
        {
            if (anim)
            {
                // Attack hands back to Move by itself (controller transition)
                if (animState == StAttack && !anim.IsInTransition(0) && anim.GetCurrentAnimatorStateInfo(0).shortNameHash == StMove) animState = StMove;
                if (gait && hipWorld > 0.01f)
                {
                    // a standing enemy (telegraph, cornered) still shuffles a little rather than freezing mid-stride
                    anim.SetFloat(PMoveMul, Mathf.Clamp(move.magnitude / (gait.moveStride * hipWorld), 0.25f, 3f));
                    anim.SetFloat(PRunMul, Mathf.Clamp(def.moveSpeed * DashMul / (gait.runStride * hipWorld), 0.5f, 3f));
                }
                else anim.SetFloat(PMoveMul, Mathf.Clamp(move.magnitude / Mathf.Max(0.1f, def.moveSpeed), 0.35f, 1.6f));
                return;
            }
            phase += Time.deltaTime * (5f + move.magnitude * 2f);
            float hop = Mathf.Abs(Mathf.Sin(phase)) * 0.12f * Mathf.Clamp01(move.magnitude);
            float swell = telegraph > 0f ? 1.14f + Mathf.Sin(Time.time * 40f) * 0.03f : 1f;
            visual.localPosition = Vector3.up * hop;
            visual.localScale = new Vector3(swell, 2f - swell, swell);
        }

        void ApplyFlash(bool on)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i]) continue;
                if (on)
                {
                    // built once per renderer instead of on every hit
                    if (flashMats == null) flashMats = new Material[renderers.Length][];
                    if (flashMats[i] == null) { flashMats[i] = new Material[originalMats[i].Length]; for (int k = 0; k < flashMats[i].Length; k++) flashMats[i][k] = flash; }
                    renderers[i].sharedMaterials = flashMats[i];
                }
                else
                {
                    renderers[i].sharedMaterials = originalMats[i];
                }
            }
        }

        void Die()
        {
            // The only place with a big effect, by the project's readability rule.
            Fx.Burst(transform.position + Vector3.up, Fx.Kind.Death, 26);
            run.Rig.Shake(0.28f);
            run.Enemies.Remove(this);
            if (!anim) { Destroy(gameObject); return; }

            // animated: the body plays its death, stays a moment, sinks, and the model goes back to the pool
            dead = true;
            cc.enabled = false; // bullets and the player pass through the corpse
            if (flashLeft > 0f) { flashLeft = 0f; ApplyFlash(false); }
            animState = StDie;
            anim.CrossFadeInFixedTime(StDie, 0.08f, 0, 0f);
            StartCoroutine(Corpse());
        }

        System.Collections.IEnumerator Corpse()
        {
            yield return new WaitForSeconds(1.6f);
            float t = 0f;
            var start = visual.localPosition;
            while (t < 0.8f)
            {
                t += Time.deltaTime;
                visual.localPosition = start + Vector3.down * (t * 1.2f);
                yield return null;
            }
            ReleaseModel();
            Destroy(gameObject);
        }

        // Back to the pool before the enemy is destroyed (a child cannot be moved out while its parent is being destroyed;
        // enemies cleared with the room just lose their model and the pool refills by instantiating).
        void ReleaseModel()
        {
            if (!model) return;
            // the rim slot was added on top of the prefab's materials; put them back before pooling
            for (int i = 0; i < renderers.Length; i++) if (renderers[i]) renderers[i].sharedMaterials = baseMats[i];
            visual.localPosition = Vector3.zero;
            VisualPool.Return(modelPrefab, model);
            model = null;
        }
    }

    // Pooled enemy models, keyed by prefab. Kept in a DontDestroyOnLoad root so they survive the stage scene swaps.
    static class VisualPool
    {
        static readonly Dictionary<GameObject, Stack<GameObject>> free = new Dictionary<GameObject, Stack<GameObject>>();
        static Transform root;
        static bool quitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { free.Clear(); root = null; quitting = false; Application.quitting += () => quitting = true; }

        public static GameObject Take(GameObject prefab, Transform parent)
        {
            if (free.TryGetValue(prefab, out var s))
                while (s.Count > 0)
                {
                    var g = s.Pop();
                    if (!g) continue;
                    g.transform.SetParent(parent, false);
                    g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity;
                    g.SetActive(true);
                    return g;
                }
            return Object.Instantiate(prefab, parent);
        }

        public static void Return(GameObject prefab, GameObject g)
        {
            if (quitting || !g || !prefab) return;
            if (!root)
            {
                var go = new GameObject("EnemyVisualPool");
                Object.DontDestroyOnLoad(go);
                root = go.transform;
            }
            g.SetActive(false);
            g.transform.SetParent(root, false);
            if (!free.TryGetValue(prefab, out var s)) free[prefab] = s = new Stack<GameObject>();
            s.Push(g);
        }
    }
}
