// 거점 대장간 도깨비(고블린) 대장장이 (11-20). Mixamo auto-rigged Tripo goblin, Humanoid.
// Hammer blows = the Mixamo one-hand top-down axe blow ("Standing Melee Attack Downward") played unchanged and
// looping, with a smithing hammer in the hand instead of the axe (Kido 11-20: no IK, no edits to the animation; the
// anvil - prefab child SmithAnvil - is placed where the hammer lands). Sparks at the measured contact time.
// 3-6 blows per set, then sometimes a break clip (wipe sweat / neck stretch / idle) or a pause.
// Talk: HubInput.InteractPressed() within talkRadius (no other dialogue open) -> stops, turns the body toward the
// player (yaw, eased), head looks at the player (humanoid look-at), Mixamo talk clip + HubDialogue lines.
// Ends when the dialogue closes, on walk-off, or after talkMaxTime (never while his dialogue is open).
// The hammer (child "Hammer", origin = grip, +Z = handle toward the head, +Y = face) is laid in the right fist
// every frame from the hand bones, so it follows the clip's own wrist.
// Pivot = between the feet, local +Z = facing. Dev (editor only): ArtSource/Hub/goblin_talk.trigger starts a talk;
// ArtSource/Hub/goblin_cam.trigger records close-ups to ArtSource/Hub/goblin_pose/play_*.png.
using RoomRun;
using UnityEngine;

namespace Hub
{
    [RequireComponent(typeof(Animator))]
    public sealed class GoblinSmithNPC : MonoBehaviour
    {
        [Header("Animator states (base layer)")]
        public string chopState = "Strike";
        public string idleState = "Idle";
        public string talkState = "Talk";
        public string[] breakStates = { "Wipe", "Stretch", "Idle" };

        [Header("Hammer blow (clip normalized times, set by GoblinSmithSetup)")]
        public float chopApex = 0.45f;       // hammer highest
        public float chopContact = 0.545f;   // hammer face at the anvil top
        public Vector3 anvilLocal;            // prefab-local point the hammer face hits at contact (measured)
        public Vector3 sparkLocal;            // prefab-local spark/flash point = SmithAnvil top centre (zero = hammer head)
        public Vector2Int strikesPerSet = new Vector2Int(3, 6);
        [Range(0, 1)] public float breakChance = 0.45f;
        public Vector2 pauseTime = new Vector2(0.6f, 1.3f);
        public float idleBreakTime = 2.4f;

        [Header("Hammer / sparks")]
        public Transform hammer;
        public float gripToHead = 0.4f, headHalf = 0.1f;   // prefab-local
        public ParticleSystem sparks;
        public Light flash;                 // impact flash (decays fast)
        public float flashIntensity = 6f;
        float flashT;
        public AudioSource clang;

        [Header("Talk")]
        public float talkRadius = 3.0f;
        public float talkMaxTime = 8f;
        public float turnSpeed = 220f;
        public float maxBodyTurn = 150f;
        public float lookWeight = 0.9f;
        public float headHeight = 2.4f;     // world metres up the player (player ~2.85 m)
        public string speaker = "도깨비 대장장이";
        [TextArea] public string[] lines =
        {
            "흐흥, 쇠 냄새 맡고 왔나? 내 망치는 쉬는 법이 없지.",
            "도깨비 대장간 물건은 값이 좀 나가. 대신 저승 것도 벤다고.",
            "부러지면 가져와. 고쳐 줄 테니. 공짜는 아니고!",
        };

        public bool Talking { get; private set; }

        Animator anim;
        float baseYaw, yaw, stateEnd, talkStart, lookW;
        int strikesLeft, chopHash, idleHash, talkHash;
        Transform player, rHand, rIndex, rLittle, rMiddle;
        bool devTalk;

        enum Mode { Work, Break, Pause, Talk, Return }
        Mode mode;

        void Awake()
        {
            anim = GetComponent<Animator>();
            anim.applyRootMotion = false;
            baseYaw = transform.localEulerAngles.y;
            chopHash = Animator.StringToHash(chopState);
            idleHash = Animator.StringToHash(idleState);
            talkHash = Animator.StringToHash(talkState);
            rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            rIndex = anim.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            rLittle = anim.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            rMiddle = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            if (!hammer) hammer = transform.Find("Hammer");
        }

