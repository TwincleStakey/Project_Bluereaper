// 이기어검 (flying-sword command) attack test (11-18, Kido via coordinator). The hero keeps the katana hilt in the RIGHT hand
// and directs the blade with the LEFT hand's two-finger sword sign (검결), like Yondu's arrow: on the thrust the blade's
// cut segments (the same 9 slices of the hero's own sword that the 사복검 test cuts, WhipCombat.BladeFrac / Plates) leave
// the hilt one after another, fly curved homing arcs to the target, hit, loop back and rejoin the hilt.
// Body: clip EG_Command (Assets/Game/GSHero/Art/EG_Command.fbx, keyed in Blender by ArtSource/Eogeom/blender/eg_anim.py
// from the references in ArtSource/Eogeom/ref): f0 ready, f3 검결 beside the face, f6 thrust (left arm straight at the target,
// left shoulder forward), f11 hold, f17 검결 back at the chest (recall), f24 ready. Evaluated on WhipBody's hidden proxy and
// copied onto the real skeleton after the Animator; the code only picks the clip time (no code-made poses).
// The rig has no finger bones, so a short soul-blue glow along the left hand stands in for the two extended fingers.
// Toggle: dev panel 플레이어 > 이기어검 (테스트) = PlayerPrefs RR.Eogeom (default 0). It wins over the 사복검 toggle while on.
using System.Collections.Generic;
using UnityEngine;

namespace RoomRun
{
    [DefaultExecutionOrder(260)]   // after SplitBody (50), LSPoseFix (100), ArmStretch (150), eye flames (200)
    public sealed class EogeomCombat : MonoBehaviour
    {
        public const string Key = "RR.Eogeom";
        public static bool UseEogeom => PlayerPrefs.GetInt(Key, 0) != 0;
        public static string ClipPath = "Assets/Game/GSHero/Art/EG_Command.fbx";
        // v9 (Kido: "칼 어깨에 한번 올려봐 대검 몸통 머리 뚫지않게"): the same clip keyed with the katana resting on the right shoulder
        // at rest / walk / run (f0, the 검결 raise and from the recall on); ShoulderRest = false gives v8
        public static bool ShoulderRest = true;
        public static string ShoulderClipPath = "Assets/Game/GSHero/Art/EG_CommandShoulder.fbx";
        // v12 (Kido: base stance FF14 Dark Knight shoulder carry + left-hand command from Mixamo): the sword rests on the right
        // shoulder in every frame (EG_CommandFF14.fbx); the LEFT arm of the attack comes from Mixamo "Standing 1H Magic Attack 01"
        // (the cast, f7 -> f16 squeezed into the 0.17 s thrust) and, while the attack is held, a loop of "Standing 2H Magic Attack 03"
        // (f34-f100, the sustained push, only its left arm). The arm is then turned so the shoulder->hand line points at the aim.
        public static bool FF14Base = true;
        public static string FF14ClipPath = "Assets/Game/GSHero/Art/EG_CommandFF14.fbx";
        // v13 (Kido: "어깨에 칼 올리라니깐 반대로 들고 있으면 ... 손은 왜저렇게 파들거려 손가락도 이상한대"):
        //  - EG_CommandFF14b.fbx: blade ON the right shoulder going up and back ~30 deg (v12 lay flat behind the shoulder)
        //  - the hold is one still pose (no Mixamo 2H loop, no clip-time wobble, the arm-aim turn eased) + a slow breath
        //  - the left wrist keeps our clip's straight fist (Mixamo's open-hand wrist bent the finger glow), fingers along the forearm
        public static bool V13 = true;
        public static string FF14bClipPath = "Assets/Game/GSHero/Art/EG_CommandFF14b.fbx";
        // v14 (Kido: "래퍼런스랑 너무 다르자나"; ref ArtSource/Eogeom/ref/shoulder/ref_ff14_drk_idle_xivmodarchive.jpg): FF14 DRK
        // weapon-drawn idle copied: fist beside the right ear, elbow out, blade over the shoulder behind the neck running back
        // almost level (EG_CommandFF14c.fbx). The v13 hold / finger fixes stay on.
        public static bool V14 = true;
        // v15 (Kido: "저 손가락이랑 너무 이상한대 손에 제대로 저 눈이랑 같은 색 광원으로 한번 감아봐"): no finger cards; the left
        // fist + wrist are wrapped in a copy of the hero's own EyeFlame particle system (M_GSEyeFlame, HDR 0.55/1.5/3, same look and
        // colour as the eyes) re-shaped to a small sphere round the fist, world-space so it streams off the hand when it moves
        // (replaces the fingertip Cosmos trail), plus a small point light in the eye blue that lights the forearm and the ground.
        public static bool V15 = true;
        // v16 (Kido's wuxia frame: fully side-on, waist back, chin up, left arm dead straight at the target, blades streaming out
        // of the hand in rows): EG_CommandFF14d.fbx (turn 90, half in the pelvis, lean back 20, chin up); the left arm is our clip
        // (no Mixamo cast), only the aim correction stays; the segments leave from the hand in parallel lanes, not a wide fan.
        public static bool V16 = false;   // 11-19 Kido: v15 was better -> v16 off by default (token egv16=1)
        public static string FF14dClipPath = "Assets/Game/GSHero/Art/EG_CommandFF14d.fbx";
        public static float LaneGap = 0.22f, LaneRise = 0.16f;
        public static float WrapRadius = 0.05f, WrapRate = 340f, WrapSpeed = 0.14f, WrapSize = 1.5f;   // v15b: the eye-sized flame was too small on the fist
        public static float HandLightIntensity = 2.2f, HandLightRange = 1.6f;
        public static Color HandLightColor = new Color(0.30f, 0.62f, 1f);   // eye flame body (0.55,1.5,3) normalised
        GameObject handWrap; ParticleSystem[] wrapPs; float[] wrapRate; Light handLight; bool wrapTried, wrapLogged;
        public static string FF14cClipPath = "Assets/Game/GSHero/Art/EG_CommandFF14c.fbx";
        public static float BreathDeg = 0.7f, BreathHz = 0.6f;   // ~0.6 cm at the fist
        Quaternion armFix = Quaternion.identity; bool armFixSet;
        public static bool MxLayer = true;
        public static float MxArmLift = 0.14f;   // the held arm points at the aim, this much up (tan ~8 deg)
        public static string MxCastPath = "Assets/Game/RoomRun/ThirdParty/MixamoEogeom/EG_MX_Cast1H.fbx";
        public static string MxHoldPath = "Assets/Game/RoomRun/ThirdParty/MixamoEogeom/EG_MX_Hold2H.fbx";
        public static float MxCastFrom = 7f / 30f, MxCastPeak = 16f / 30f, MxHoldFrom = 34f / 30f, MxHoldTo = 100f / 30f;
        AnimationClip mxCast, mxHold; float mxW, mxHoldW, mxHoldT;

        // ---- timing (seconds; clip keys at 30 fps: 검결 f3 = .10, thrust f6 = .20, recall f17 = .57, end f24 = .80) ----
        public static float LaunchAt = 5f / 30f; // thrust frame: the first (tip) segment leaves (v3: clip thrust f6 -> f5)
        public static float Stagger = 0.014f;    // between segments, tip first (v2 .018 -> .014)
        public static float Range = 11f, ConeDeg = 75f;
        public static float OutBase = 0.15f, BackBase = 0.18f;   // flight times (+ a little per metre) (v2: .17/.21 -> .15/.18)
        // v2 (gap 2): the pointing hand HOLDS (clip f11) while the segments are out; once the last one is well on its way home
        // (ReleaseAt of its return flight) the clip runs on at RecallSpeed so the 검결 comes back to the chest as they rejoin
        public static float HoldAt = 11f / 30f, ReleaseAt = 0.55f, RecallSpeed = 1.5f;
        public static bool HoldUntilHome = true;
        // v8 (Kido: "캐리어 인터셉터처럼 각자 누르고있는동안 적들 계속 공격하게해"): while attack is HELD the pieces stay out; after each
        // hit a piece loops out and dives on its own next target (spread over the enemies near the cursor), until release.
        public static bool Interceptor = true;
        public static int EnemyHitDamage = 1;
        public static float EnemyHitInterval = 0.08f;   // one enemy takes at most one piece hit per this many seconds (damage tuning)
        public static int MaxPiecesPerEnemy = 3;        // spreading: a target that already has this many pieces diving is skipped
        public static float ReDiveDelayMin = 0.03f, ReDiveDelayMax = 0.12f;
        // v2 (gap 3): thin additive rim on the flying slices (soul blue body, white edge) so the dark iron reads at zoom 1.6
        public static bool Rim = true;
        // v3: a very short thin blue streak from the fist toward the target on the thrust frame (no light)
        public static bool Streak = true;
        // v5 (Kido: "칼조각 훨씬 커도 될거같에 한 2배키워"): flying pieces grow to FlyScale right after leaving the hilt and shrink
        // back as they rejoin (the rejoined sword is the original size)
        public static float FlyScale = 2f;
        // v5 trail styles: 0 = v4 thin ribbon, 1 = A: our own long trail (0.35 s, wide, afterimage copies of the piece, sparks),
        // 2 = B: asset trail (Vefects Trails VFX URP prefab on each piece, TrailPrefabPath) + enchant glow
        // v6 (Kido picked "Vefects 우주"): default = the Vefects Cosmos trail recoloured to the 어검 blue-white
        public static int TrailStyle = 2;
        public static string TrailPrefabPath = "Assets/Vefects/Trails/VFX/Particles/VFX_Trail_Cosmos.prefab";
        public static bool Recolor = true;   // false = the pack's own purple/gold (before/after captures)
        public static Color RecolorA = new Color(0.78f, 1.0f, 1.55f, 1f), RecolorB = new Color(0.22f, 0.55f, 1.0f, 1f);   // v6b: more cyan-white, darker low end (v6a read violet and blocky)
        static readonly Dictionary<Material, Material> recolored = new Dictionary<Material, Material>();
        static Material Recolored(Material src)
        {
            if (!src || !src.HasProperty("_Color_01") || !src.HasProperty("_Color_02")) return src;
            if (recolored.TryGetValue(src, out var m) && m) return m;
            m = new Material(src) { name = src.name + "_EogeomBlue" };
            // the pack's colour = lerp(_Color_01, _Color_02, panning noise): both ends moved to our blue-white, alpha kept
            var a = src.GetColor("_Color_01"); var b = src.GetColor("_Color_02");
            m.SetColor("_Color_01", new Color(RecolorA.r, RecolorA.g, RecolorA.b, a.a));
            m.SetColor("_Color_02", new Color(RecolorB.r, RecolorB.g, RecolorB.b, b.a));
            recolored[src] = m;
            return m;
        }
        public static float AssetTrailLife = 0.45f;   // the Vefects trails live ~1 s; scaled so they follow the piece ~0.45 s
        public static float GhostEvery = 0.03f, GhostLife = 0.16f;
        public static int GhostCount = 4;
        public static float StreakLife = 0.12f, StreakLen = 2.6f;
        public static bool Glow = true;          // 검결 finger glow (no finger bones)
        public static float LegShare = 1f;

