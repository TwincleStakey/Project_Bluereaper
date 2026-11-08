// Player: twin-stick movement, dodge roll with invulnerable frames, 12-round gun with a reload.
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoomRun
{
    public struct PlayerCommand
    {
        public Vector2 move;
        public Vector3 aimDir;   // Flat world direction. Zero keeps the last aim.
        public bool fire, dodge, reload, restart;
    }

    public interface ICommandSource
    {
        PlayerCommand Read(PlayerController player, Camera cam);
    }

    // Default input. Actions are rebindable through InputActionRebindingExtensions later.
    public sealed class InputReader : ICommandSource
    {
        readonly InputAction move, aimStick, fire, dodge, reload, restart;

        public InputReader()
        {
            move = new InputAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");

            aimStick = new InputAction("Aim", InputActionType.Value, "<Gamepad>/rightStick");

            fire = new InputAction("Fire", InputActionType.Button, "<Mouse>/leftButton");
            fire.AddBinding("<Gamepad>/rightTrigger");

            dodge = new InputAction("Dodge", InputActionType.Button, "<Keyboard>/space");
            dodge.AddBinding("<Gamepad>/buttonEast");

            reload = new InputAction("Reload", InputActionType.Button, "<Keyboard>/r");
            reload.AddBinding("<Gamepad>/buttonWest");

            restart = new InputAction("Restart", InputActionType.Button, "<Keyboard>/enter");
            restart.AddBinding("<Gamepad>/start");

            move.Enable(); aimStick.Enable(); fire.Enable(); dodge.Enable(); reload.Enable(); restart.Enable();
        }

        public PlayerCommand Read(PlayerController player, Camera cam)
        {
            var c = new PlayerCommand
            {
                move = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f),
                fire = fire.IsPressed(),
                dodge = dodge.WasPressedThisFrame(),
                reload = reload.WasPressedThisFrame(),
                restart = restart.WasPressedThisFrame(),
            };

            var stick = aimStick.ReadValue<Vector2>();
            if (stick.sqrMagnitude > 0.2f)
            {
                c.aimDir = new Vector3(stick.x, 0, stick.y).normalized;
            }
            else if (Mouse.current != null && cam)
            {
                var plane = new Plane(Vector3.up, player.transform.position + Vector3.up * PlayerController.MuzzleHeight);
                var ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                if (plane.Raycast(ray, out float d))
                {
                    var flat = ray.GetPoint(d) - player.transform.position;
                    flat.y = 0;
                    if (flat.sqrMagnitude > 0.04f) c.aimDir = flat.normalized;
                }
            }
            return c;
        }
    }

    [RequireComponent(typeof(CharacterController), typeof(Health))]
    public sealed class PlayerController : MonoBehaviour
    {
        public const float MuzzleHeight = 1.05f;

        [Header("Move")]
        public float moveSpeed = 7f;
        public float acceleration = 60f;
        public float rollSpeed = 13.5f;
        public float rollTime = 0.42f;
        public float rollInvulnerable = 0.36f;
        public float rollCooldown = 0.2f;

        [Header("Gun")]
        public int magazine = 12;
        public float shotInterval = 0.15f;
        public float reloadTime = 1.1f;
        public float bulletSpeed = 24f;
        public float spreadDeg = 2.5f;

        [Header("Hit")]
        public float hitInvulnerable = 1f;

        public ICommandSource Source;
        public bool Frozen;
        public bool DebugInvulnerable;
        public int Ammo { get; private set; }
        public bool Reloading => reloadLeft > 0f;
        public float ReloadProgress => Reloading ? 1f - reloadLeft / reloadTime : 1f;
        public bool Rolling => rollLeft > 0f;
        public Vector3 AimDir { get; private set; } = Vector3.forward;
        public Vector3 Velocity { get; private set; }
        public Health Health { get; private set; }

        CharacterController cc;
        Transform visual;
        Renderer[] renderers;
        Light muzzle;
        Vector3 vel, rollDir;
        float rollLeft, rollCool, shotCool, reloadLeft, hitLeft, walkPhase, recoil;
        float fallbackY;

        public void Init(RoomRunConfig cfg)
        {
            Health = GetComponent<Health>();
            Health.Init(Team.Player, cfg.playerHp);
            Health.Damaged += OnDamaged;
            Health.Died += () => RunDirector.Instance.OnPlayerDied();
            gameObject.layer = Ground.ActorLayer;

            cc = GetComponent<CharacterController>();
            cc.height = 1.7f; cc.center = new Vector3(0, 0.9f, 0); cc.radius = 0.38f;
            cc.skinWidth = 0.03f; cc.stepOffset = 0.3f; cc.minMoveDistance = 0f;

            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            if (cfg.playerPrefab)
            {
                var m = Instantiate(cfg.playerPrefab, visual);
                m.transform.localScale = Vector3.one * cfg.playerScale;
            }
            else
            {
                MeshLib.Part("Stand-in", MeshLib.Capsule, cfg.ringPlayer, visual, Vector3.up * 0.9f, new Vector3(0.7f, 0.9f, 0.7f));
            }
            renderers = visual.GetComponentsInChildren<Renderer>();
            MeshLib.Part("FootRing", MeshLib.Ring, cfg.ringPlayer, transform, Vector3.up * 0.06f, Vector3.one * 0.75f);

            var lg = new GameObject("MuzzleLight");
            lg.transform.SetParent(transform, false);
            muzzle = lg.AddComponent<Light>();
            muzzle.type = LightType.Point;
            muzzle.color = new Color(0.62f, 0.91f, 1f);
            muzzle.range = 5f;
            muzzle.intensity = 0f;
            muzzle.shadows = LightShadows.None;

            Ammo = magazine;
        }

        public void ResetForRun()
        {
            Health.current = Health.max;
            Health.invulnerable = false;
            Ammo = magazine;
            reloadLeft = rollLeft = hitLeft = 0f;
            vel = Vector3.zero;
            SetVisible(true);
            visual.localRotation = Quaternion.identity;
        }

        public void PlaceAt(Vector3 p, float groundFallback)
        {
            fallbackY = groundFallback;
            cc.enabled = false;
            p.y = Ground.Sample(p, groundFallback);
            transform.position = p;
            cc.enabled = true;
            vel = Vector3.zero;
            rollLeft = 0f;
        }

        void OnDamaged(int amount)
        {
            hitLeft = hitInvulnerable;
            Fx.Burst(transform.position + Vector3.up, Fx.Kind.EnemySpark, 14);
            RunDirector.Instance.Rig.Shake(0.55f);
        }

        void Update()
        {
            var dir = RunDirector.Instance;
            var cmd = Source != null ? Source.Read(this, dir.Rig.Cam) : default;
            if (cmd.restart && (dir.PlayerDead || dir.RunComplete)) { dir.Restart(); return; }

            float dt = Time.deltaTime;
            bool canAct = !Frozen && Health.Alive;

            if (cmd.aimDir.sqrMagnitude > 0.01f && canAct) AimDir = cmd.aimDir;

            rollCool -= dt;
            shotCool -= dt;
            hitLeft -= dt;

            if (canAct && cmd.dodge && !Rolling && rollCool <= 0f)
            {
                var m = new Vector3(cmd.move.x, 0, cmd.move.y);
                rollDir = m.sqrMagnitude > 0.01f ? m.normalized : AimDir;
                rollLeft = rollTime;
                rollCool = rollTime + rollCooldown;
            }

            Vector3 want;
            if (Rolling)
            {
                rollLeft -= dt;
                want = rollDir * rollSpeed;
                vel = want;
            }
            else
            {
                want = canAct ? new Vector3(cmd.move.x, 0, cmd.move.y) * moveSpeed : Vector3.zero;
                vel = Vector3.MoveTowards(vel, want, acceleration * dt);
            }

            var delta = Ground.LimitClimb(transform.position, vel * dt, fallbackY);
            float groundY = Ground.Sample(transform.position + delta, fallbackY);
            delta.y = groundY - transform.position.y;
            if (cc.enabled) cc.Move(delta);
            Velocity = vel;

            Health.invulnerable = DebugInvulnerable || Frozen || hitLeft > 0f || (Rolling && rollLeft > rollTime - rollInvulnerable);

            if (canAct && Reloading)
            {
                reloadLeft -= dt;
                if (reloadLeft <= 0f) { reloadLeft = 0f; Ammo = magazine; }
            }
            if (canAct && !Rolling && RunDirector.Instance.config.combat) UpdateGun(cmd);
            UpdateVisual(dt);
            Shader.SetGlobalVector("_LSPush", new Vector4(transform.position.x, transform.position.y, transform.position.z, 1.6f));
        }

        void UpdateGun(PlayerCommand cmd)
        {
            if (Reloading) return;
            if ((cmd.reload && Ammo < magazine) || Ammo == 0)
            {
                reloadLeft = reloadTime;
                return;
            }
            if (cmd.fire && shotCool <= 0f)
            {
                shotCool = shotInterval;
                Ammo--;
                var d = Quaternion.Euler(0, Random.Range(-spreadDeg, spreadDeg), 0) * AimDir;
                var pos = transform.position + Vector3.up * MuzzleHeight + AimDir * 0.85f;
                RunDirector.Instance.Bullets.Fire(Team.Player, pos, d, bulletSpeed);
                muzzle.transform.position = pos;
                muzzle.intensity = 7f;
                recoil = 0.16f;
                RunDirector.Instance.Rig.Shake(0.09f);
            }
        }

        void UpdateVisual(float dt)
        {
            muzzle.intensity = Mathf.MoveTowards(muzzle.intensity, 0f, 60f * dt);
            recoil = Mathf.MoveTowards(recoil, 0f, 1.2f * dt);

            if (!Health.Alive) { SetVisible(false); return; }

            // Blink while invulnerable after a hit.
            SetVisible(hitLeft <= 0f || Mathf.Repeat(Time.time * 14f, 1f) > 0.45f);

            if (Rolling)
            {
                float t = 1f - rollLeft / rollTime;
                var axis = Vector3.Cross(Vector3.up, rollDir);
                visual.rotation = Quaternion.AngleAxis(t * 360f, axis) * Quaternion.LookRotation(rollDir);
                visual.localPosition = Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.35f);
                return;
            }

            float speed01 = Mathf.Clamp01(new Vector2(vel.x, vel.z).magnitude / moveSpeed);
            walkPhase += speed01 * dt * 13f;
            float bob = Mathf.Abs(Mathf.Sin(walkPhase)) * 0.09f * speed01;
            float sway = Mathf.Sin(walkPhase) * 4f * speed01;
            var lean = Vector3.Cross(Vector3.up, vel) * 0.9f;
            visual.rotation = Quaternion.Euler(lean.x, 0, lean.z) * Quaternion.LookRotation(AimDir) * Quaternion.Euler(0, 0, sway);
            visual.localPosition = Vector3.up * bob - AimDir * recoil;
        }

        void SetVisible(bool on)
        {
            foreach (var r in renderers) if (r) r.enabled = on;
        }
    }
}
