// Yokai behaviours. All movement is steering inside the room, so stages need no navmesh.
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
        Material[][] originalMats;
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

            MeshLib.Part("FootRing", MeshLib.Ring, cfg.ringEnemy, transform, Vector3.up * 0.06f, Vector3.one * (0.85f * s));

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

            move += Separation() * 2.5f;
            var delta = Ground.LimitClimb(transform.position, move * dt, fallbackY);
            float groundY = Ground.Sample(transform.position + delta, fallbackY);
            delta.y = groundY - transform.position.y;
            cc.Move(delta);

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

        Vector3 Separation()
        {
            Vector3 push = Vector3.zero;
            foreach (var e in run.Enemies)
            {
                if (e == this || !e) continue;
                var d = transform.position - e.transform.position;
                d.y = 0;
                float m = d.magnitude;
                float r = (cc.radius + e.cc.radius) * 1.3f;
                if (m > 0.001f && m < r) push += d / m * (1f - m / r);
            }
            return push;
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
                    var f = new Material[originalMats[i].Length];
                    for (int k = 0; k < f.Length; k++) f[k] = flash;
                    renderers[i].sharedMaterials = f;
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