        public bool Attacking => kind != 0;
        public Vector3 AttackAim => aim;
        // v4 (Kido: "wasd 상체 따로 움직이면서"): while moving the legs keep the WASD locomotion (SplitBody.LegDir picks run /
        // strafe / backpedal) and only the upper body plays EG_Command, turned so the chest twist is measured from the cursor aim
        public bool BodyToAim => kind != 0 && moveW < 0.5f;
        public static float MoveAttackScale = 0.8f;
        public float MoveScale => kind == 0 ? 1f : MoveAttackScale;
        public static bool UpperAim = true;     // false = v3 (upper body follows the hips while moving)
        // v8 direction rule (Kido: "이방향규칙 정해놓고 적용해", V Rising style; ArtSource/Eogeom/out/v8/notes.md):
        // 1 no root snap or flip: the body turns continuously; 2 legs = locomotion (MoveX/MoveZ blend in body space), upper body = the
        // attack layer; 3+4 the twist toward the cursor is shared: the pelvis (whole lower body) takes most of it, leaning at most
        // PelvisLead toward the run direction, the chest turns the rest back to the cursor, never more than ChestMax;
        // 5 the 2D blend tree picks run / strafe / backpedal from the move direction against the pelvis (continuous blend, plus
        // hysteresis on the lean side when running straight away from the cursor); 6 running = straighter spine (RunSpineShare);
        // 7 the blade-vs-legs distance stays checked every frame; 8 the attack plays from the upper body whatever the legs do.
        public static bool DirRule = true;
        public static float PelvisLead = 30f, ChestMax = 45f, RunSpineShare = 0.65f;
        Vector3 cursorAim = Vector3.forward; float leadSign = 1f;
        public Vector3 CursorAim => cursorAim;
        public Vector3 PelvisDir(Vector3 moveFlat)
        {
            var a = kind != 0 ? aim : cursorAim;
            moveFlat.y = 0f;
            if (moveFlat.sqrMagnitude < 1e-4f) return a;
            float ang = Vector3.SignedAngle(a, moveFlat.normalized, Vector3.up);
            if (Mathf.Abs(ang) < 150f) leadSign = Mathf.Sign(ang);          // straight away from the cursor: keep the last side
            float off = Mathf.Abs(ang) < 150f ? Mathf.Clamp(ang, -PelvisLead, PelvisLead) : leadSign * PelvisLead * 0.5f;
            return Quaternion.AngleAxis(off, Vector3.up) * a;
        }
        public float MoveW => moveW;
        public float UpperYaw => upperYaw;
        public bool AllHome { get { for (int k = 0; k < plates; k++) if (st[k] != 0) return false; return true; } }
        // capture log (EogeomCapture)
        public float T => t;
        public int Kind => kind;
        public float ClipT => ct;
        public int Flying { get { int n = 0; for (int k = 0; k < plates; k++) if (st[k] != 0) n++; return n; } }

        PlayerController pc; Animator anim;
        bool holding; float holdT;
        readonly Dictionary<Health, float> lastHit = new Dictionary<Health, float>();
        float[] wait;   // v8: a short pause after a hit before the next dive (staggers the pieces)
        readonly List<Health> cand = new List<Health>(64);
        Transform rHand, lHand, lFore, sword;
        WhipBody body; AnimationClip clip, clipIdle;
        float bodyW, idleW, lastCt, ct, runGripW;
        int kind; float t; bool movingAtk; Vector3 aim = Vector3.forward;
        float moveW, upperYaw;
        Transform rUpperB, lUpperB, spineB;
        Health target; Vector3 targetPt;

        // real-sword cut (same split as WhipCombat.CutSword)
        bool swordCut, swordCutTried;
        Transform swordT; Vector3 cutA, cutF, cutS; float cutScale, bladeLenLocal; Vector3 bladeBaseLocal;
        Vector3[] cvPos, cvNrm; int[] cvPlate; Vector3[] cvOut, cvOutN;
        Mesh blade; GameObject bladeGo;
        int plates;

        // segments
        int[] st;                 // 0 home (on the hilt), 1 out, 2 back
        float[] pt, dur, sgn, amp;
        Vector3[] b0, b1, b2, b3; // current path (cubic Bezier); b3 of an out path follows the target
        Vector3[] pos, dir, nrm;
        Health[] tgt;
        float[] flyS;             // v5 current scale of each piece
        struct Ghost { public Vector3 p, d, n; public float s, time; }
        readonly List<Ghost>[] ghosts = new List<Ghost>[16];
        float[] ghostNext;
        Mesh ghostMesh; GameObject ghostGo; Material ghostMat;
        struct Spark { public Vector3 p, v; public float t, life, size; }
        readonly List<Spark> sparks = new List<Spark>(256);
        float[] sparkNext;
        GameObject[] assetTrail;
        bool[] launched;
        readonly List<Vector3>[] trailP = new List<Vector3>[16];
        readonly List<float>[] trailT = new List<float>[16];
        Mesh ribbon; GameObject ribbonGo; Material ribbonMat;
        Mesh glow; GameObject glowGo; Material glowMat;
        Mesh rim; GameObject rimGo; Material rimMat;
        int[][] plateTris; Vector3[] rimV; Vector2[] rimUv; Color[] rimC; readonly List<int> rimT = new List<int>(8192);
        readonly float[] rimW = new float[16];

        public void Setup(PlayerController owner, Animator animator)
        {
            pc = owner; anim = animator;
            rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            lFore = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            rUpperB = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            lUpperB = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            spineB = anim.GetBoneTransform(HumanBodyBones.Spine);
            plates = Mathf.Clamp(WhipCombat.Plates, 1, 16);
            st = new int[plates]; pt = new float[plates]; dur = new float[plates]; sgn = new float[plates]; amp = new float[plates];
            b0 = new Vector3[plates]; b1 = new Vector3[plates]; b2 = new Vector3[plates]; b3 = new Vector3[plates];
            pos = new Vector3[plates]; dir = new Vector3[plates]; nrm = new Vector3[plates];
            tgt = new Health[plates]; launched = new bool[plates];
            wait = new float[plates];
            flyS = new float[plates]; ghostNext = new float[plates]; sparkNext = new float[plates]; assetTrail = new GameObject[plates];
            for (int k = 0; k < plates; k++) { flyS[k] = 1f; ghosts[k] = new List<Ghost>(8); }
            for (int k = 0; k < plates; k++) { trailP[k] = new List<Vector3>(32); trailT[k] = new List<float>(32); }
            BuildDrawing();
            body = new WhipBody(anim);
            var cp = ShoulderRest ? (FF14Base ? (V16 ? FF14dClipPath : V14 ? FF14cClipPath : V13 ? FF14bClipPath : FF14ClipPath) : ShoulderClipPath) : ClipPath;
            mxCast = WhipBody.LoadClip(MxCastPath, null); mxHold = WhipBody.LoadClip(MxHoldPath, null);
            if (MxLayer) Debug.Log($"[RoomRun] eogeom: Mixamo left-arm layer cast={(mxCast ? mxCast.name : "missing")} hold={(mxHold ? mxHold.name : "missing")}");
            clip = WhipBody.LoadClip(cp, "EG_Command") ?? WhipBody.LoadClip(cp, null);
            clipIdle = clip;
            if (!body.Ready || !clip) Debug.LogWarning($"[RoomRun] eogeom: body clip missing (ready={body.Ready} clip={clip}) at {ClipPath}");
            else Debug.Log($"[RoomRun] eogeom: body clip {clip.name} {clip.length:0.00}s");
        }

