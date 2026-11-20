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
        // Renderers of the character model's own foot ring, kept hidden (the hurt blink must not switch them back on).
        readonly System.Collections.Generic.HashSet<Renderer> hiddenRing = new System.Collections.Generic.HashSet<Renderer>();

        public const float MuzzleHeight = 1.05f;

        [Header("Move")]
        public float moveSpeed = 7f;
        public float acceleration = 60f;
        public float rollSpeed = 13.5f;
        public float rollTime = 0.42f;
        public float rollInvulnerable = 0.36f;
        public float rollCooldown = 0.2f;
        public float dashSlashAfter = 0.1f;   // greatsword: seconds into the dash before an attack turns it into a dash slash

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

            this.cfg = cfg;
            BuildVisual();
            // Kido 11-10: no ring under the player. Kept behind the config switch (off) for co-op identification later.
            if (cfg.playerFootRing) MeshLib.Part("FootRing", MeshLib.Ring, cfg.ringPlayer, transform, Vector3.up * 0.06f, Vector3.one * 0.75f);

            var lg = new GameObject("MuzzleLight");
            lg.transform.SetParent(transform, false);
            muzzle = lg.AddComponent<Light>();
            muzzle.type = LightType.Point;
            muzzle.color = new Color(0.62f, 0.91f, 1f);
            muzzle.range = 5f;
            muzzle.intensity = 0f;
            muzzle.shadows = LightShadows.None;

            Ammo = magazine;
            // 11-19: 1·2·3 keys switch 양손검 / 이기어검 / 사복검 (WeaponHotkeys)
            if (!GetComponent<WeaponHotkeys>()) gameObject.AddComponent<WeaponHotkeys>();
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

        // ---- temporary greatsword look test (11-13). Dev panel 양손검 캐릭터 flips RR.Greatsword; default on. ----
        public const string GreatswordKey = "RR.Greatsword";
        public static bool UseGreatsword => PlayerPrefs.GetInt(GreatswordKey, 1) != 0;
        RoomRunConfig cfg;
        bool usingGreatsword;
        Animator gsAnim;
        SplitBody split;
        GreatswordCombat gs;
        float gsCheck;
        public bool GreatswordActive => usingGreatsword && gs;
        // 11-18 whip test (WhipCombat): rides on the greatsword body; the sword is hidden and its attacks are off
        WhipCombat whip;
        public bool WhipActive => GreatswordActive && whip && whip.enabled;
        void SyncWhip()
        {
            bool w = WhipCombat.UseWhip && !EogeomCombat.UseEogeom && gsAnim && gs;
            if (w && !whip) { whip = gameObject.AddComponent<WhipCombat>(); whip.Setup(this, gsAnim); }
            if (whip && whip.enabled != w) { whip.Cancel(); whip.enabled = w; }
            SyncEogeom();
        }
        // 11-18 이기어검 test (EogeomCombat): same greatsword body, the blade segments fly at the target; wins over the whip toggle
        EogeomCombat eg;
        public bool EogeomActive => GreatswordActive && eg && eg.enabled;
        void SyncEogeom()
        {
            bool e = EogeomCombat.UseEogeom && gsAnim && gs;
            if (e && !eg) { eg = gameObject.AddComponent<EogeomCombat>(); eg.Setup(this, gsAnim); }
            if (eg && eg.enabled != e) { eg.Cancel(); eg.enabled = e; }
        }

        /// 11-19 WeaponHotkeys: apply the RR.Greatsword / RR.Whip / RR.Eogeom toggles now instead of on the 0.5 s poll.
        public void SyncWeaponNow()
        {
            if (!cfg) return;
            if (UseGreatsword != usingGreatsword && cfg.greatswordPrefab) { BuildVisual(); visual.localRotation = Quaternion.LookRotation(AimDir); }
            if (gs) gs.Cancel();
            SyncWhip();
            gsCheck = 0.5f;
        }

        void BuildVisual()
        {
            if (visual) { visual.gameObject.SetActive(false); Destroy(visual.gameObject); }
            hiddenRing.Clear();
            gsAnim = null;
            if (gs) { gs.Cancel(); Destroy(gs); gs = null; }
            if (whip) { Destroy(whip); whip = null; }
            if (eg) { Destroy(eg); eg = null; }
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            usingGreatsword = UseGreatsword && cfg.greatswordPrefab;
            var prefab = usingGreatsword ? cfg.greatswordPrefab : cfg.playerPrefab;
            if (prefab)
            {
                var m = Instantiate(prefab, visual);
                m.transform.localScale = Vector3.one * (usingGreatsword ? cfg.greatswordScale : cfg.playerScale);
                if (usingGreatsword)
                {
                    gsAnim = m.GetComponentInChildren<Animator>();
                    if (gsAnim)
                    {
                        gs = gameObject.AddComponent<GreatswordCombat>();
                        gs.Setup(this, cfg, m.transform, gsAnim);
                        // each swing faces the cursor at once (Kido: slashes go where the mouse points)
                        gs.SwingStarted += () => { if (visual) visual.rotation = Quaternion.LookRotation(AimDir); };
                        // 11-15: the sweep clips twist the chest ~130° off the aim (windup/follow-through); keep the body on the cursor
                        var sf = GetComponent<SwingFacing>() ?? gameObject.AddComponent<SwingFacing>();
                        sf.visual = visual; sf.anim = gsAnim; sf.gs = gs; sf.pc = this;
                        // 11-15: upper/lower body split (berserker clips only; SplitBody.On toggles it)
                        split = GetComponent<SplitBody>() ?? gameObject.AddComponent<SplitBody>();
                        split.visual = visual; split.anim = gsAnim; split.gs = gs; split.pc = this;
                    }
                    if (!GetComponent<VegetationCut>()) gameObject.AddComponent<VegetationCut>();
                    SyncWhip();
                }
                // The character model carries its own glowing foot ring (DD_RingPlayer); hidden unless the switch is on.
                // (11-10: a runtime blank material from Shader.Find broke builds, where that shader is stripped.)
                if (!cfg.playerFootRing)
                    foreach (var r in m.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = r.sharedMaterials;
                        int ring = 0;
                        foreach (var mt in mats) if (mt && mt.name.Contains("RingPlayer")) ring++;
                        if (ring == 0) continue;
                        if (ring == mats.Length) { r.enabled = false; hiddenRing.Add(r); }
                        else
                        {
                            // the ring is one slot of a shared mesh: draw that slot with a shader that draws nothing
                            var blank = Shader.Find("RoomRun/Invisible");
                            if (!blank) { Debug.LogWarning("[RoomRun] RoomRun/Invisible shader missing; foot ring left visible"); continue; }
                            var none = new Material(blank) { name = "RR_Invisible" };
                            for (int i = 0; i < mats.Length; i++) if (mats[i] && mats[i].name.Contains("RingPlayer")) mats[i] = none;
                            r.sharedMaterials = mats;
                        }
                    }
            }
            else
            {
                MeshLib.Part("Stand-in", MeshLib.Capsule, cfg.ringPlayer, visual, Vector3.up * 0.9f, new Vector3(0.7f, 0.9f, 0.7f));
            }
            renderers = visual.GetComponentsInChildren<Renderer>();
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

            // greatsword: attacking out of the dash cuts it short into a lunging dash slash (11-14 feel pass)
            if (Rolling && GreatswordActive && !WhipActive && !EogeomActive && canAct && cmd.fire && rollLeft < rollTime - dashSlashAfter && RunDirector.Instance.config.combat)
            {
                rollLeft = 0f;
                if (cmd.aimDir.sqrMagnitude > 0.01f) AimDir = cmd.aimDir;
                gs.DashSlash();
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
                if (GreatswordActive && gs.Attacking)
                {
                    // swinging: barely steer, and a short ground lunge just before each hit
                    want *= gs.moveWhileSwing;
                    if (gs.LungeVelocity.sqrMagnitude > 0.01f) { vel = gs.LungeVelocity + want; want = vel; }
                }
                if (WhipActive && whip.Attacking) want *= whip.MoveScale;
                if (EogeomActive && eg.Attacking) want *= eg.MoveScale;
                // v3 sweep fix: berserker swings move only by the clip's root path (GreatswordCombat -> RootMove); the run
                // speed is dropped at once instead of decelerating (that slide pushed the feet ~0.3 m every hit)
                if (GreatswordActive && gs.Attacking && gs.Berserker) vel = want;
                else vel = Vector3.MoveTowards(vel, want, acceleration * dt);
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
            // the greatsword replaces the gun: no bullets while it is on
            if (canAct && !Rolling && RunDirector.Instance.config.combat && !GreatswordActive) UpdateGun(cmd);
            if ((gsCheck -= dt) <= 0f)
            {
                gsCheck = 0.5f;
                if (cfg && UseGreatsword != usingGreatsword && cfg.greatswordPrefab) { BuildVisual(); visual.localRotation = Quaternion.LookRotation(AimDir); }
                SyncWhip();
            }
            if (EogeomActive)
            {
                gs.Tick(dt, false, canAct && RunDirector.Instance.config.combat, AimDir, vel, moveSpeed, Rolling);
                eg.Tick(dt, cmd.fire, canAct && !Rolling && RunDirector.Instance.config.combat, AimDir);
            }
            else if (WhipActive)
            {
                gs.Tick(dt, false, canAct && RunDirector.Instance.config.combat, AimDir, vel, moveSpeed, Rolling);
                whip.Tick(dt, cmd.fire, canAct && !Rolling && RunDirector.Instance.config.combat, AimDir);
            }
            else if (GreatswordActive) gs.Tick(dt, cmd.fire, canAct && RunDirector.Instance.config.combat, AimDir, vel, moveSpeed, Rolling);
            UpdateVisual(dt);
            Shader.SetGlobalVector("_LSPush", new Vector4(transform.position.x, transform.position.y, transform.position.z, 1.6f));
        }

        /// v3 sweep fix: root motion from GreatswordCombat's clip root path, moved in the same frame as the pose.
        public Vector3 RootVelocity { get; private set; }
        public void RootMove(Vector3 d, float dt)
        {
            if (!cc || !cc.enabled) return;
            var delta = Ground.LimitClimb(transform.position, d, fallbackY);
            delta.y = Ground.Sample(transform.position + delta, fallbackY) - transform.position.y;
            cc.Move(delta);
            RootVelocity = dt > 1e-5f ? d / dt : Vector3.zero;
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

            if (Rolling && usingGreatsword)
            {
                // ground dash: face the dash, feet stay down
                visual.localPosition = Vector3.zero;
                visual.rotation = Quaternion.RotateTowards(visual.rotation, Quaternion.LookRotation(rollDir), 1440f * dt);
                return;
            }
            if (Rolling)
            {
                float t = 1f - rollLeft / rollTime;
                var axis = Vector3.Cross(Vector3.up, rollDir);
                visual.rotation = Quaternion.AngleAxis(t * 360f, axis) * Quaternion.LookRotation(rollDir);
                visual.localPosition = Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.35f);
                return;
            }

            if (usingGreatsword)
            {
                // the clips carry the body motion; just face the aim (locked to it while swinging)
                visual.localPosition = Vector3.zero;
                // berserker clips (11-15): LoA style, the body faces where it runs and turns to the cursor only to swing
                var flatVel = new Vector3(vel.x, 0f, vel.z);
                var curFwd = visual.forward; curFwd.y = 0f;
                var faceDir = gs && gs.Berserker && split ? split.LegDir(flatVel.sqrMagnitude > 0.25f ? flatVel : Vector3.zero, AimDir, curFwd)
                    : gs && gs.Berserker && flatVel.sqrMagnitude > 0.25f ? flatVel.normalized
                    : gs && gs.Berserker ? curFwd : AimDir;
                faceDir.y = 0f; if (faceDir.sqrMagnitude < 1e-4f) faceDir = AimDir;
                // v3 (Kido: east run with body and sword stuck north): SplitBody off = the body turns to where it runs
                // (cursor when standing) at 720 deg/s; head and a little chest look at the cursor (SplitBody)
                float turn = SplitBody.On ? 1080f : 720f;
                // whip: the whole body squares up to the cursor for each lash (the reference does), snapped at the start
                // 11-18 v8 direction rule (EogeomCombat.DirRule): moving with 이기어검 on, the body turns continuously to the pelvis
                // direction (cursor, leaning <= 30 deg toward the run); the leg blend tree then plays run / strafe / backpedal
                // v12: standing with 이기어검 the body faces the cursor (the greatsword's 65 deg side-on stance turned the shoulder carry sideways)
                if (EogeomActive && EogeomCombat.FF14Base && flatVel.sqrMagnitude <= 0.25f && !eg.BodyToAim)
                {
                    visual.rotation = Quaternion.RotateTowards(visual.rotation, Quaternion.LookRotation(AimDir), 900f * dt);
                    return;
                }
                if (EogeomActive && EogeomCombat.DirRule && flatVel.sqrMagnitude > 0.25f && !eg.BodyToAim)
                {
                    visual.rotation = Quaternion.RotateTowards(visual.rotation, Quaternion.LookRotation(eg.PelvisDir(flatVel)), 900f * dt);
                    return;
                }
                if (EogeomActive && eg.BodyToAim)
                {
                    visual.rotation = Quaternion.RotateTowards(visual.rotation, Quaternion.LookRotation(eg.AttackAim), 2400f * dt);
                    return;
                }
                if (WhipActive && whip.BodyToAim)
                {
                    visual.rotation = Quaternion.RotateTowards(visual.rotation, Quaternion.LookRotation(whip.AttackAim), 2400f * dt);
                    return;
                }
                visual.rotation = gs && gs.Attacking ? Quaternion.AngleAxis(gs.SpinYaw, Vector3.up) * Quaternion.LookRotation(AimDir) * Quaternion.Euler(gs.BodyLean, 0f, 0f)
                    : Quaternion.RotateTowards(visual.rotation, Quaternion.LookRotation(faceDir), turn * dt);
                // hit 3 spin: turn about the planted left foot, not the root
                if (gs && gs.Attacking && gs.SpinYaw > 0f && gs.SpinYaw < 360f)
                {
                    var pv = gs.SpinPivot;
                    visual.position = pv + Quaternion.AngleAxis(gs.SpinYaw, Vector3.up) * (transform.position - pv);
                }
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
            foreach (var r in renderers) if (r) r.enabled = on && !hiddenRing.Contains(r);
        }
    }
}
