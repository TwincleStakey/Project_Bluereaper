// Upper/lower body split locomotion test (twin-stick: WASD moves, mouse aims).
// Legs (root) play an 8-direction strafe blend relative to where the hips face. The spine twists toward the
// aim up to maxTwist degrees; past that the hips (legs) turn to follow. Upper body layer uses an Avatar Mask.
using UnityEngine;
using UnityEngine.InputSystem;

namespace LocoSplit
{
    public enum AttackMode { UpperOnly, FullBody }

    public class LSPlayer : MonoBehaviour
    {
        public Animator anim;
        public Camera cam;

        [Header("Move")]
        public float moveSpeed = 2.4f;
        public float accel = 12f;

        [Header("Facing")]
        public bool faceCursor = true;          // Kido's pick: whole body always faces the cursor, legs blend 8 ways
        public float faceTurnSpeed = 900f;      // deg/s
        public float backSpeedMul = 0.7f;       // backpedal slower
        public float strafeSpeedMul = 0.9f;
        public float attackMoveMul = 0.35f;     // slow down while a full-body slash plays

        [Header("Aim / twist (split mode)")]
        public float maxTwist = 70f;            // spine can twist this far from the hips
        public float hipFollowSpeed = 200f;     // deg/s the hips drift toward the aim while moving
        public float idleTurnThreshold = 50f;   // standing still: hips turn once twist passes this
        public float idleTurnSpeed = 360f;
        public float stanceYawOffset = 0f;      // chest yaw the stance clip already has (greatsword idle is turned)
        [Range(0, 1)] public float twistWeight = 1f;

        [Header("Layers")]
        public bool upperLayerOn = true;
        public AttackMode attackMode = AttackMode.FullBody;

        // Demo/autopilot can drive these instead of the devices.
        [System.NonSerialized] public bool external;
        [System.NonSerialized] public Vector2 extMove;
        [System.NonSerialized] public Vector3 extAimPoint;
        [System.NonSerialized] public bool extAttack;

        public float BodyYaw => bodyYaw;
        public float AimYaw => aimYaw;
        public float Twist => twist;
        public Vector2 LocalMove => animMove;

        float bodyYaw, aimYaw, twist, idleTurnTimer;
        bool idleTurning;
        Vector3 vel;
        Vector2 animMove;
        Transform[] spine;
        static readonly float[] spineShare = { 0.25f, 0.35f, 0.4f };
        int upperLayer = -1, fullLayer = -1;
        float fullWeight;

        static readonly int HMoveX = Animator.StringToHash("MoveX"), HMoveZ = Animator.StringToHash("MoveZ"),
            HSpeed = Animator.StringToHash("Speed"), HAttack = Animator.StringToHash("Attack");

        InputAction move, attack;

        void Awake()
        {
            if (!anim) anim = GetComponentInChildren<Animator>();
            if (!cam) cam = Camera.main;
            move = new InputAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");
            attack = new InputAction("Attack", InputActionType.Button, "<Mouse>/leftButton");
            attack.AddBinding("<Gamepad>/rightTrigger");
            move.Enable(); attack.Enable();

            spine = new[]
            {
                anim.GetBoneTransform(HumanBodyBones.Spine),
                anim.GetBoneTransform(HumanBodyBones.Chest),
                anim.GetBoneTransform(HumanBodyBones.UpperChest),
            };
            upperLayer = anim.GetLayerIndex("Upper");
            fullLayer = anim.GetLayerIndex("AttackFull");
            bodyYaw = aimYaw = transform.eulerAngles.y;
        }

        void OnDestroy() { move?.Dispose(); attack?.Dispose(); }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector2 m = external ? extMove : Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);
            bool atk = external ? extAttack : attack.WasPressedThisFrame();
            if (external) extAttack = false;

            // Aim: mouse onto the ground plane (or the demo's point).
            Vector3 aimPoint = transform.position + Quaternion.Euler(0, aimYaw, 0) * Vector3.forward;
            if (external) aimPoint = extAimPoint;
            else if (Mouse.current != null && cam && !LSDevPanel.MouseOverPanel)
            {
                var plane = new Plane(Vector3.up, transform.position + Vector3.up * 1.0f);
                var ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                if (plane.Raycast(ray, out float d)) aimPoint = ray.GetPoint(d);
            }
            Vector3 toAim = aimPoint - transform.position; toAim.y = 0;
            if (toAim.sqrMagnitude > 0.04f) aimYaw = Mathf.Atan2(toAim.x, toAim.z) * Mathf.Rad2Deg;