        void BuildDrawing()
        {
            blade = new Mesh { name = "EogeomBlade" }; blade.MarkDynamic();
            bladeGo = new GameObject("EogeomBlade");
            bladeGo.AddComponent<MeshFilter>().sharedMesh = blade;
            bladeGo.AddComponent<MeshRenderer>();
            var trail = Shader.Find("RoomRun/BladeTrail");
            // soul blue, like the hero's eye flames / greatsword trail / 사복검 ribbon
            ribbonMat = new Material(trail) { name = "RR_EogeomTrail" };
            ribbonMat.SetColor("_Color", new Color(0.12f, 0.42f, 1.25f, 1f));
            ribbonMat.SetColor("_Edge", new Color(0.8f, 1.1f, 1.45f, 1f));
            ribbonMat.SetFloat("_BodyPow", 0.9f); ribbonMat.SetFloat("_EdgeWidth", 0.25f);
            ribbon = new Mesh { name = "EogeomTrail" }; ribbon.MarkDynamic();
            ribbonGo = new GameObject("EogeomTrail");
            ribbonGo.AddComponent<MeshFilter>().sharedMesh = ribbon;
            var rr = ribbonGo.AddComponent<MeshRenderer>(); rr.sharedMaterial = ribbonMat;
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glowMat = new Material(ribbonMat) { name = "RR_EogeomSign" };
            glowMat.SetColor("_Color", new Color(0.5f, 1.2f, 3.2f, 1f));   // v5: brighter (was .25,.65,1.6)
            glowMat.SetColor("_Edge", new Color(2.6f, 3.0f, 3.4f, 1f));
            glow = new Mesh { name = "EogeomSign" }; glow.MarkDynamic();
            glowGo = new GameObject("EogeomSign");
            glowGo.AddComponent<MeshFilter>().sharedMesh = glow;
            var gr = glowGo.AddComponent<MeshRenderer>(); gr.sharedMaterial = glowMat;
            gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // rim: uv.y = 1 - |n.v| (grazing faces = the silhouette) -> faint blue body, bright white edge
            rimMat = new Material(trail) { name = "RR_EogeomRim" };
            rimMat.SetColor("_Color", new Color(0.5f, 1.3f, 3.6f, 1f));
            rimMat.SetColor("_Edge", new Color(3.2f, 3.6f, 4.0f, 1f));
            rimMat.SetFloat("_BodyPow", 1.2f); rimMat.SetFloat("_EdgeWidth", 0.5f);
            rim = new Mesh { name = "EogeomRim" }; rim.MarkDynamic();
            rim.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            rimGo = new GameObject("EogeomRim");
            rimGo.AddComponent<MeshFilter>().sharedMesh = rim;
            var rmr = rimGo.AddComponent<MeshRenderer>(); rmr.sharedMaterial = rimMat;
            rmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // v5 A: afterimage copies of the flying pieces (additive, same shader)
            ghostMat = new Material(trail) { name = "RR_EogeomGhost" };
            ghostMat.SetColor("_Color", new Color(0.3f, 0.8f, 2.4f, 1f));
            ghostMat.SetColor("_Edge", new Color(1.6f, 2.0f, 2.6f, 1f));
            ghostMat.SetFloat("_BodyPow", 0.8f); ghostMat.SetFloat("_EdgeWidth", 0.45f);
            ghostMesh = new Mesh { name = "EogeomGhost" }; ghostMesh.MarkDynamic();
            ghostMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            ghostGo = new GameObject("EogeomGhost");
            ghostGo.AddComponent<MeshFilter>().sharedMesh = ghostMesh;
            var gmr = ghostGo.AddComponent<MeshRenderer>(); gmr.sharedMaterial = ghostMat;
            gmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void OnEnable() { if (bladeGo) bladeGo.SetActive(true); if (ribbonGo) ribbonGo.SetActive(true); if (glowGo) glowGo.SetActive(true); if (rimGo) rimGo.SetActive(true); if (ghostGo) ghostGo.SetActive(true); if (tipTrail) tipTrail.SetActive(true); }

        void OnDisable()
        {
            if (sword) sword.gameObject.SetActive(true);
            if (bladeGo) bladeGo.SetActive(false);
            if (ribbonGo) ribbonGo.SetActive(false);
            if (glowGo) glowGo.SetActive(false);
            if (rimGo) rimGo.SetActive(false);
            if (ghostGo) ghostGo.SetActive(false);
            if (tipTrail) { tipTrail.SetActive(false); tipTrailOn = false; }
            if (assetTrail != null) foreach (var a in assetTrail) if (a) a.SetActive(false);
            sparks.Clear();
            kind = 0;
            if (st != null) for (int k = 0; k < plates; k++) { st[k] = 0; trailP[k].Clear(); trailT[k].Clear(); }
        }

        void OnDestroy()
        {
            if (sword) sword.gameObject.SetActive(true);
            if (bladeGo) Destroy(bladeGo);
            if (ribbonGo) Destroy(ribbonGo);
            if (glowGo) Destroy(glowGo);
            if (ribbonMat) Destroy(ribbonMat);
            if (glowMat) Destroy(glowMat);
            if (rimGo) Destroy(rimGo);
            if (rimMat) Destroy(rimMat);
            if (ghostGo) Destroy(ghostGo);
            if (tipTrail) Destroy(tipTrail);
            if (handWrap) Destroy(handWrap);
            if (handLight) Destroy(handLight.gameObject);
            if (ghostMat) Destroy(ghostMat);
            if (assetTrail != null) foreach (var a in assetTrail) if (a) Destroy(a);
            body?.Dispose();
        }

        public void Cancel() { kind = 0; }

        /// Called by PlayerController every frame while the mode is on (instead of the greatsword's attack input).
        public void Tick(float dt, bool fire, bool canAct, Vector3 aimDir)
        {
            { var ca = aimDir; ca.y = 0f; if (ca.sqrMagnitude > 1e-4f) cursorAim = ca.normalized; }
            if (!canAct) { kind = 0; holding = false; return; }
            if (kind == 0)
            {
                if (fire && AllHome) StartAttack(aimDir);
            }
            else
            {
                t += dt;
                // v8: holding = attack still pressed since this command started; the aim follows the cursor while held
                if (Interceptor)
                {
                    if (holding && !fire) { holding = false; RecallAll(); }
                    if (holding)
                    {
                        holdT += dt;
                        var ad = aimDir; ad.y = 0f;
                        if (ad.sqrMagnitude > 1e-4f) aim = Vector3.Slerp(aim, ad.normalized, Mathf.Min(1f, dt * 12f)).normalized;
                    }
                }
                // the segments leave one after another from the thrust frame, tip first
                for (int i = 0; i < plates; i++)
                {
                    int k = plates - 1 - i;
                    if (!launched[k] && t >= LaunchAt + i * Stagger) { launched[k] = true; if (st[k] == 0) Launch(k); }
                }
                float len = clip ? clip.length : 0.8f;
                // clip time: straight up to the hold frame, held there until the volley is coming home, then the recall
                if (!HoldUntilHome) ct = t;
                else if (ct < HoldAt) ct = Mathf.Min(HoldAt, ct + dt);
                else if (holding) ct = V13 ? HoldAt : HoldAt - 0.05f * (0.5f + 0.5f * Mathf.Sin(holdT * 7f));   // small directing motion between f9.5 and f11 while held
                else if (Released()) ct = Mathf.Max(ct, HoldAt) + dt * RecallSpeed;
                if (fire && !holding && ct >= len * 0.8f && AllHome) StartAttack(aimDir);
                else if (ct >= len) kind = 0;
            }
        }

        void StartAttack(Vector3 aimDir)
        {
            kind = 1; t = 0f; ct = 0f; holding = Interceptor; holdT = 0f;
            var v = pc.Velocity; v.y = 0f; movingAtk = v.magnitude > 0.6f;
            aim = aimDir; aim.y = 0f; if (aim.sqrMagnitude < 1e-4f) aim = pc.transform.forward; aim.Normalize();
            for (int k = 0; k < plates; k++) launched[k] = false;
            target = FindTarget(out targetPt);
            if (target) { var d = target.transform.position - Origin; d.y = 0f; if (d.sqrMagnitude > 0.01f) aim = d.normalized; }
        }

        // every segment launched and either home or far enough into its return flight
        bool Released()
        {
            if (holding) return false;
            for (int k = 0; k < plates; k++)
            {
                if (!launched[k]) return false;
                if (st[k] == 1) return false;
                if (st[k] == 2 && pt[k] < dur[k] * ReleaseAt) return false;
            }
            return true;
        }

        Vector3 Origin => pc.transform.position;
        readonly Collider[] overlap = new Collider[128];

        Health FindTarget(out Vector3 pt)
        {
            Health best = null; float bs = float.MaxValue;
            float cosMax = Mathf.Cos(ConeDeg * Mathf.Deg2Rad);
            int n = Physics.OverlapSphereNonAlloc(Origin + Vector3.up, Range, overlap, 1 << Ground.ActorLayer, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var h = overlap[i].GetComponentInParent<Health>();
                if (!h || h.team == Team.Player || !h.Alive) continue;
                var d = h.transform.position - Origin; d.y = 0f;
                float dist = d.magnitude; if (dist < 0.01f || dist > Range) continue;
                float c = Vector3.Dot(d / dist, aim); if (c < cosMax) continue;
                float score = dist * (1f + (1f - c) * 2f);
                if (score < bs) { bs = score; best = h; }
            }
            pt = best ? best.transform.position + Vector3.up * 1.0f : Origin + aim * 7f + Vector3.up * 1.0f;
            return best;
        }

        // ---------------- segment flight ----------------
        void Launch(int k)
        {
            st[k] = 1; pt[k] = 0f; tgt[k] = target;
            var p0 = HomePos(k);
            var T = target && target.Alive ? target.transform.position + Vector3.up * 1.0f : targetPt;
            var to = T - p0; to.y = 0f; float L = Mathf.Max(1f, to.magnitude); var toN = to / L;
            var S = Vector3.Cross(Vector3.up, toN);
            // alternate sides with varied width: the volley fans out left and right like Yondu's arrow loops
            sgn[k] = (k % 2 == 0) ? 1f : -1f;
            amp[k] = Mathf.Min(2.6f, (0.28f + 0.07f * ((k * 37) % 5)) * L);
            b0[k] = p0;
            b1[k] = p0 + aim * 0.9f + Vector3.up * 1.1f + S * sgn[k] * amp[k] * 0.9f;
            b2[k] = T - toN * L * 0.3f + S * sgn[k] * amp[k] * 0.6f + Vector3.up * 0.8f;
            b3[k] = T;
            dur[k] = OutBase + L * 0.012f;
            if (V16 && lHand)
            {
                // rows out of the hand: 3 lanes side by side x 3 heights, each lane straight-ish to the target
                int lane = k % 3 - 1, row = (k / 3) % 3 - 1;
                var hand = lHand.position;
                var off = S * lane * LaneGap + Vector3.up * row * LaneRise;
                b1[k] = hand + toN * 0.8f + off * 0.6f;
                b2[k] = T - toN * L * 0.35f + off + Vector3.up * 0.3f;
                b3[k] = T;
                dur[k] = OutBase * 0.9f + L * 0.011f;
            }
            pos[k] = p0; dir[k] = SwordDir; nrm[k] = SwordFlat;
            trailP[k].Clear(); trailT[k].Clear();
        }

        void ArriveOut(int k)
        {
            var h = tgt[k];
            if (h && h.Alive && h.team != Team.Player)
            {
                float now = Time.time;
                if (!lastHit.TryGetValue(h, out float lt) || now - lt >= EnemyHitInterval)
                {
                    lastHit[h] = now;
                    if (h.Hit(EnemyHitDamage)) Fx.Burst(pos[k], Fx.Kind.EnemySpark, 4);
                    var rig = RunDirector.Instance ? RunDirector.Instance.Rig : null;
                    if (rig) rig.Shake(0.04f);
                }
            }
            if (Interceptor && holding) { ReDive(k); return; }
            if (VegetationCut.Instance) VegetationCut.Instance.Sector(pos[k] - aim * 0.6f, aim, 1.2f, 25f);
            // back: overshoot past the target, loop round on the far side and come home on the other side of the body
            st[k] = 2; pt[k] = 0f;
            var p = pos[k];
            var away = p - Origin; away.y = 0f; float L = Mathf.Max(1f, away.magnitude); away /= L;
            var S = Vector3.Cross(Vector3.up, away);
            b0[k] = p;
            b1[k] = p + away * 1.4f - S * sgn[k] * 0.9f + Vector3.up * 0.9f;
            b2[k] = HomePos(k) + away * 1.2f - S * sgn[k] * 1.0f + Vector3.up * 1.0f;
            dur[k] = BackBase + L * 0.012f;
        }

        // v8: next target for piece k: enemies near the cursor side (cone around the aim), fewest pieces already on them,
        // not the one it just hit unless it is alone; falls back to a point ahead of the hero
        Health PickTarget(int k, out Vector3 pt)
        {
            cand.Clear();
            float cosMax = Mathf.Cos(ConeDeg * Mathf.Deg2Rad);
            int n = Physics.OverlapSphereNonAlloc(Origin + Vector3.up, Range, overlap, 1 << Ground.ActorLayer, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var h = overlap[i].GetComponentInParent<Health>();
                if (!h || h.team == Team.Player || !h.Alive || cand.Contains(h)) continue;
                var d = h.transform.position - Origin; d.y = 0f; float dist = d.magnitude;
                if (dist < 0.01f || dist > Range || Vector3.Dot(d / dist, aim) < cosMax) continue;
                cand.Add(h);
            }
            Health best = null; float bs = float.MaxValue;
            foreach (var h in cand)
            {
                int on = 0; for (int j = 0; j < plates; j++) if (j != k && st[j] == 1 && tgt[j] == h) on++;
                if (on >= MaxPiecesPerEnemy && cand.Count > 1) continue;
                var d = h.transform.position - Origin; d.y = 0f; float dist = d.magnitude;
                float score = on * 3f + (1f - Vector3.Dot(d / Mathf.Max(dist, 0.01f), aim)) * 4f + dist * 0.15f
                            + (h == tgt[k] && cand.Count > 1 ? 2.5f : 0f) + Random.value * 0.8f;
                if (score < bs) { bs = score; best = h; }
            }
            if (best) { pt = best.transform.position + Vector3.up * 1.0f; return best; }
            // nobody there: circle a point ahead of the hero on the cursor side
            pt = Origin + aim * 5f + Quaternion.AngleAxis(Random.Range(-40f, 40f), Vector3.up) * aim * Random.Range(0f, 1.5f) + Vector3.up * 1.0f;
            return null;
        }

        void ReDive(int k)
        {
            var p = pos[k];
            var nt = PickTarget(k, out var T);
            tgt[k] = nt;
            st[k] = 1; pt[k] = 0f;
            wait[k] = Random.Range(ReDiveDelayMin, ReDiveDelayMax);
            // swing out past the last hit (keeps its momentum), loop round and dive on the next one from a new side
            var fwd = dir[k]; fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = aim; fwd.Normalize();
            var to = T - p; to.y = 0f; float L = Mathf.Max(1f, to.magnitude); var toN = to / L;
            var S = Vector3.Cross(Vector3.up, toN);
            sgn[k] = Random.value < 0.5f ? 1f : -1f;
            b0[k] = p;
            b1[k] = p + fwd * Random.Range(1.2f, 2.0f) + Vector3.up * Random.Range(0.8f, 1.5f);
            b2[k] = T - toN * Mathf.Max(1.2f, L * 0.4f) + S * sgn[k] * Random.Range(0.8f, 1.8f) + Vector3.up * Random.Range(0.7f, 1.3f);
            b3[k] = T;
            dur[k] = OutBase + 0.06f + (L + 2f) * 0.02f;
        }

        // released: every piece still out turns round and flies home from where it is
        void RecallAll()
        {
            for (int k = 0; k < plates; k++)
            {
                if (st[k] != 1) continue;
                ArriveOutNoHit(k);
            }
        }
        void ArriveOutNoHit(int k)
        {
            st[k] = 2; pt[k] = 0f; wait[k] = 0f;
            var p = pos[k];
            var away = p - Origin; away.y = 0f; float L = Mathf.Max(1f, away.magnitude); away /= L;
            var S = Vector3.Cross(Vector3.up, away);
            b0[k] = p;
            b1[k] = p + dir[k] * 0.8f + Vector3.up * 0.8f;
            b2[k] = HomePos(k) + away * 1.2f - S * sgn[k] * 1.0f + Vector3.up * 1.0f;
            dur[k] = BackBase + L * 0.012f;
        }

        static Vector3 Bez(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u)
        {
            float v = 1f - u;
            return v * v * v * a + 3f * v * v * u * b + 3f * v * u * u * c + u * u * u * d;
        }
        static Vector3 BezD(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u)
        {
            float v = 1f - u;
            return 3f * v * v * (b - a) + 6f * v * u * (c - b) + 3f * u * u * (d - c);
        }

        void Fly(float dt)
        {
            float now = Time.time;
            for (int k = 0; k < plates; k++)
            {
                if (st[k] == 0) continue;
                if (wait[k] > 0f) { wait[k] -= dt; pos[k] += dir[k] * dt * 4f; trailP[k].Add(pos[k]); trailT[k].Add(now); continue; }
                pt[k] += dt;
                float u = Mathf.Clamp01(pt[k] / dur[k]);
                if (st[k] == 1)
                {
                    var h = tgt[k];
                    if (h && h.Alive) b3[k] = h.transform.position + Vector3.up * 1.0f;   // homing
                    float e = u * u * (1.6f - 0.6f * u);                                   // leaves the hilt fast, still faster at the end
                    pos[k] = Bez(b0[k], b1[k], b2[k], b3[k], e);
                    var d = BezD(b0[k], b1[k], b2[k], b3[k], Mathf.Max(e, 0.02f));
                    if (d.sqrMagnitude > 1e-6f) dir[k] = d.normalized;
                    var up = Vector3.ProjectOnPlane(Vector3.up, dir[k]);
                    nrm[k] = up.sqrMagnitude > 1e-4f ? Vector3.Slerp(nrm[k], up.normalized, Mathf.Min(1f, dt * 25f)).normalized : nrm[k];
                    if (u >= 1f) ArriveOut(k);
                }
                else
                {
                    var home = HomePos(k);
                    float e = u < 0.5f ? 2f * u * u : 1f - 2f * (1f - u) * (1f - u);
                    var p = Bez(b0[k], b1[k], b2[k], home, e);
                    var d = BezD(b0[k], b1[k], b2[k], home, Mathf.Clamp(e, 0.02f, 0.98f));
                    // last 25 %: settle onto the hilt (position + the sword's own frame)
                    float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, u));
                    pos[k] = Vector3.Lerp(p, home, w);
                    var fd = d.sqrMagnitude > 1e-6f ? d.normalized : dir[k];
                    dir[k] = Vector3.Slerp(fd, SwordDir, w).normalized;
                    var up = Vector3.ProjectOnPlane(Vector3.up, dir[k]);
                    nrm[k] = Vector3.Slerp(up.sqrMagnitude > 1e-4f ? up.normalized : nrm[k], SwordFlat, w).normalized;
                    if (u >= 1f) { st[k] = 0; }
                }
                trailP[k].Add(pos[k]); trailT[k].Add(now);
            }
        }

