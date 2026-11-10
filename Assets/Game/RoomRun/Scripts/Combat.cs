// Shared combat pieces: ground queries, health, pooled bullets and burst effects.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoomRun
{
    public enum Team { Player, Enemy }

    public static class Ground
    {
        // Built-in layers only, so the project's TagManager stays untouched.
        public const int WallLayer = 1;   // TransparentFX
        public const int ActorLayer = 2;  // Ignore Raycast
        public static readonly int GroundMask = ~((1 << WallLayer) | (1 << ActorLayer));

        // Horizontal move that refuses cliffs and mountainsides, sliding along them on one axis if it can.
        public static Vector3 LimitClimb(Vector3 pos, Vector3 delta, float fallback, float maxRise = 1.2f)
        {
            bool Ok(Vector3 d)
            {
                float flat = new Vector2(d.x, d.z).magnitude;
                if (flat < 1e-5f) return true;
                float rise = Sample(pos + d, fallback) - pos.y;
                return rise <= Mathf.Max(0.3f, flat * maxRise);
            }
            delta.y = 0f;
            if (Ok(delta)) return delta;
            var x = new Vector3(delta.x, 0, 0);
            if (Ok(x)) return x;
            var z = new Vector3(0, 0, delta.z);
            return Ok(z) ? z : Vector3.zero;
        }

        public static float Sample(Vector3 p, float fallback)
        {
            var from = new Vector3(p.x, fallback + 40f, p.z);
            return Physics.Raycast(from, Vector3.down, out var hit, 120f, GroundMask, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : fallback;
        }
    }

    public sealed class Health : MonoBehaviour
    {
        public Team team;
        public int max = 5;
        public int current = 5;
        public bool invulnerable;
        public event Action<int> Damaged;
        public event Action Died;

        public bool Alive => current > 0;

        public void Init(Team t, int hp)
        {
            team = t;
            max = current = hp;
        }

        public bool Hit(int amount)
        {
            if (!Alive || invulnerable) return false;
            current = Mathf.Max(0, current - amount);
            Damaged?.Invoke(amount);
            if (current == 0) Died?.Invoke();
            return true;
        }

        public void Heal(int amount) => current = Mathf.Min(max, current + amount);
    }

    public static class MeshLib
    {
        static Mesh ring, sphere, capsule, cube;

        public static Mesh Ring => ring ? ring : ring = BuildRing(0.82f, 1f, 48);
        public static Mesh Sphere => sphere ? sphere : sphere = Builtin(PrimitiveType.Sphere);
        public static Mesh Capsule => capsule ? capsule : capsule = Builtin(PrimitiveType.Capsule);
        public static Mesh Cube => cube ? cube : cube = Builtin(PrimitiveType.Cube);

        static Mesh Builtin(PrimitiveType t)
        {
            var g = GameObject.CreatePrimitive(t);
            var m = g.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.Destroy(g);
            return m;
        }

        static Mesh BuildRing(float inner, float outer, int seg)
        {
            var v = new Vector3[seg * 2];
            var t = new int[seg * 6];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v[i * 2] = d * inner;
                v[i * 2 + 1] = d * outer;
                int n = (i + 1) % seg;
                t[i * 6] = i * 2; t[i * 6 + 1] = n * 2; t[i * 6 + 2] = i * 2 + 1;
                t[i * 6 + 3] = i * 2 + 1; t[i * 6 + 4] = n * 2; t[i * 6 + 5] = n * 2 + 1;
            }
            var m = new Mesh { name = "RoomRunRing", vertices = v, triangles = t };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        public static GameObject Part(string name, Mesh mesh, Material mat, Transform parent, Vector3 localPos, Vector3 localScale)
        {
            var g = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localScale = localScale;
            g.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }
    }

    public sealed class BulletSystem : MonoBehaviour
    {
        class Bullet
        {
            public GameObject go;
            public Team team;
            public Vector3 dir;
            public float speed, life, radius;
            public int damage;
        }

        readonly List<Bullet> live = new List<Bullet>();
        readonly Stack<Bullet> poolPlayer = new Stack<Bullet>();
        readonly Stack<Bullet> poolEnemy = new Stack<Bullet>();
        readonly RaycastHit[] hits = new RaycastHit[12];
        // Health of each collider a bullet touched (null = not an actor), so it is not looked up again every frame.
        readonly Dictionary<int, Health> healthOf = new Dictionary<int, Health>();
        RoomRunConfig cfg;

        public int LiveEnemyBullets { get; private set; }

        // Live bullets, read by FieldLighting to hang lights on the nearest ones.
        public int LiveCount => live.Count;
        public void GetLive(int i, out Vector3 pos, out Team team) { pos = live[i].go.transform.position; team = live[i].team; }

        public void Init(RoomRunConfig config) => cfg = config;

        // Read by test bots to dodge. Position and velocity of each enemy bullet.
        public void ForEachEnemyBullet(Action<Vector3, Vector3> visit)
        {
            foreach (var b in live)
                if (b.team == Team.Enemy) visit(b.go.transform.position, b.dir * b.speed);
        }

        public void Fire(Team team, Vector3 pos, Vector3 dir, float speed, int damage = 1)
        {
            var pool = team == Team.Player ? poolPlayer : poolEnemy;
            var b = pool.Count > 0 ? pool.Pop() : Create(team);
            dir.y = 0;
            dir.Normalize();
            b.dir = dir;
            b.speed = speed;
            b.damage = damage;
            b.life = team == Team.Player ? 1.6f : 6f;
            b.go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir));
            b.go.SetActive(true);
            live.Add(b);
        }

        Bullet Create(Team team)
        {
            var go = new GameObject(team == Team.Player ? "PlayerBullet" : "EnemyBullet");
            go.transform.SetParent(transform, false);
            var b = new Bullet { go = go, team = team };
            if (team == Team.Player)
            {
                // Cool and long, so it never reads as an enemy shot.
                var p = MeshLib.Part("Body", MeshLib.Capsule, cfg.playerBullet, go.transform, Vector3.zero, new Vector3(0.13f, 0.34f, 0.13f));
                p.transform.localRotation = Quaternion.Euler(90, 0, 0);
                b.radius = 0.14f;
            }
            else
            {
                // Warm and round, with a bright core.
                MeshLib.Part("Body", MeshLib.Sphere, cfg.enemyBullet, go.transform, Vector3.zero, Vector3.one * 0.44f);
                var core = MeshLib.Part("Core", MeshLib.Sphere, cfg.enemyBulletCore, go.transform, new Vector3(0, 0.1f, 0), Vector3.one * 0.26f);
                core.GetComponent<MeshRenderer>().sortingOrder = 1;
                b.radius = 0.2f;
            }
            return b;
        }

        void Recycle(int index)
        {
            var b = live[index];
            b.go.SetActive(false);
            (b.team == Team.Player ? poolPlayer : poolEnemy).Push(b);
            live.RemoveAt(index);
        }

        public void ClearAll()
        {
            healthOf.Clear();   // a new room: colliders change
            for (int i = live.Count - 1; i >= 0; i--) Recycle(i);
            LiveEnemyBullets = 0;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            int enemyCount = 0;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var b = live[i];
                b.life -= dt;
                var pos = b.go.transform.position;
                float dist = b.speed * dt;
                if (b.life <= 0f || Resolve(b, pos, dist))
                {
                    Recycle(i);
                    continue;
                }
                b.go.transform.position = pos + b.dir * dist;
                if (b.team == Team.Enemy) enemyCount++;
            }
            LiveEnemyBullets = enemyCount;
        }

        // True when the bullet stops this frame.
        bool Resolve(Bullet b, Vector3 pos, float dist)
        {
            int n = Physics.SphereCastNonAlloc(pos, b.radius, b.dir, hits, dist, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            RaycastHit best = default;
            Health bestHealth = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.collider is TerrainCollider) continue;   // terrain is always floor
                int id = h.collider.GetInstanceID();
                if (!healthOf.TryGetValue(id, out var health)) healthOf[id] = health = h.collider.GetComponentInParent<Health>();
                if (health)
                {
                    if (health.team == b.team || !health.Alive || health.invulnerable) continue;
                }
                else
                {
                    bool wall = h.collider.gameObject.layer == Ground.WallLayer;
                    // Ignore ground: floors face up, and a shot starting inside a slope reports distance 0.
                    if (!wall && (h.distance <= 0f || Mathf.Abs(h.normal.y) > 0.6f)) continue;
                }
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = h;
                    bestHealth = health;
                }
            }
            if (bestDist == float.MaxValue) return false;
            if (bestHealth) bestHealth.Hit(b.damage);
            var at = best.distance > 0f ? best.point : pos;
            Fx.Burst(at, b.team == Team.Player ? Fx.Kind.PlayerSpark : Fx.Kind.EnemySpark, 6);
            return true;
        }
    }

    public sealed class Fx : MonoBehaviour
    {
        public enum Kind { PlayerSpark, EnemySpark, Death }

        static Fx instance;
        ParticleSystem playerSpark, enemySpark, death;

        public void Init(RoomRunConfig cfg)
        {
            instance = this;
            playerSpark = Make("PlayerSpark", cfg.playerBullet, 0.08f, 0.32f, 2f, 6f);
            enemySpark = Make("EnemySpark", cfg.enemyBulletCore, 0.08f, 0.32f, 2f, 6f);
            death = Make("Death", cfg.enemyBullet, 0.16f, 0.8f, 3f, 11f);
        }

        ParticleSystem Make(string n, Material mat, float size, float life, float s0, float s1)
        {
            var g = new GameObject(n);
            g.transform.SetParent(transform, false);
            var ps = g.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.5f, life);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startSpeed = new ParticleSystem.MinMaxCurve(s0, s1);
            main.gravityModifier = 1.2f;
            main.maxParticles = 512;
            var em = ps.emission;
            em.enabled = false;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.1f;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0));
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = MeshLib.Cube;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        public static void Burst(Vector3 pos, Kind kind, int count)
        {
            if (!instance) return;
            var ps = kind switch
            {
                Kind.PlayerSpark => instance.playerSpark,
                Kind.EnemySpark => instance.enemySpark,
                _ => instance.death,
            };
            var p = new ParticleSystem.EmitParams { position = pos, applyShapeToPosition = true };
            ps.Emit(p, count);
        }

        public static void ClearAll()
        {
            if (!instance) return;
            instance.playerSpark.Clear();
            instance.enemySpark.Clear();
            instance.death.Clear();
        }
    }
}