            // Move in world space (top-down camera, fixed yaw).
            float camYaw = cam ? cam.transform.eulerAngles.y : 0f;
            Vector3 wishDir = Quaternion.Euler(0, camYaw, 0) * new Vector3(m.x, 0, m.y);
            float fwd = wishDir.sqrMagnitude > 0.001f ? Vector3.Dot(wishDir.normalized, Quaternion.Euler(0, aimYaw, 0) * Vector3.forward) : 1f;
            float dirMul = fwd >= 0 ? Mathf.Lerp(strafeSpeedMul, 1f, fwd) : Mathf.Lerp(strafeSpeedMul, backSpeedMul, -fwd);
            if (fullWeight > 0.5f) dirMul *= attackMoveMul;
            Vector3 wish = wishDir * moveSpeed * dirMul;
            vel = Vector3.MoveTowards(vel, wish, accel * moveSpeed * dt);
            transform.position += vel * dt;
            bool moving = vel.sqrMagnitude > 0.05f;

            // Hips follow the aim: always within maxTwist; while moving they drift to face it;
            // standing still they turn in place once the twist is large.
            float delta = Mathf.DeltaAngle(bodyYaw, aimYaw);
            if (faceCursor)
            {
                idleTurning = false;
                bodyYaw = Mathf.MoveTowardsAngle(bodyYaw, aimYaw, faceTurnSpeed * dt);
            }
            else if (moving)
            {
                idleTurning = false;
                bodyYaw = Mathf.MoveTowardsAngle(bodyYaw, aimYaw, hipFollowSpeed * dt);
            }
            else
            {
                if (Mathf.Abs(delta) > idleTurnThreshold) idleTurnTimer += dt; else idleTurnTimer = 0;
                if (idleTurnTimer > 0.15f) idleTurning = true;
                if (idleTurning)
                {
                    bodyYaw = Mathf.MoveTowardsAngle(bodyYaw, aimYaw, idleTurnSpeed * dt);
                    if (Mathf.Abs(Mathf.DeltaAngle(bodyYaw, aimYaw)) < 2f) idleTurning = false;
                }
            }
            delta = Mathf.DeltaAngle(bodyYaw, aimYaw);
            if (!faceCursor && Mathf.Abs(delta) > maxTwist) bodyYaw = aimYaw - Mathf.Sign(delta) * maxTwist;
            twist = Mathf.DeltaAngle(bodyYaw, aimYaw);
            transform.rotation = Quaternion.Euler(0, bodyYaw, 0);

            // Lower body: velocity in hip space -> 8-direction blend. Turning in place reads as a slow side step.
            Vector3 local = Quaternion.Euler(0, -bodyYaw, 0) * vel / Mathf.Max(0.01f, moveSpeed);
            Vector2 target = new Vector2(local.x, local.z);
            if (!moving && idleTurning) target = new Vector2(Mathf.Sign(Mathf.DeltaAngle(bodyYaw, aimYaw)) * 0.35f, 0);
            animMove = Vector2.MoveTowards(animMove, target, 6f * dt);
            anim.SetFloat(HMoveX, animMove.x);
            anim.SetFloat(HMoveZ, animMove.y);
            anim.SetFloat(HSpeed, animMove.magnitude);

            // Upper body / attacks.
            if (upperLayer >= 0) anim.SetLayerWeight(upperLayer, upperLayerOn ? 1f : 0f);
            if (atk)
            {
                if (attackMode == AttackMode.FullBody && fullLayer >= 0) { anim.SetTrigger(HAttack); fullWeight = 1f; }
                else anim.SetTrigger(HAttack);
            }
            if (fullLayer >= 0)
            {
                bool fullBusy = attackMode == AttackMode.FullBody &&
                                (!anim.GetCurrentAnimatorStateInfo(fullLayer).IsName("Empty") || anim.IsInTransition(fullLayer));
                fullWeight = Mathf.MoveTowards(fullWeight, fullBusy ? 1f : 0f, 6f * dt);
                anim.SetLayerWeight(fullLayer, fullWeight);
            }
            anim.SetBool("FullAttack", attackMode == AttackMode.FullBody);
        }

        // After the Animator: twist the spine toward the aim, spread over three bones.
        void LateUpdate()
        {
            float t = (twist + stanceYawOffset) * twistWeight;
            // While a full-body attack plays, let the clip own the spine.
            t *= 1f - fullWeight;
            for (int i = 0; i < spine.Length; i++)
            {
                if (!spine[i]) continue;
                spine[i].rotation = Quaternion.AngleAxis(t * spineShare[i], Vector3.up) * spine[i].rotation;
            }
        }
    }
}