        // ---------------- pose + draw ----------------
        void LateUpdate()
        {
            if (!pc || !anim || !rHand) return;
            if (!sword)
            {
                sword = rHand.Find("Greatsword");
                if (!sword) foreach (var tr in rHand.GetComponentsInChildren<Transform>(true)) if (tr.name == "Greatsword") { sword = tr; break; }
            }
            if (sword && sword.gameObject.activeSelf) sword.gameObject.SetActive(false);
            if (EogeomCapture.Frames > 0 && !GetComponent<EogeomCapture>()) gameObject.AddComponent<EogeomCapture>();
            float dt = Time.deltaTime;

            // body from the clip (the code only picks the time)
            bool clipBody = body != null && body.Ready && clip;
            if (clipBody)
            {
                if (kind != 0) lastCt = Mathf.Min(clip.length, ct);
                bodyW = Mathf.MoveTowards(bodyW, kind != 0 ? 1f : 0f, dt / (kind != 0 ? 0.04f : 0.12f));
                // moving weight follows the real speed every frame (start/stop mid-attack blends, no snap)
                var mv = pc.Velocity; mv.y = 0f;
                moveW = Mathf.MoveTowards(moveW, mv.magnitude > 0.6f ? 1f : 0f, dt / 0.15f);
                movingAtk = moveW > 0.5f;
                upperYaw = 0f;
                // v6: the standing idle (clip f0) crossfades UNDER the attack as it fades out, so the body never passes through
                // the greatsword carry pose for a few frames (the blade came within 0.10 m of the legs there)
                var iv0 = pc.Velocity; iv0.y = 0f;
                idleW = Mathf.MoveTowards(idleW, kind == 0 && iv0.magnitude < 0.6f ? 1f : 0f, dt / 0.12f);
                if (idleW > 0f && bodyW < 1f)
                {
                    body.Sample(clipIdle, 0f);
                    body.Apply(idleW, idleW, idleW * 0.5f, idleW, idleW);
                }
                // v8b: running with 이기어검 on, the RIGHT arm keeps the ready-pose grip (katana low at the right-rear) instead of the
                // greatsword's two-hand run pose; blending out of an attack into that pose swept the blade past the legs (0.035 m)
                runGripW = Mathf.MoveTowards(runGripW, kind == 0 && iv0.magnitude >= 0.6f ? 1f : 0f, dt / 0.1f);
                if (runGripW > 0f && bodyW < 1f)
                {
                    body.Sample(clipIdle, 0f);
                    body.Apply(0f, 0f, 0f, 0f, runGripW);
                }
                if (bodyW > 0f)
                {
                    body.Sample(clip, lastCt);
                    // legs + hips (step-in, hip drop) only when standing; the spine lean / twist always
                    body.Apply(bodyW * LegShare * (1f - moveW), bodyW * (DirRule ? Mathf.Lerp(1f, RunSpineShare, moveW) : 1f), bodyW, bodyW, bodyW);
                    if (UpperAim && moveW > 0f) AimUpper(bodyW * moveW);
                    if (MxLayer && mxCast) MixamoLeftArm(dt);
                }
                // (standing idle = the clip's ready pose f0, applied above)
            }

            MeasureLegClear();
            if (sword && !swordCut && !swordCutTried) CutSword();
            Fly(dt);
            if (swordCut) { DrawPlates(); DrawRim(); }
            DrawTrails();
            UpdateGhostsAndSparks(dt);
            UpdateAssetTrails();
            DrawSign();
        }

        // v12: left arm from the Mixamo cast / hold, then turned about the vertical so the arm line points at the aim
        void MixamoLeftArm(float dt)
        {
            bool on = kind != 0;
            mxW = Mathf.MoveTowards(mxW, on && (holding || ct < HoldAt + 0.02f || !Released()) ? 1f : 0f, dt / (on ? 0.04f : 0.12f));
            mxHoldW = Mathf.MoveTowards(mxHoldW, !V13 && holding && ct >= HoldAt - 0.01f && mxHold ? 1f : 0f, dt / 0.15f);
            if (mxW <= 0f) { armFixSet = false; return; }
            var clipHand = lHand ? lHand.localRotation : Quaternion.identity;
            // cast: clip f7 -> f16 over the 검결 + thrust (ct 0 -> LaunchAt), then it eases to f19
            float castT = ct < LaunchAt ? Mathf.Lerp(MxCastFrom, MxCastPeak, ct / LaunchAt) : Mathf.Lerp(MxCastPeak, MxCastPeak + 0.1f, Mathf.Clamp01((ct - LaunchAt) / 0.2f));
            if (!V16) { body.Sample(mxCast, castT); body.Apply(0f, 0f, 0f, mxW * bodyW, 0f); }
            if (mxHoldW > 0f)
            {
                mxHoldT += dt;
                float span = MxHoldTo - MxHoldFrom;
                body.Sample(mxHold, MxHoldFrom + Mathf.Repeat(mxHoldT, span));
                body.Apply(0f, 0f, 0f, mxHoldW * mxW * bodyW, 0f);
            }
            else mxHoldT = 0f;
            // aim: turn the upper arm about the vertical through the shoulder so shoulder -> hand points along the aim
            if (lUpperB && lHand)
            {
                // v12b: yaw AND pitch: the Mixamo hold raised the arm toward overhead; the line now points at the aim, ~8 deg up
                var d = lHand.position - lUpperB.position;
                var want = (aim + Vector3.up * MxArmLift).normalized;
                if (d.sqrMagnitude > 1e-4f)
                {
                    var fix = Quaternion.FromToRotation(d.normalized, want);
                    if (V13)
                    {
                        // eased (no per-frame snapping), then a slow breath about the horizontal axis across the arm
                        armFix = armFixSet ? Quaternion.Slerp(armFix, fix, Mathf.Min(1f, dt * 14f)) : fix; armFixSet = true;
                        fix = armFix;
                        if (holding && ct >= HoldAt)
                        {
                            var ax = Vector3.Cross(Vector3.up, want); if (ax.sqrMagnitude > 1e-4f)
                                fix = Quaternion.AngleAxis(BreathDeg * Mathf.Sin(holdT * BreathHz * 2f * Mathf.PI), ax.normalized) * fix;
                        }
                    }
                    lUpperB.rotation = Quaternion.Slerp(Quaternion.identity, fix, mxW) * lUpperB.rotation;
                }
            }
            if (V13 && lHand) lHand.localRotation = Quaternion.Slerp(lHand.localRotation, clipHand, mxW);
        }