        void OnEnable() { StartSet(true); }

        void StartSet(bool snap = false)
        {
            mode = Mode.Work;
            strikesLeft = Random.Range(strikesPerSet.x, strikesPerSet.y + 1);
            lastLoop = -1; sparkLoop = -1;
            if (snap) anim.Play(chopHash, 0, 0f);
            else anim.CrossFadeInFixedTime(chopHash, 0.25f, 0, 0f);
        }
        int lastLoop, sparkLoop;
        void Update()
        {
            var run = RunDirector.Instance;
            player = run && run.Player ? run.Player.transform : null;
            HandleTalkInput();
            float dt = Time.deltaTime;
            var st = anim.GetCurrentAnimatorStateInfo(0);
            bool fading = anim.IsInTransition(0);

            switch (mode)
            {
                case Mode.Work: UpdateBlow(dt); break;
                case Mode.Break:
                    if (!fading && st.shortNameHash != chopHash && (st.normalizedTime >= 0.96f || (stateEnd > 0 && Time.time > stateEnd))) StartSet();
                    break;
                case Mode.Pause:
                    if (Time.time > stateEnd) StartSet();
                    break;
                case Mode.Talk:
                    bool mine = ReferenceEquals(HubDialogue.Owner, this) && HubDialogue.IsOpen;
                    bool far = !devTalk && player && Flat(player.position - transform.position).magnitude > talkRadius + 1.5f;
                    if (!player || far || (!mine && Time.time - talkStart > talkMaxTime)) EndTalk();
                    break;
                case Mode.Return:
                    if (Mathf.Abs(yaw) < 2f) { yaw = 0; StartSet(); }
                    break;
            }

            // body yaw toward the player while talking, back to the anvil otherwise
            float target = 0f;
            if (mode == Mode.Talk && player)
            {
                var parentRot = transform.parent ? transform.parent.rotation : Quaternion.identity;
                var local = Quaternion.Inverse(parentRot) * Flat(player.position - transform.position);
                if (local.sqrMagnitude > 1e-4f)
                    target = Mathf.Clamp(Mathf.DeltaAngle(baseYaw, Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg), -maxBodyTurn, maxBodyTurn);
            }
            yaw = Mathf.MoveTowardsAngle(yaw, target, turnSpeed * dt);
            var e = transform.localEulerAngles; e.y = baseYaw + yaw; transform.localEulerAngles = e;
            lookW = Mathf.MoveTowards(lookW, mode == Mode.Talk && player ? lookWeight : 0f, dt * 2.5f);
#if UNITY_EDITOR
            DevCam();
#endif
        }

        // the Mixamo blow plays unchanged and loops; sparks at the measured contact time, sets counted by loops
        void UpdateBlow(float dt)
        {
            var st = anim.GetCurrentAnimatorStateInfo(0);
            if (st.shortNameHash != chopHash) return;
            int loop = Mathf.FloorToInt(st.normalizedTime);
            float frac = st.normalizedTime - loop;
            if (frac >= chopContact && sparkLoop != loop) { sparkLoop = loop; Spark(); }
            if (loop != lastLoop)
            {
                if (lastLoop >= 0 && --strikesLeft <= 0) { EndSet(); return; }
                lastLoop = loop;
            }
        }
        void OnAnimatorIK(int layer)
        {
            if (lookW > 0.001f && player)
            {
                anim.SetLookAtWeight(lookW, 0.15f, 0.85f, 1f, 0.6f);
                anim.SetLookAtPosition(player.position + Vector3.up * headHeight);
            }
            else anim.SetLookAtWeight(0f);
        }

        void LateUpdate()
        {
            if (flash) { flashT = Mathf.MoveTowards(flashT, 0f, Time.deltaTime * 7f); flash.intensity = flashIntensity * flashT * flashT; flash.enabled = flashT > 0.001f; }
            // the hammer sits in the right fist: Z = across the palm toward the index (head side), Y = knuckles
            if (!hammer || !rHand || !rIndex || !rLittle || !rMiddle) return;
            var across = rIndex.position - rLittle.position;
            var knuck = rMiddle.position - rHand.position;
            if (across.sqrMagnitude < 1e-8f || knuck.sqrMagnitude < 1e-8f) return;
            hammer.SetPositionAndRotation(Vector3.Lerp(rHand.position, rMiddle.position, 0.72f), Quaternion.LookRotation(across.normalized, knuck.normalized));
        }

