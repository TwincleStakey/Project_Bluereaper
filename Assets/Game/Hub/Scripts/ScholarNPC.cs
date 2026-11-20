// 거점 서생 NPC (11-20, Kido: 책상에 앉아 붓글씨 쓰는 선비, 말 걸면 고개 돌려 쳐다보고, 끝나면 다시 쓰기).
// Model/clips: ArtSource/DeskMan/scripts (rig.py -> anim.py) -> Models/Scholar.fbx, prefab built by ScholarNPCSetup.
// Idle: Sit_Write loops 1-3 times, then sometimes Sit_Dip (inks the brush) or Sit_Pause (strokes his chin, thinks),
// all of which start and end on the same pose so they chain without pops.
// Talk: player within interactRadius presses E (or pad North) -> crossfade to Sit_Attend (sits up, brush raised) and the
// chest/neck/head turn toward the player in LateUpdate and the shared HubDialogue box shows his lines (interact key turns
// pages). Closing the last page, walking past leaveRadius, or EndTalk() -> back to writing.
// Other systems (dialogue UI later) can listen to TalkStarted/TalkEnded or call BeginTalk()/EndTalk().
// Dev (editor only): ArtSource/Hub/scholar_talk.trigger present = talking, absent = writing (for captures).
// ArtSource/Hub/scholar_walk.trigger (deleted on use): the player walks in from the court, stops across the desk,
// talks (BeginTalk stands in for the E key), circles to the south end of the desk, then walks away (talk ends).
using System.Linq;
using RoomRun;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hub
{
    public sealed class ScholarNPC : MonoBehaviour
    {
        public Animator animator;
        public Transform chest, neck, head;
        public float interactRadius = 3.6f;      // from the seat; the 1.7x desk puts a player across it ~2.9 m away
        public float leaveRadius = 6.5f;
        [Range(0, 120)] public float yawLimit = 85f;
        [Range(0, 60)] public float pitchLimit = 25f;
        public float lookInTime = 0.45f, lookOutTime = 0.6f;
        [Tooltip("11-20 Kido: when the talk is over he goes back to writing even if the player stays. Seconds per talk (until a dialogue UI exists); a dialogue system can set holdTalk while its window is open.")]
        public float talkSeconds = 5f;
        [System.NonSerialized] public bool holdTalk;

        [Header("Dialogue (11-20 placeholder lines)")]
        public string speakerName = "서생";
        [TextArea] public string[] lines =
        {
            "어서 오시게. 또 한 넋이 이 마당에 들었구먼.",
            "나는 이 문을 지나는 넋들의 이름을 적는 서생일세. 자네 이름도 이미 여기 적혀 있지.",
            "저 문 너머가 궁금하거든 먼저 살아서 돌아오게나. 기록은 그다음일세.",
        };
        float talkStart;
        public float eyeHeight = 1.45f;                  // where on the player he looks (m above the player's feet)

        public bool Talking { get; private set; }

        // Which way he actually sits: from the hips to the paper on his desk (the prefab root's own axes are not trusted:
        // in Play the root came out facing +Z while the body faced the desk on +X).
        Transform paper;
        Quaternion Facing
        {
            get
            {
                if (!paper) paper = GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Desk_Paper");
                var f = paper ? paper.position - transform.position : transform.forward; f.y = 0;
                return f.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(f.normalized, Vector3.up) : transform.rotation;
            }
        }
        public event System.Action<ScholarNPC> TalkStarted, TalkEnded;

        const string Write = "Sit_Write", Dip = "Sit_Dip", Pause = "Sit_Pause", Attend = "Sit_Attend";
        int writeLoopsLeft;
        float look, yawS, pitchS;
        Transform target;

        void Start()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            animator.Play(Write, 0, Random.value);
            writeLoopsLeft = Random.Range(1, 3);
        }

        Transform Player()
        {
            if (target) return target;
            var run = RunDirector.Instance;
            if (run && run.Player) target = run.Player.transform;
            if (!target) { var p = FindFirstObjectByType<PlayerController>(); if (p) target = p.transform; }
            return target;
        }

        void Update()
        {
            var p = Player();
            float dist = 999f;
            if (p) { var d = p.position - transform.position; d.y = 0; dist = d.magnitude; }

            // the dialogue box owns the interact key while it is open (it turns the pages)
            bool pressed = !HubDialogue.IsOpen && HubInput.InteractPressed();
#if UNITY_EDITOR
            var trig = System.IO.File.Exists(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource", "Hub", "scholar_talk.trigger"));
            if (trig != devTrig) { devTrig = trig; if (trig) BeginTalk(); else EndTalk(); }
#endif
#if UNITY_EDITOR
            DevWalk(p, dist);
#endif
            if (pressed && dist <= interactRadius && !Talking) BeginTalk();
            else if (Talking && dist > leaveRadius && !devTrig) EndTalk();
            else if (Talking && !holdTalk && !devTrig && HubDialogue.Owner != (object)this && Time.time - talkStart > talkSeconds) EndTalk();

            if (!Talking) IdleCycle();
            look = Mathf.MoveTowards(look, Talking ? 1f : 0f, Time.deltaTime / (Talking ? lookInTime : lookOutTime));
        }
#if UNITY_EDITOR
        bool devTrig;
        int walkStage = -1; float walkT;
        void DevWalk(Transform p, float dist)
        {
            if (!p) return;
            if (walkStage < 0)
            {
                var f = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource", "Hub", "scholar_walk.trigger");
                if (!System.IO.File.Exists(f)) return;
                var pc = p.GetComponent<PlayerController>(); if (!pc) return;
                try { System.IO.File.Delete(f); } catch { }
                walkStage = 0; walkT = Time.time;
                // route in this NPC's frame (+Z = toward the desk/court): start 7 m out, stop 1.9 m across the desk,
                // circle to the desk's end, leave 7 m out the other way
                DevLog($"walk armed: npc {transform.position} fwd {transform.forward} facing {Facing * Vector3.forward} scale {transform.lossyScale.x:0.00} player {p.position}");
                var fq = Facing; float sc = transform.lossyScale.x;
                Vector2 W(float x, float z) { var w = transform.position + fq * new Vector3(x, 0, z) * sc; return new Vector2(w.x, w.z); }
                pc.Source = new Walker(this, new[] { W(0.3f, 5.6f), W(0.2f, 1.7f) }, new[] { W(-1.4f, 1.5f), W(-1.6f, 0.7f) }, new[] { W(-1.8f, 2.6f), W(0.6f, 4.9f) });
            }
        }
        internal static void DevLog(string m)
        {
            var f = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource", "Hub", "scholar_walk_log.txt");
            try { System.IO.File.AppendAllText(f, $"[{System.DateTime.Now:HH:mm:ss}] t={Time.time:0.00} {m}\n"); } catch { }
        }
        sealed class Walker : ICommandSource
        {
            readonly ScholarNPC n; readonly Vector2[][] legs; int leg, k; float wait = -1f; readonly float start; bool placed; float nextLog;
            public Walker(ScholarNPC npc, params Vector2[][] l) { n = npc; legs = l; start = Time.time + 6.8f; }   // after the capture's own teleport and stills, so the clip sees the walk in
            public PlayerCommand Read(PlayerController p, Camera cam)
            {
                if (Time.time < start || leg >= legs.Length) return new PlayerCommand();
                if (wait > 0f) { if (Time.time < wait) return Face(p); wait = -1f; }
                var pos = new Vector2(p.transform.position.x, p.transform.position.z);
                var pts = legs[leg];
                if (Time.time > nextLog) { nextLog = Time.time + 0.5f; DevLog($"leg {leg} k {k} pos {pos} target {pts[Mathf.Min(k, pts.Length - 1)]} talking {n.Talking}"); }
                if (!placed)
                {
                    placed = true;   // CharacterController owns the position: switch it off for the jump
                    var cc = p.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
                    p.transform.position = new Vector3(pts[0].x, p.transform.position.y, pts[0].y);
                    if (cc) cc.enabled = true;
                    k = 1; return Face(p);
                }
                var d = pts[k] - pos;
                if (d.magnitude < 0.35f)
                {
                    k++;
                    if (k >= pts.Length)
                    {
                        k = 0; leg++;
                        if (leg == 1) { HubDialogue.DevAutoAdvance = 0.9f; n.BeginTalk(); wait = Time.time + 3.5f; }   // stands in for pressing E
                        else if (leg == 2) wait = Time.time + 4.5f;   // stays beside him: the talk times out and he goes back to writing
                    }
                    return Face(p);
                }
                var m = d.normalized * Mathf.Clamp01(d.magnitude / 0.6f + 0.3f);
                return new PlayerCommand { move = m, aimDir = new Vector3(m.x, 0f, m.y) };
            }
            PlayerCommand Face(PlayerController p)
            {
                var to = n.transform.position - p.transform.position; to.y = 0;
                return new PlayerCommand { aimDir = to.normalized };
            }
        }
#else
        const bool devTrig = false;
#endif

        void IdleCycle()
        {
            if (animator.IsInTransition(0)) return;
            var st = animator.GetCurrentAnimatorStateInfo(0);
            float frac = st.normalizedTime - Mathf.Floor(st.normalizedTime);
            if (st.IsName(Write))
            {
                // count finished loops; branch near the loop end (the pose there equals the start of Dip/Pause)
                if (frac > 0.97f && st.normalizedTime >= writeLoopsLeft - 0.03f)
                {
                    float r = Random.value;
                    bool hasDip = animator.HasState(0, Animator.StringToHash(Dip)), hasPause = animator.HasState(0, Animator.StringToHash(Pause));
                    if (r < 0.4f && hasDip) animator.CrossFadeInFixedTime(Dip, 0.25f);
                    else if (r < 0.7f && hasPause) animator.CrossFadeInFixedTime(Pause, 0.25f);
                    else { animator.Play(Write, 0, 0f); }
                    writeLoopsLeft = Random.Range(1, 3);
                }
            }
            else if (st.IsName(Dip) || st.IsName(Pause))
            {
                if (st.normalizedTime > 0.97f) animator.CrossFadeInFixedTime(Write, 0.25f);
            }
            else if (st.IsName(Attend))
                animator.CrossFadeInFixedTime(Write, 0.6f);
        }

        public void BeginTalk()
        {
            if (Talking) return;
            Talking = true; talkStart = Time.time;
            animator.CrossFadeInFixedTime(Attend, 0.45f);
            HubDialogue.Show(this, speakerName, lines, EndTalk);   // last page closed -> back to writing
            TalkStarted?.Invoke(this);
        }

        public void EndTalk()
        {
            if (!Talking) return;
            Talking = false;
            HubDialogue.Close(this);
            animator.CrossFadeInFixedTime(Write, 0.6f, 0, 0f);
            writeLoopsLeft = Random.Range(1, 3);
            TalkEnded?.Invoke(this);
        }

        void LateUpdate()
        {
            var p = Player();
            float yaw = 0f, pitch = 0f;
            if (p && head)
            {
                var dir = (p.position + Vector3.up * eyeHeight) - head.position;
                var local = Quaternion.Inverse(Facing) * dir;
                yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -yawLimit, yawLimit);
                float flat = new Vector2(local.x, local.z).magnitude;
                pitch = Mathf.Clamp(-Mathf.Atan2(local.y, flat) * Mathf.Rad2Deg, -pitchLimit, pitchLimit);
            }
            // smooth the aim itself too so a fast-moving player doesn't snap the head
            yawS = Mathf.LerpAngle(yawS, yaw, 1f - Mathf.Exp(-Time.deltaTime * 6f));
            pitchS = Mathf.Lerp(pitchS, pitch, 1f - Mathf.Exp(-Time.deltaTime * 6f));
            float w = look * look * (3f - 2f * look);
            if (w <= 0.001f) return;
            var up = Vector3.up; faceRight = Facing * Vector3.right;
            Turn(chest, 0.35f, 0.0f, w, up);
            Turn(neck, 0.30f, 0.4f, w, up);
            Turn(head, 0.35f, 0.6f, w, up);
        }

        Vector3 faceRight;
        void Turn(Transform b, float yawShare, float pitchShare, float w, Vector3 up)
        {
            if (!b) return;
            var right = Quaternion.AngleAxis(yawS * w, up) * faceRight;
            b.rotation = Quaternion.AngleAxis(pitchS * pitchShare * w, right) * Quaternion.AngleAxis(yawS * yawShare * w, up) * b.rotation;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, interactRadius);
        }
    }
}