        static Vector3 FlatN(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
        static Vector3 ChestFwd(Transform l, Transform r) => FlatN(Vector3.Cross(r.position - l.position, Vector3.up));

        // the chest gets the same yaw against the cursor aim as the clip's chest has against the clip's own root (= the aim
        // when standing): one yaw on the Spine bone, the clip's own spine bend / twist / lean stay as keyed
        void AimUpper(float w)
        {
            if (!spineB || !rUpperB || !lUpperB) return;
            var pl = body.ProxyBone(HumanBodyBones.LeftUpperArm); var pr = body.ProxyBone(HumanBodyBones.RightUpperArm);
            var ph = body.ProxyBone(HumanBodyBones.Hips);
            if (!pl || !pr || !ph) return;
            var root = ph.root;
            float clipYaw = Vector3.SignedAngle(FlatN(root.forward), ChestFwd(pl, pr), Vector3.up);
            var want = Quaternion.AngleAxis(clipYaw, Vector3.up) * aim;
            float d = Vector3.SignedAngle(ChestFwd(lUpperB, rUpperB), want, Vector3.up);
            d = DirRule ? Mathf.Clamp(d, -ChestMax, ChestMax) : Mathf.Clamp(d, -150f, 150f);
            upperYaw = d * w;
            spineB.rotation = Quaternion.AngleAxis(upperYaw, Vector3.up) * spineB.rotation;
        }

        // v6 check (Kido: the sword must never go through the legs): closest distance between the held sword line
        // (hand -> assembled tip) and the leg bones, every frame; logged by EogeomCapture
        public float LegClear { get; private set; } = 9f;
        public float HeadClear { get; private set; } = 9f;    // v9: blade vs head / neck
        public float TorsoClear { get; private set; } = 9f;   // v9: blade vs spine and the far (left) shoulder
        public float ShoulderClear { get; private set; } = 9f; // v9: blade vs the right shoulder it rests on (contact ~0.07 m)
        Transform[] bodyB;
        static float SegDist(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
        {
            float best = 9f; var d = b1 - b0;
            for (int i = 0; i <= 16; i++)
            {
                var pnt = Vector3.Lerp(a0, a1, i / 16f);
                float tt = Mathf.Clamp01(Vector3.Dot(pnt - b0, d) / Mathf.Max(d.sqrMagnitude, 1e-6f));
                best = Mathf.Min(best, Vector3.Distance(pnt, b0 + d * tt));
            }
            return best;
        }
        Transform[] legB;
        void MeasureLegClear()
        {
            if (!swordT || !rHand) { LegClear = 9f; return; }
            legB ??= new[] { anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg), anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg), anim.GetBoneTransform(HumanBodyBones.LeftFoot),
                             anim.GetBoneTransform(HumanBodyBones.RightUpperLeg), anim.GetBoneTransform(HumanBodyBones.RightLowerLeg), anim.GetBoneTransform(HumanBodyBones.RightFoot) };
            var a0 = rHand.position; var a1 = swordT.TransformPoint(bladeBaseLocal + cutA * bladeLenLocal);
            float best = 9f;
            for (int s0 = 0; s0 < 2; s0++)
                for (int j = 0; j < 2; j++)
                {
                    var b0 = legB[s0 * 3 + j]; var b1 = legB[s0 * 3 + j + 1]; if (!b0 || !b1) continue;
                    for (int i = 0; i <= 16; i++)
                    {
                        var pnt = Vector3.Lerp(a0, a1, i / 16f); var d = b1.position - b0.position;
                        float tt = Mathf.Clamp01(Vector3.Dot(pnt - b0.position, d) / Mathf.Max(d.sqrMagnitude, 1e-6f));
                        best = Mathf.Min(best, Vector3.Distance(pnt, b0.position + d * tt));
                    }
                }
            LegClear = best;
            bodyB ??= new[] { anim.GetBoneTransform(HumanBodyBones.Head), anim.GetBoneTransform(HumanBodyBones.Neck), anim.GetBoneTransform(HumanBodyBones.Spine),
                              anim.GetBoneTransform(HumanBodyBones.Chest), anim.GetBoneTransform(HumanBodyBones.RightShoulder), anim.GetBoneTransform(HumanBodyBones.RightUpperArm),
                              anim.GetBoneTransform(HumanBodyBones.LeftShoulder), anim.GetBoneTransform(HumanBodyBones.LeftUpperArm) };
            if (bodyB[0] && bodyB[1] && bodyB[2] && bodyB[3])
            {
                var head = bodyB[0].position;
                HeadClear = Mathf.Min(SegDist(a0, a1, head, head + Vector3.up * 0.22f * (Vector3.Distance(rHand.position, lFore ? lFore.position : rHand.position) > 0f ? 1f : 1f)), SegDist(a0, a1, bodyB[1].position, head));
                TorsoClear = SegDist(a0, a1, bodyB[2].position, bodyB[1].position);
                if (bodyB[6] && bodyB[7]) TorsoClear = Mathf.Min(TorsoClear, SegDist(a0, a1, bodyB[6].position, bodyB[7].position));
                if (bodyB[4] && bodyB[5]) ShoulderClear = SegDist(a0, a1, bodyB[4].position, bodyB[5].position);
            }
        }

        Vector3 SwordDir => swordT ? swordT.TransformDirection(cutA).normalized : rHand.forward;
        Vector3 SwordFlat => swordT ? swordT.TransformDirection(cutF).normalized : rHand.up;
        Vector3 HomePos(int k)
        {
            if (!swordT) return rHand.position;
            float plateLocal = bladeLenLocal / plates;
            return swordT.TransformPoint(bladeBaseLocal + cutA * ((k + 0.5f) * plateLocal));
        }

        // read the hero's sword mesh once, sort its triangles into the hilt (rigid) and the blade slices
        // (the same split as WhipCombat.CutSword: BladeFrac of the length from the tip, Plates slices)
        void CutSword()
        {
            swordCutTried = true;
            var mf = sword.GetComponentInChildren<MeshFilter>(true);
            var mr = mf ? mf.GetComponent<MeshRenderer>() : null;
            if (!mf || !mr || !mf.sharedMesh) return;
            var src = mf.sharedMesh;
            Vector3[] v; Vector3[] n; Vector2[] uv;
            try { v = src.vertices; n = src.normals; uv = src.uv; } catch { return; }
            if (v == null || v.Length == 0) { Debug.LogWarning("[RoomRun] eogeom: sword mesh not readable"); return; }
            swordT = mf.transform;
            var b = src.bounds; var e = b.extents;
            int ax = e.x >= e.y && e.x >= e.z ? 0 : e.y >= e.z ? 1 : 2;
            int fl = e.x <= e.y && e.x <= e.z ? 0 : e.y <= e.z ? 1 : 2;
            Vector3 A = ax == 0 ? Vector3.right : ax == 1 ? Vector3.up : Vector3.forward;
            cutF = fl == 0 ? Vector3.right : fl == 1 ? Vector3.up : Vector3.forward;
            var lo = b.center - Vector3.Scale(A, e); var hi = b.center + Vector3.Scale(A, e);
            bool hiTip = (swordT.TransformPoint(hi) - rHand.position).sqrMagnitude > (swordT.TransformPoint(lo) - rHand.position).sqrMagnitude;
            cutA = hiTip ? A : -A;
            cutS = Vector3.Cross(cutA, cutF);
            var tipLocal = hiTip ? hi : lo;
            float total = e[ax] * 2f;
            bladeLenLocal = total * WhipCombat.BladeFrac;
            bladeBaseLocal = tipLocal - cutA * bladeLenLocal;
            cutScale = swordT.lossyScale.x;

            var P = new List<Vector3>(); var N = new List<Vector3>(); var U = new List<Vector2>(); var K = new List<int>();
            var map = new Dictionary<long, int>();
            int subs = src.subMeshCount;
            var tris = new int[subs][];
            bool hasN = n != null && n.Length == v.Length, hasUv = uv != null && uv.Length == v.Length;
            for (int sm = 0; sm < subs; sm++)
            {
                var tri = src.GetTriangles(sm);
                var outT = new int[tri.Length];
                for (int i = 0; i < tri.Length; i += 3)
                {
                    var c = (v[tri[i]] + v[tri[i + 1]] + v[tri[i + 2]]) / 3f;
                    float u = Vector3.Dot(c - bladeBaseLocal, cutA) / bladeLenLocal;
                    int k = u < 0f ? -1 : Mathf.Min(plates - 1, (int)(u * plates));
                    for (int j = 0; j < 3; j++)
                    {
                        int vi = tri[i + j];
                        long key = ((long)(k + 1) << 32) | (uint)vi;
                        if (!map.TryGetValue(key, out int ni))
                        {
                            ni = P.Count; map[key] = ni;
                            P.Add(v[vi]); N.Add(hasN ? n[vi] : Vector3.up); U.Add(hasUv ? uv[vi] : Vector2.zero); K.Add(k);
                        }
                        outT[i + j] = ni;
                    }
                }
                tris[sm] = outT;
            }
            cvPos = P.ToArray(); cvNrm = N.ToArray(); cvPlate = K.ToArray();
            cvOut = new Vector3[cvPos.Length]; cvOutN = new Vector3[cvPos.Length];
            blade.Clear();
            blade.indexFormat = cvPos.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            blade.vertices = cvPos; blade.normals = cvNrm; blade.uv = U.ToArray();
            blade.subMeshCount = subs;
            for (int sm = 0; sm < subs; sm++) blade.SetTriangles(tris[sm], sm, false);
            var pl = new List<int>[plates]; for (int k = 0; k < plates; k++) pl[k] = new List<int>();
            for (int sm = 0; sm < subs; sm++)
                for (int i = 0; i < tris[sm].Length; i += 3)
                {
                    int k = K[tris[sm][i]];
                    if (k >= 0) { pl[k].Add(tris[sm][i]); pl[k].Add(tris[sm][i + 1]); pl[k].Add(tris[sm][i + 2]); }
                }
            plateTris = new int[plates][]; for (int k = 0; k < plates; k++) plateTris[k] = pl[k].ToArray();
            rimV = new Vector3[cvPos.Length]; rimUv = new Vector2[cvPos.Length]; rimC = new Color[cvPos.Length];
            bladeGo.GetComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
            swordCut = true;
            Debug.Log($"[RoomRun] eogeom: sword cut into {plates} segments ({cvPos.Length} verts, blade {bladeLenLocal * Mathf.Abs(swordT.lossyScale[ax]):0.00} m)");
        }

