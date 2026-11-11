// Yokai behaviours. All movement is steering inside the room, so stages need no navmesh.
using System.Collections.Generic;
using UnityEngine;

namespace RoomRun
{
    [RequireComponent(typeof(CharacterController), typeof(Health))]
    public sealed class EnemyController : MonoBehaviour
    {
        const float MuzzleHeight = 1.05f;

        EnemyDef def;
        RunDirector run;
        CharacterController cc;
        Health health;
        Transform visual;
        Renderer[] renderers;
        Material[][] originalMats, flashMats;
        Material flash;
        float fallbackY, fireTimer, telegraph, flashLeft, contactCool, phase, ringAngle;
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
            health.Damaged += _ => { flashLeft = 0.08f; ApplyFlash(true); };
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
            if (d.prefab)
            {
                var m = Instantiate(d.prefab, visual);
                m.transform.localScale = Vector3.one * s;
            }
            else
            {
                MeshLib.Part("Stand-in", MeshLib.Capsule, cfg.ringEnemy, visual, Vector3.up * s, Vector3.one * s);
            }
            renderers = visual.GetComponentsInChildren<Renderer>();
            originalMats = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                originalMats[i] = renderers[i].sharedMaterials;
                // dark fields: an extra rim slot so the enemy reads against the dark and catches nearby light
                if (FieldLighting.EnemyRim && !(renderers[i] is ParticleSystemRenderer))
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
                case EnemyBehaviour.Dasher:
                    move = TickDash(dt, dirTo);
                    break;
            }

            using (mSep.Auto()) move += Separation() * 2.5f;
            Vector3 delta;
            using (mGround.Auto())
            {
                delta = Ground.LimitClimb(transform.position, move * dt, fallbackY);
                float groundY = Ground.Sample(transform.position + delta, fallbackY);
                delta.y = groundY - transform.position.y;
            }
            using (mMove.Auto()) MoveCheap(delta);

            if (dist < 1.0f + cc.radius && contactCool <= 0f && player.Health.Hit(1)) contactCool = 0.8f;
            contactCool -= dt;

            Face(dirTo, dt);
            Animate(move);

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
                }
                return dashDir * def.moveSpeed * 3.4f;
            }
            if (telegraph > 0f)
            {
                telegraph -= dt;
                dashDir = dirTo;
                if (telegraph <= 0f) dashLeft = 0.42f;
                return Vector3.zero;
            }
            dashWait -= dt;
            if (dashWait <= 0f) telegraph = 0.55f;
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

        void Animate(Vector3 move)
        {
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
            Destroy(gameObject);
        }
    }
}