        void EndSet()
        {
            if (breakStates != null && breakStates.Length > 0 && Random.value < breakChance)
            {
                var s = breakStates[Random.Range(0, breakStates.Length)];
                anim.CrossFadeInFixedTime(s, 0.35f, 0, 0f);
                mode = Mode.Break;
                stateEnd = s == idleState ? Time.time + idleBreakTime : -1f;
            }
            else
            {
                mode = Mode.Pause;
                stateEnd = Time.time + Random.Range(pauseTime.x, pauseTime.y);
            }
        }

        void HandleTalkInput()
        {
#if UNITY_EDITOR
            if (Time.frameCount % 15 == 0)
            {
                var trig = DevPath("goblin_talk.trigger");
                if (System.IO.File.Exists(trig)) { try { System.IO.File.Delete(trig); } catch { } if (!Talking) { BeginTalk(); devTalk = true; } return; }
            }
#endif
            if (Talking || HubDialogue.IsOpen || !player || !HubInput.InteractPressed()) return;
            if (Flat(player.position - transform.position).magnitude <= talkRadius) BeginTalk();
        }

        public void BeginTalk()
        {
            Talking = true;
            mode = Mode.Talk;
            talkStart = Time.time;
            anim.CrossFadeInFixedTime(talkHash, 0.3f, 0, 0f);
            HubDialogue.Show(this, speaker, lines, EndTalk);
        }

        public void EndTalk()
        {
            if (!Talking) return;
            Talking = false; devTalk = false;
            mode = Mode.Return;
            anim.CrossFadeInFixedTime(idleHash, 0.3f, 0, 0f);
            HubDialogue.Close(this);
        }

        void Spark()
        {
            if (sparks && hammer)
            {
                sparks.transform.position = sparkLocal != Vector3.zero ? transform.TransformPoint(sparkLocal) : hammer.TransformPoint(new Vector3(0f, headHalf, gripToHead));
                sparks.Play(true);
            }
            flashT = 1f;
            if (clang && clang.clip) { clang.pitch = Random.Range(0.92f, 1.08f); clang.Play(); }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

#if UNITY_EDITOR
        static string DevPath(string f) => System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource", "Hub", f);
        Camera devCam; RenderTexture devRt; int devFrame = -1; float devNext;
        void DevCam()
        {
            if (devFrame < 0)
            {
                if (Time.frameCount % 20 != 0) return;
                var trig = DevPath("goblin_cam.trigger");
                if (!System.IO.File.Exists(trig)) return;
                try { System.IO.File.Delete(trig); } catch { }
                var go = new GameObject("GoblinDevCam"); devCam = go.AddComponent<Camera>(); devCam.enabled = false; devCam.fieldOfView = 30;
                devRt = new RenderTexture(512, 512, 24); devCam.targetTexture = devRt;
                devFrame = 0; devNext = Time.time;
                System.IO.Directory.CreateDirectory(DevPath("goblin_pose"));
            }
            if (Time.time < devNext) return;
            devNext = Time.time + 0.1f;
            bool side = devFrame < 40;
            var c = transform.position + Vector3.up * 1.1f + transform.forward * 0.5f;
            devCam.transform.position = side ? c + transform.right * 5.5f + Vector3.up * 0.3f
                                             : c + Quaternion.Euler(49.5f, 0, 0) * Vector3.back * 9f;
            devCam.transform.LookAt(c);
            devCam.Render();
            RenderTexture.active = devRt; var tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); tex.Apply(); RenderTexture.active = null;
            System.IO.File.WriteAllBytes(DevPath($"goblin_pose/play_{devFrame:000}.png"), tex.EncodeToPNG());
            Destroy(tex);
            if (++devFrame >= 110) { Destroy(devCam.gameObject); devRt.Release(); devFrame = -1; System.IO.File.WriteAllText(DevPath("goblin_pose/play_done.txt"), "ok"); }
        }
#endif
    }
}