        void DrawPlates()
        {
            var l2w = swordT.localToWorldMatrix;
            float plateLocal = bladeLenLocal / plates;
            var sd = new Vector3[plates];
            for (int k = 0; k < plates; k++) if (st[k] != 0) sd[k] = Vector3.Cross(dir[k], nrm[k]).normalized;
            for (int k = 0; k < plates; k++)
            {
                // grows over the first .07 s out, shrinks back over the last 35 % of the way home
                float g = st[k] == 1 ? Mathf.SmoothStep(0f, 1f, pt[k] / 0.07f) : st[k] == 2 ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 1f, pt[k] / dur[k])) : 0f;
                flyS[k] = Mathf.Lerp(1f, FlyScale, g);
            }
            for (int i = 0; i < cvPos.Length; i++)
            {
                int k = cvPlate[i];
                if (k < 0 || st[k] == 0) { cvOut[i] = l2w.MultiplyPoint3x4(cvPos[i]); cvOutN[i] = l2w.MultiplyVector(cvNrm[i]).normalized; continue; }
                var c = bladeBaseLocal + cutA * ((k + 0.5f) * plateLocal);
                var d = cvPos[i] - c;
                float fs = cutScale * flyS[k];
                float da = Vector3.Dot(d, cutA) * fs, df = Vector3.Dot(d, cutF) * fs, ds = Vector3.Dot(d, cutS) * fs;
                cvOut[i] = pos[k] + dir[k] * da + nrm[k] * df + sd[k] * ds;
                var nn = cvNrm[i];
                cvOutN[i] = dir[k] * Vector3.Dot(nn, cutA) + nrm[k] * Vector3.Dot(nn, cutF) + sd[k] * Vector3.Dot(nn, cutS);
            }
            blade.vertices = cvOut; blade.normals = cvOutN;
            blade.RecalculateBounds();
        }

        // v5 A: every GhostEvery seconds a flying piece leaves a copy of itself that fades over GhostLife; a few sparks peel off
        readonly List<Vector3> ghV = new List<Vector3>(65536);
        readonly List<Vector2> ghUv = new List<Vector2>(65536);
        readonly List<Color> ghC = new List<Color>(65536);
        readonly List<int> ghT = new List<int>(131072);
        void UpdateGhostsAndSparks(float dt)
        {
            ghostMesh.Clear();
            ghV.Clear(); ghUv.Clear(); ghC.Clear(); ghT.Clear();
            if (TrailStyle != 1 || !swordCut || plateTris == null) { sparks.Clear(); return; }
            float now = Time.time;
            for (int k = 0; k < plates; k++)
            {
                var G = ghosts[k];
                G.RemoveAll(g => now - g.time > GhostLife);
                if (st[k] != 0 && now >= ghostNext[k])
                {
                    ghostNext[k] = now + GhostEvery;
                    G.Add(new Ghost { p = pos[k], d = dir[k], n = nrm[k], s = flyS[k], time = now });
                    while (G.Count > GhostCount) G.RemoveAt(0);
                }
                if (st[k] != 0 && now >= sparkNext[k] && sparks.Count < 200)
                {
                    sparkNext[k] = now + Random.Range(0.025f, 0.06f);
                    var side = Vector3.Cross(dir[k], Vector3.up).normalized;
                    sparks.Add(new Spark { p = pos[k] - dir[k] * 0.1f * flyS[k], v = (side * Random.Range(-1f, 1f) + Vector3.up * Random.Range(0.2f, 1f)) * 1.2f - dir[k] * 0.8f, t = 0f, life = Random.Range(0.15f, 0.28f), size = Random.Range(0.025f, 0.05f) });
                }
            }
            // ghost copies of the plate mesh (span from the view angle like the rim, alpha by age)
            var cam = RunDirector.Instance && RunDirector.Instance.Rig ? RunDirector.Instance.Rig.Cam : Camera.main;
            var cp = cam ? cam.transform.position : Vector3.up * 20f;
            float plateLocal = bladeLenLocal / plates;
            for (int k = 0; k < plates; k++)
            {
                var G = ghosts[k]; if (G.Count == 0) continue;
                var tris = plateTris[k];
                foreach (var g in G)
                {
                    float age = Mathf.Clamp01((now - g.time) / GhostLife);
                    if (age >= 1f) continue;
                    var sdv = Vector3.Cross(g.d, g.n).normalized;
                    var c0 = bladeBaseLocal + cutA * ((k + 0.5f) * plateLocal);
                    int b = ghV.Count;
                    // copy only the verts this plate's triangles use (remap)
                    var map = ghostMap ??= new Dictionary<int, int>(2048); map.Clear();
                    foreach (int vi in tris)
                    {
                        if (!map.TryGetValue(vi, out int ni))
                        {
                            ni = ghV.Count; map[vi] = ni;
                            var d = cvPos[vi] - c0; float fs = cutScale * g.s * (1f + age * 0.25f);
                            var wp = g.p + g.d * (Vector3.Dot(d, cutA) * fs) + g.n * (Vector3.Dot(d, cutF) * fs) + sdv * (Vector3.Dot(d, cutS) * fs);
                            var nn = cvNrm[vi]; var wn = g.d * Vector3.Dot(nn, cutA) + g.n * Vector3.Dot(nn, cutF) + sdv * Vector3.Dot(nn, cutS);
                            float span = 1f - Mathf.Abs(Vector3.Dot(wn.normalized, (cp - wp).normalized));
                            ghV.Add(wp); ghUv.Add(new Vector2(0.2f + age * 0.7f, 0.5f + 0.5f * span)); ghC.Add(new Color(1f, 1f, 1f, 0.55f * (1f - age)));
                        }
                        ghT.Add(ni);
                    }
                }
            }
            // sparks: tiny camera-facing diamonds
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                var sp = sparks[i]; sp.t += dt; sp.p += sp.v * dt; sp.v *= 1f - 3f * dt; sparks[i] = sp;
                if (sp.t >= sp.life) { sparks.RemoveAt(i); continue; }
                float a = 1f - sp.t / sp.life;
                var view = (cp - sp.p).normalized; var r = Vector3.Cross(view, Vector3.up).normalized * sp.size; var u = Vector3.Cross(r, view).normalized * sp.size;
                int b = ghV.Count;
                ghV.Add(sp.p - r); ghV.Add(sp.p + u); ghV.Add(sp.p + r); ghV.Add(sp.p - u);
                for (int j = 0; j < 4; j++) { ghUv.Add(new Vector2(0.05f, 1f)); ghC.Add(new Color(1f, 1f, 1f, a)); }
                ghT.Add(b); ghT.Add(b + 1); ghT.Add(b + 2); ghT.Add(b); ghT.Add(b + 2); ghT.Add(b + 3);
            }
            if (ghT.Count == 0) return;
            ghostMesh.SetVertices(ghV); ghostMesh.SetUVs(0, ghUv); ghostMesh.SetColors(ghC); ghostMesh.SetTriangles(ghT, 0, false);
            ghostMesh.RecalculateBounds();
        }
        Dictionary<int, int> ghostMap;

        // v5 B: an asset trail prefab rides each flying piece (spawned once per piece, emitting only while it flies)
        GameObject trailPrefab; bool trailPrefabTried; string trailPrefabFor;
        void UpdateAssetTrails()
        {
            if (TrailStyle != 2 || string.IsNullOrEmpty(TrailPrefabPath))
            {
                if (assetTrail != null) foreach (var a in assetTrail) if (a && a.activeSelf) a.SetActive(false);
                return;
            }
            if (trailPrefabFor != TrailPrefabPath)
            {
                trailPrefabFor = TrailPrefabPath; trailPrefab = null;
                foreach (var a in assetTrail) if (a) Destroy(a);
#if UNITY_EDITOR
                trailPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(TrailPrefabPath);
#endif
                if (!trailPrefab) Debug.LogWarning("[RoomRun] eogeom: trail prefab not found " + TrailPrefabPath);
                else PrewarmTrails();
            }
            if (trailRecolorFor != Recolor) { trailRecolorFor = Recolor; foreach (var a in assetTrail) if (a) ApplyTrailLook(a); }
            if (!trailPrefab) return;
            if (prewarmFrames > 0 && --prewarmFrames == 0)
                foreach (var a0 in assetTrail) if (a0) { foreach (var ps in a0.GetComponentsInChildren<ParticleSystem>()) ps.Clear(); a0.SetActive(false); }
            for (int k = 0; k < plates; k++)
            {
                bool fly = st[k] != 0;
                var a = assetTrail[k];
                if (!a && fly) a = MakeTrail(k);
                if (!a) continue;
                if (fly)
                {
                    if (!a.activeSelf) { a.transform.position = pos[k]; a.SetActive(true); foreach (var tr in a.GetComponentsInChildren<TrailRenderer>()) tr.Clear(); }
                    a.transform.SetPositionAndRotation(pos[k], Quaternion.LookRotation(dir[k], nrm[k]));
                    foreach (var ps in a.GetComponentsInChildren<ParticleSystem>()) { var em = ps.emission; em.enabled = true; }
                    foreach (var tr in a.GetComponentsInChildren<TrailRenderer>()) tr.emitting = true;
                }
                else if (a.activeSelf)
                {
                    foreach (var ps in a.GetComponentsInChildren<ParticleSystem>()) { var em = ps.emission; em.enabled = false; }
                    foreach (var tr in a.GetComponentsInChildren<TrailRenderer>()) tr.emitting = false;
                }
            }
        }

        bool trailRecolorFor = true;
        GameObject MakeTrail(int k)
        {
            var a = assetTrail[k] = Instantiate(trailPrefab); a.name = "EogeomAssetTrail" + k;
            foreach (var ps in a.GetComponentsInChildren<ParticleSystem>()) { var m = ps.main; m.startLifetimeMultiplier *= AssetTrailLife; }
            ApplyTrailLook(a);
            return a;
        }
        readonly Dictionary<ParticleSystemRenderer, Material> trailOrig = new Dictionary<ParticleSystemRenderer, Material>();
        void ApplyTrailLook(GameObject a)
        {
            foreach (var r in a.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (!trailOrig.TryGetValue(r, out var orig)) trailOrig[r] = orig = r.sharedMaterial;
                // the pack's screen-distortion layer renders magenta here (needs an opaque-texture setup we do not use): off
                if (orig && orig.name.Contains("Distortion")) { r.enabled = false; continue; }
                r.sharedMaterial = Recolor ? Recolored(orig) : orig;
                if (r.trailMaterial) r.trailMaterial = Recolor ? Recolored(r.trailMaterial) : r.trailMaterial;
            }
        }
        // v6: make every piece's trail up front and show it for one frame at the hilt, so the shaders are compiled / uploaded
        // before the first volley (the first volley used to draw a faint trail)
        void PrewarmTrails()
        {
            for (int k = 0; k < plates; k++)
            {
                var a = assetTrail[k] ? assetTrail[k] : MakeTrail(k);
                a.transform.position = HomePos(k);
                a.SetActive(true);
                foreach (var ps in a.GetComponentsInChildren<ParticleSystem>()) { ps.Emit(1); var em = ps.emission; em.enabled = false; }
            }
            prewarmFrames = 2;
        }
        int prewarmFrames;

        // fingertip trail: one instance, made with the piece trails (prewarmed), scaled down, life cut to TipTrailLife
        // v15: the eye flame copied onto the left fist + a small eye-blue point light
        void UpdateHandWrap(Vector3 c, float a)
        {
            if (!wrapTried)
            {
                wrapTried = true;
                ParticleSystem eye = null;
                foreach (var ps in anim.GetComponentsInChildren<ParticleSystem>(true)) if (ps.name == "EyeFlame") { eye = ps; break; }
                if (eye)
                {
                    handWrap = Instantiate(eye.gameObject); handWrap.name = "EogeomHandWrap";
                    Debug.Log($"[RoomRun] eogeom v15: eye flame lossyScale {eye.transform.lossyScale.x:0.0000} local {eye.transform.localScale.x:0.0000}, clone scale {handWrap.transform.localScale.x:0.0000}");
                    handWrap.transform.localScale = Vector3.one;
                    foreach (var mb in handWrap.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(mb);
                    wrapPs = handWrap.GetComponentsInChildren<ParticleSystem>(true); wrapRate = new float[wrapPs.Length];
                    for (int i = 0; i < wrapPs.Length; i++)
                    {
                        var ps = wrapPs[i];
                        var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.World; m.scalingMode = ParticleSystemScalingMode.Shape;
                        m.startSpeedMultiplier = WrapSpeed; m.startSizeMultiplier *= WrapSize;
                        var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = WrapRadius; sh.radiusThickness = 0.3f;
                        sh.position = Vector3.zero; sh.rotation = Vector3.zero; sh.scale = Vector3.one;
                        // the eye flame renders as stretched streaks whose speed comes from the head flow script; without it they
                        // collapse to slivers, so the hand copy uses camera-facing billboards that rise a little like a flame
                        var pr = ps.GetComponent<ParticleSystemRenderer>(); if (pr) { pr.renderMode = ParticleSystemRenderMode.Billboard; pr.maxParticleSize = 1f; }
                        m.gravityModifierMultiplier = -0.12f;
                        var em = ps.emission; wrapRate[i] = WrapRate; em.rateOverTimeMultiplier = 0f;
                        ps.Play();
                    }
                    Debug.Log($"[RoomRun] eogeom v15: hand wrap = copy of {eye.name} ({wrapPs.Length} systems)");
                }
                else Debug.LogWarning("[RoomRun] eogeom v15: hero EyeFlame not found, hand wrap off");
                var lg = new GameObject("EogeomHandLight"); handLight = lg.AddComponent<Light>();
                handLight.type = LightType.Point; handLight.color = HandLightColor; handLight.range = HandLightRange;
                handLight.shadows = LightShadows.None; handLight.intensity = 0f;
            }
            float flick = 0.88f + 0.12f * Mathf.PerlinNoise(Time.time * 7f, 0.3f);
            if (handWrap)
            {
                handWrap.transform.SetPositionAndRotation(c, Quaternion.identity);
                for (int i = 0; i < wrapPs.Length; i++) { var em = wrapPs[i].emission; em.rateOverTimeMultiplier = wrapRate[i] * a; }
                if (a > 0.5f && !wrapLogged && wrapPs.Length > 0 && wrapPs[0].particleCount > 0)
                {
                    wrapLogged = true; var m0 = wrapPs[0].main;
                    Debug.Log($"[RoomRun] eogeom v15: wrap particles {wrapPs[0].particleCount}, startSize x{m0.startSizeMultiplier:0.000}, playing {wrapPs[0].isPlaying}, at {c}, renderer {(wrapPs[0].GetComponent<Renderer>() ? wrapPs[0].GetComponent<Renderer>().enabled.ToString() : "none")} layer {handWrap.layer}");
                }
            }
            if (handLight) { handLight.transform.position = c + Vector3.up * 0.12f; handLight.intensity = HandLightIntensity * a * flick; handLight.enabled = a > 0.01f; }
        }

        void UpdateTipTrail(float a)
        {
            bool want = TipTrail && TrailStyle == 2 && trailPrefab && a > 0.15f;
            if (!tipTrail && trailPrefab && TipTrail)
            {
                tipTrail = Instantiate(trailPrefab); tipTrail.name = "EogeomTipTrail";
                tipTrail.transform.localScale = Vector3.one * TipTrailScale;
                foreach (var ps in tipTrail.GetComponentsInChildren<ParticleSystem>())
                {
                    var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    m.startLifetimeMultiplier *= TipTrailLife;   // the pack's ~1 s -> ~0.25 s
                }
                ApplyTrailLook(tipTrail);
                tipTrail.transform.position = tipPos; tipTrail.SetActive(true);
                foreach (var ps in tipTrail.GetComponentsInChildren<ParticleSystem>()) { ps.Emit(1); var em = ps.emission; em.enabled = false; }   // prewarm
                tipTrailOn = false; prewarmTip = 2;
                return;
            }
            if (!tipTrail) return;
            if (prewarmTip > 0 && --prewarmTip == 0) { foreach (var ps in tipTrail.GetComponentsInChildren<ParticleSystem>()) ps.Clear(); }
            if (want)
            {
                if (!tipTrailOn) { tipTrail.transform.position = tipPos; foreach (var tr in tipTrail.GetComponentsInChildren<TrailRenderer>()) tr.Clear(); }
                tipTrail.transform.SetPositionAndRotation(tipPos, Quaternion.LookRotation(tipDir, Vector3.up));
            }
            if (want != tipTrailOn)
            {
                tipTrailOn = want;
                foreach (var ps in tipTrail.GetComponentsInChildren<ParticleSystem>()) { var em = ps.emission; em.enabled = want; }
                foreach (var tr in tipTrail.GetComponentsInChildren<TrailRenderer>()) tr.emitting = want;
            }
        }
        int prewarmTip;

        void DrawRim()
        {
            rim.Clear();
            if (!Rim || plateTris == null) return;
            var cam = RunDirector.Instance && RunDirector.Instance.Rig ? RunDirector.Instance.Rig.Cam : Camera.main;
            var cp = cam ? cam.transform.position : Vector3.up * 20f;
            rimT.Clear();
            for (int k = 0; k < plates; k++)
            {
                rimW[k] = 0f;
                if (st[k] == 0) continue;
                // full while flying; fades over the last part of the way home so the rejoined blade is plain steel again
                rimW[k] = st[k] == 1 ? Mathf.Clamp01(pt[k] / 0.04f) : 1f - Mathf.InverseLerp(0.75f, 1f, pt[k] / dur[k]);
                if (rimW[k] > 0.01f) rimT.AddRange(plateTris[k]);
            }
            if (rimT.Count == 0) return;
            for (int i = 0; i < cvPos.Length; i++)
            {
                int k = cvPlate[i];
                if (k < 0 || rimW[k] <= 0f) { rimV[i] = cvOut[i]; rimUv[i] = Vector2.zero; rimC[i] = Color.clear; continue; }
                var n = cvOutN[i];
                rimV[i] = cvOut[i] + n * 0.012f;
                var v = (cp - cvOut[i]).normalized;
                float span = 1f - Mathf.Abs(Vector3.Dot(n, v));
                rimUv[i] = new Vector2(0.05f, 0.55f + 0.45f * span);
                rimC[i] = new Color(1f, 1f, 1f, rimW[k]);
            }
            rim.vertices = rimV; rim.uv = rimUv; rim.colors = rimC;
            rim.SetTriangles(rimT, 0, false);
            rim.RecalculateBounds();
        }

        // one thin ribbon behind every flying segment, lying flat (reads from the top-down camera)
        readonly List<Vector3> rv = new List<Vector3>(1024);
        readonly List<Vector2> ruv = new List<Vector2>(1024);
        readonly List<Color> rc = new List<Color>(1024);
        readonly List<int> rt = new List<int>(2048);
        public static float TrailLife = 0.14f, TrailWidth = 0.09f;
        public static float TrailLifeA = 0.35f, TrailWidthA = 0.17f;
        void DrawTrails()
        {
            float now = Time.time;
            rv.Clear(); ruv.Clear(); rc.Clear(); rt.Clear();
            for (int k = 0; k < plates; k++)
            {
                var P = trailP[k]; var T = trailT[k];
                if (TrailStyle == 2) { P.Clear(); T.Clear(); continue; }
                float life = TrailStyle == 1 ? TrailLifeA : TrailLife, width = TrailStyle == 1 ? TrailWidthA : TrailWidth;
                int drop = 0; while (drop < T.Count && now - T[drop] > life) drop++;
                if (drop > 0) { P.RemoveRange(0, drop); T.RemoveRange(0, drop); }
                int n = P.Count; if (n < 2) continue;
                int start = rv.Count;
                for (int i = 0; i < n; i++)
                {
                    var tan = i < n - 1 ? P[i + 1] - P[i] : P[i] - P[i - 1];
                    var side = Vector3.Cross(tan, Vector3.up); side = side.sqrMagnitude > 1e-8f ? side.normalized : Vector3.right;
                    float age = Mathf.Clamp01((now - T[i]) / life);
                    float w = width * (1f - age * (TrailStyle == 1 ? 0.8f : 0.6f));
                    // three across: soft outer edges (uv.y 0) and a bright core (uv.y 1)
                    rv.Add(P[i] - side * w); ruv.Add(new Vector2(age, 0f)); rc.Add(new Color(1, 1, 1, 0.7f));
                    rv.Add(P[i]); ruv.Add(new Vector2(age, 1f)); rc.Add(new Color(1, 1, 1, 0.7f));
                    rv.Add(P[i] + side * w); ruv.Add(new Vector2(age, 0f)); rc.Add(new Color(1, 1, 1, 0.7f));
                    if (i > 0)
                    {
                        int a = start + (i - 1) * 3, b = start + i * 3;
                        rt.Add(a); rt.Add(b); rt.Add(a + 1); rt.Add(a + 1); rt.Add(b); rt.Add(b + 1);
                        rt.Add(a + 1); rt.Add(b + 1); rt.Add(a + 2); rt.Add(a + 2); rt.Add(b + 1); rt.Add(b + 2);
                    }
                }
            }
            ribbon.Clear();
            if (rt.Count == 0) return;
            ribbon.SetVertices(rv); ribbon.SetUVs(0, ruv); ribbon.SetColors(rc); ribbon.SetTriangles(rt, 0, false);
            ribbon.RecalculateBounds();
        }

        // 검결 stand-in (v4, Kido: "주먹 앞에 손가락 두개같은 빛"): two finger-shaped glow cards (index + middle, side by side,
        // a little apart) sticking out of the front of the left fist along the hand line, plus a faint knuckle glow. Additive,
        // no light. Sizes are in the rig's own units (forearm length / 0.203 m in the Blender source) so they scale with the hero.
        // v7 (Kido: "손끝에도 칼 vfx나게 해봐"): the same blue-white Cosmos trail rides the fingertips while the 검결 is held,
        // smaller and shorter, so the raise and the thrust leave a sword-like streak from the two fingers
        public static bool TipTrail = true;
        public static float TipTrailScale = 0.35f, TipTrailLife = 0.25f;
        GameObject tipTrail; bool tipTrailOn; Vector3 tipPos, tipDir;
        public static float FingerLen = 0.13f, FingerThick = 0.034f, FingerGap = 0.034f, FistFront = 0.085f;   // v5: thicker/brighter for z1.6
        readonly List<int> gt = new List<int>(96);
        // v4b: the fist's own front, measured once from the skinned mesh in the hand bone's bind space (the vertices weighted to
        // LeftHand): fingers start at the centre of the fist's front cap and point along the forearm -> hand line, side by side
        // across the fist's widest direction. Everything below is in hand-local space, so the fingers ride the bone exactly.
        bool fistKnown, fistTried; Vector3 fistFrontL, fistAxisL = Vector3.up, fistSideL = Vector3.right; float fistWidthL = 0.05f;
        void MeasureFist()
        {
            fistTried = true;
            foreach (var smr in anim.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = smr.sharedMesh; var bones = smr.bones;
                if (!m || bones == null) continue;
                int hi = System.Array.IndexOf(bones, lHand), fi = System.Array.IndexOf(bones, lFore);
                Debug.Log($"[RoomRun] eogeom fist probe: {smr.name} bones {bones.Length} hand {hi} fore {fi} readable {m.isReadable} verts {m.vertexCount}");
                if (hi < 0 || fi < 0) continue;
                Vector3[] v; BoneWeight[] bw;
                try { v = m.vertices; bw = m.boneWeights; } catch (System.Exception ex) { Debug.Log("[RoomRun] eogeom fist probe: " + ex.Message); continue; }
                bool baked = false;
                if ((v == null || v.Length == 0) && bw != null && bw.Length > 0)
                {
                    // non-readable hero mesh: bake the current skinned pose and bring it into hand-local space instead of bind space
                    var tmp = new Mesh();
                    try { smr.BakeMesh(tmp, true); v = tmp.vertices; } catch (System.Exception ex) { Debug.Log("[RoomRun] eogeom fist bake: " + ex.Message); }
                    Destroy(tmp);
                    if (v != null && v.Length == bw.Length)
                    {
                        var l2w = smr.transform.localToWorldMatrix; var w2h = lHand.worldToLocalMatrix;
                        for (int i = 0; i < v.Length; i++) v[i] = w2h.MultiplyPoint3x4(l2w.MultiplyPoint3x4(v[i]));
                        baked = true;
                    }
                }
                Debug.Log($"[RoomRun] eogeom fist probe: got {(v == null ? -1 : v.Length)} verts, {(bw == null ? -1 : bw.Length)} weights");
                if (v == null || bw == null || bw.Length != v.Length) continue;
                var bp = m.bindposes;
                var handW = bp[hi].inverse; var foreW = bp[fi].inverse;
                var A = baked ? lHand.InverseTransformDirection(lHand.position - lFore.position).normalized
                              : bp[hi].MultiplyVector(handW.GetColumn(3) - foreW.GetColumn(3)).normalized;   // forearm -> hand, hand-local
                var pts = new List<Vector3>();
                for (int i = 0; i < v.Length; i++)
                {
                    var b = bw[i];
                    float w = b.boneIndex0 == hi ? b.weight0 : b.boneIndex1 == hi ? b.weight1 : b.boneIndex2 == hi ? b.weight2 : b.boneIndex3 == hi ? b.weight3 : 0f;
                    if (w > 0.5f) pts.Add(baked ? v[i] : bp[hi].MultiplyPoint3x4(v[i]));
                }
                Debug.Log($"[RoomRun] eogeom fist probe: {pts.Count} hand verts");
                if (pts.Count < 8) continue;
                float front = float.MinValue; foreach (var q in pts) front = Mathf.Max(front, Vector3.Dot(q, A));
                float back = float.MaxValue; foreach (var q in pts) back = Mathf.Min(back, Vector3.Dot(q, A));
                float capDepth = (front - back) * 0.18f;
                var c = Vector3.zero; int n = 0;
                foreach (var q in pts) if (Vector3.Dot(q, A) > front - capDepth) { c += q; n++; }
                c /= Mathf.Max(1, n);
                // widest axis of the fist perpendicular to A (8 candidate directions)
                var u0 = Vector3.Cross(A, Mathf.Abs(A.y) < 0.9f ? Vector3.up : Vector3.right).normalized; var u1 = Vector3.Cross(A, u0);
                float best = -1f; Vector3 bestAx = u0;
                for (int k = 0; k < 8; k++)
                {
                    float ang = k * Mathf.PI / 8f; var ax = u0 * Mathf.Cos(ang) + u1 * Mathf.Sin(ang);
                    float lo = float.MaxValue, hiV = float.MinValue;
                    foreach (var q in pts) { float d = Vector3.Dot(q, ax); lo = Mathf.Min(lo, d); hiV = Mathf.Max(hiV, d); }
                    if (hiV - lo > best) { best = hiV - lo; bestAx = ax; }
                }
                fistAxisL = A; fistFrontL = c + A * (capDepth * 0.3f); fistSideL = bestAx; fistWidthL = best; fistKnown = true;
                Debug.Log($"[RoomRun] eogeom: fist measured on {smr.name}: {pts.Count} verts, length {front - back:0.000}, width {best:0.000} (hand-local)");
                return;
            }
            Debug.LogWarning("[RoomRun] eogeom: could not measure the fist, fingers use the bone line");
        }
        void Finger(Vector3 b, Vector3 d, Vector3 side, float len, float w, float a)
        {
            // 4 rows along the finger (base, middle, near the tip, tip), 3 across (soft edge, bright core, soft edge)
            float[] u = { 0f, 0.4f, 0.78f, 1f }, wd = { 0.85f, 1f, 0.8f, 0.22f }, al = { 0.7f, 1f, 1f, 0.9f };
            int start = gv.Count;
            for (int i = 0; i < 4; i++)
            {
                var c = b + d * len * u[i]; var o = side * w * 0.5f * wd[i];
                var col = new Color(1, 1, 1, a * al[i]);
                gv.Add(c - o); guv.Add(new Vector2(0.06f, 0.25f)); gc.Add(col);
                gv.Add(c); guv.Add(new Vector2(0.02f, 1f)); gc.Add(col);
                gv.Add(c + o); guv.Add(new Vector2(0.06f, 0.25f)); gc.Add(col);
                if (i > 0)
                {
                    int p0 = start + (i - 1) * 3, p1 = start + i * 3;
                    gt.Add(p0); gt.Add(p1); gt.Add(p0 + 1); gt.Add(p0 + 1); gt.Add(p1); gt.Add(p1 + 1);
                    gt.Add(p0 + 1); gt.Add(p1 + 1); gt.Add(p0 + 2); gt.Add(p0 + 2); gt.Add(p1 + 1); gt.Add(p1 + 2);
                }
            }
        }
        readonly List<Vector3> gv = new List<Vector3>(8);
        readonly List<Vector2> guv = new List<Vector2>(8);
        readonly List<Color> gc = new List<Color>(8);
        static readonly int[] GlowTris = { 0, 1, 2, 2, 1, 3, 2, 3, 4, 4, 3, 5 };
        void DrawSign()
        {
            glow.Clear();
            if (!Glow || !lHand || !lFore) { UpdateTipTrail(0f); return; }
            float a = kind != 0 ? Mathf.Clamp01(t / 0.066f) * (1f - Mathf.InverseLerp(0.62f, 0.69f, ct)) : 0f;   // ~2 frames in, ~2 frames out after the recall
            for (int k = 0; k < plates && a < 0.35f; k++) if (st[k] != 0) a = 0.35f;   // stays faintly lit while the swords are out
            if (a <= 0.01f && !(Streak && kind != 0 && t - LaunchAt < StreakLife)) { UpdateTipTrail(0f); if (V15 && wrapTried) UpdateHandWrap(lHand.position, 0f); return; }
            var d = (lHand.position - lFore.position).normalized;
            var cam = RunDirector.Instance && RunDirector.Instance.Rig ? RunDirector.Instance.Rig.Cam : Camera.main;
            var view = cam ? (cam.transform.position - lHand.position).normalized : Vector3.up;
            var sideN = Vector3.Cross(d, view).normalized;
            float su = Vector3.Distance(lHand.position, lFore.position) / 0.203f;
            if (!fistTried) MeasureFist();
            var fistFront = lHand.position + d * (FistFront * su);
            var fingerSide = sideN;
            if (fistKnown)
            {
                fistFront = lHand.TransformPoint(fistFrontL);
                d = lHand.TransformDirection(fistAxisL).normalized;
                var fs = lHand.TransformDirection(fistSideL); fs -= d * Vector3.Dot(fs, d);
                // spread the two fingers across the fist; if that axis faces the camera they would overlap, so lean it toward the screen side
                fingerSide = Vector3.Slerp(fs.normalized, Vector3.Cross(d, view).normalized * Mathf.Sign(Vector3.Dot(fs, Vector3.Cross(d, view)) + 1e-4f), 0.35f).normalized;
                sideN = Vector3.Cross(d, view).normalized;
                if (V13)
                {
                    // straight out of the fist along the forearm line, side by side across the screen
                    d = (lHand.position - lFore.position).normalized;
                    // (the baked-mesh fist probe can come back degenerate, 0.001 m wide: then the fixed fist length is used)
                    fistFront = lHand.position + d * (fistWidthL > 0.02f ? Mathf.Max(0.02f * su, Vector3.Dot(lHand.TransformVector(fistFrontL), d)) : FistFront * su);
                    sideN = Vector3.Cross(d, view).normalized; fingerSide = sideN;
                }
            }
            gv.Clear(); guv.Clear(); gc.Clear(); gt.Clear();
            if (V15)
            {
                var dd = (lHand.position - lFore.position).normalized;
                UpdateHandWrap(lHand.position + dd * (FistFront * su * 0.45f), a);
                UpdateTipTrail(0f);
                goto streakPart;
            }
            if (a > 0.01f)
            {
                // faint knuckle glow at the front of the fist
                var kn = fistFront - d * 0.02f * su;
                var ko = sideN * 0.03f * su; var kd = d * 0.025f * su;
                int k0 = gv.Count; var kc = new Color(1, 1, 1, a * 0.35f);
                gv.Add(kn - kd - ko); gv.Add(kn - kd + ko); gv.Add(kn + kd - ko); gv.Add(kn + kd + ko);
                for (int i = 0; i < 4; i++) { guv.Add(new Vector2(0.3f, i < 2 ? 0.2f : 0.6f)); gc.Add(kc); }
                gt.Add(k0); gt.Add(k0 + 1); gt.Add(k0 + 2); gt.Add(k0 + 2); gt.Add(k0 + 1); gt.Add(k0 + 3);
                // index + middle, side by side, slightly apart; the middle finger a touch longer
                var b0 = fistFront - d * 0.012f * su;   // root the fingers just inside the knuckles
                Finger(b0 - fingerSide * FingerGap * 0.5f * su, d, sideN, FingerLen * su, FingerThick * su, a);
                Finger(b0 + fingerSide * FingerGap * 0.5f * su, d, sideN, FingerLen * 1.08f * su, FingerThick * su, a);
                tipPos = b0 + d * FingerLen * 1.04f * su; tipDir = d;
            }
            UpdateTipTrail(a);
            streakPart:
            // v3 streak: grows from the fist to StreakLen (or the target) in .035 s, then thins and fades
            float ts = t - LaunchAt;
            bool streak = Streak && kind != 0 && ts >= -0.01f && ts < StreakLife;
            if (streak)
            {
                var from = fistFront + d * FingerLen * su;
                var to = targetPt; to.y = Mathf.Lerp(from.y, to.y, 0.5f);
                var sd = to - from; float full = Mathf.Min(StreakLen, sd.magnitude); sd = sd.sqrMagnitude > 1e-4f ? sd.normalized : aim;
                float grow = Mathf.Clamp01((ts + 0.01f) / 0.035f), fade = 1f - Mathf.InverseLerp(0.04f, StreakLife, ts);
                float sa = fade;
                var head = from + sd * full * grow;
                var tail = from + sd * full * grow * Mathf.InverseLerp(0.02f, StreakLife, ts);
                var vw = cam ? (cam.transform.position - head).normalized : Vector3.up;
                var ss = Vector3.Cross(sd, vw).normalized * 0.028f * (0.6f + 0.4f * fade);
                var mid = Vector3.Lerp(tail, head, 0.7f);
                int s0 = gv.Count;
                void S(Vector3 p, float span) { gv.Add(p); guv.Add(new Vector2(0.05f, span)); gc.Add(new Color(1, 1, 1, sa)); }
                S(tail - ss * 0.3f, 0.2f); S(tail + ss * 0.3f, 0.2f);
                S(mid - ss, 1f); S(mid + ss, 1f);
                S(head - ss * 0.15f, 0.9f); S(head + ss * 0.15f, 0.9f);
                foreach (var ix in GlowTris) gt.Add(s0 + ix);
            }
            if (gt.Count == 0) return;
            glow.SetVertices(gv); glow.SetUVs(0, guv); glow.SetColors(gc); glow.SetTriangles(gt, 0, false);
            glow.RecalculateBounds();
        }
    }
}
