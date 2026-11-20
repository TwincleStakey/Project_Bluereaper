// 초가집 슬라임 (mid-boss) test driver: procedural moves on a body bone + two 7-bone arms, house carried as a prop.
// Body: one bone (squash/stretch, lean, travel); the jelly jiggle and impact ripples are in the shader.
// Arms: each bone is placed by blending two rigid frames - the body's and the house's - from shoulder (body) to tip
// (house), then re-aimed along the new chain, so the arms keep hugging the house wherever it goes and stretch like jelly.
// Moves: Idle (breathing), Walk (heave the house forward, body slumps after it), Lift (body pours off the roof, raises
// the house overhead, puts it back), Slam (same raise, then smashes the house down in front and climbs back on).
// Evaluate(t) is pure (pose from time), so edit-mode captures can scrub it; in Play the moves cycle by themselves.
using UnityEngine;

[ExecuteAlways]
public partial class ChogaSlimeBoss : MonoBehaviour
{
    public enum Move { Idle, Walk, Lift, Slam }
    public Move move = Move.Idle;
    public bool cycleInPlay = true;
    [Tooltip("0 = use 'move'; 1..10 = 그슨대 design-doc pattern (GeuseundaePatterns.cs)")]
    public int pattern = 0;
    public float time;

    public Transform house;
    public Transform bodyBone;
    public Transform[] armL = new Transform[0], armR = new Transform[0];
    public SkinnedMeshRenderer body;
    public Renderer[] eyes = new Renderer[0];

    // rest pose, captured once by the builder (boss-local space)
    [SerializeField, HideInInspector] bool restCaptured;
    [SerializeField, HideInInspector] Vector3 bodyRestPos, bodyRestScale, fwdL, rightL;
    [SerializeField, HideInInspector] Quaternion bodyRestRot;
    [SerializeField, HideInInspector] Matrix4x4 bodyRestM, houseRestM;
    [SerializeField, HideInInspector] Vector3 houseRestPos;
    [SerializeField, HideInInspector] Quaternion houseRestRot;
    [SerializeField, HideInInspector] Vector3[] restPosL, restPosR;
    [SerializeField, HideInInspector] Quaternion[] restRotL, restRotR;

    public static float Length(Move m) => m == Move.Idle ? 7.2f : m == Move.Walk ? 5.7f : m == Move.Lift ? 4.4f : 4.8f;
    public static float NetForward(Move m) => m == Move.Walk ? 3 * WalkStep : m == Move.Slam ? SlamForward : 0f;
    const float WalkCycle = 1.9f, WalkStep = 2.4f, SlamForward = 4.5f;

    struct Pose
    {
        public Vector3 bodyOff, houseOff;   // (right, up, forward) in boss space
        public float bodyPitch, bodyRoll, squash, housePitch, houseRoll, eyeGlow, rippleStart;
        // hand overrides (boss-local target for the hand bone, rotation relative to the rest grip, blend weight);
        // A = the arm on the boss's right (screen left when it faces the camera), B = the other
        public Vector3 handA, handB; public Quaternion rotA, rotB; public float wA, wB;
    }

    public void CaptureRest()
    {
        var T = transform;
        bodyRestPos = T.InverseTransformPoint(bodyBone.position);
        bodyRestRot = Quaternion.Inverse(T.rotation) * bodyBone.rotation;
        bodyRestScale = bodyBone.localScale;
        bodyRestM = T.worldToLocalMatrix * bodyBone.localToWorldMatrix;
        houseRestPos = T.InverseTransformPoint(house.position);
        houseRestRot = Quaternion.Inverse(T.rotation) * house.rotation;
        houseRestM = T.worldToLocalMatrix * house.localToWorldMatrix;
        restPosL = new Vector3[armL.Length]; restRotL = new Quaternion[armL.Length];
        restPosR = new Vector3[armR.Length]; restRotR = new Quaternion[armR.Length];
        for (int k = 0; k < armL.Length; k++) { restPosL[k] = T.InverseTransformPoint(armL[k].position); restRotL[k] = Quaternion.Inverse(T.rotation) * armL[k].rotation; }
        for (int k = 0; k < armR.Length; k++) { restPosR[k] = T.InverseTransformPoint(armR[k].position); restRotR[k] = Quaternion.Inverse(T.rotation) * armR[k].rotation; }
        // the face looks where the eyes are, seen from the body bone
        var eyeMid = Vector3.zero; foreach (var e in eyes) eyeMid += T.InverseTransformPoint(e.transform.position);
        eyeMid /= Mathf.Max(1, eyes.Length);
        var f = eyeMid - bodyRestPos; f.y = 0; fwdL = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        rightL = Vector3.Cross(Vector3.up, fwdL);
        restCaptured = true;
    }

    public Vector3 ForwardWorld => transform.TransformDirection(fwdL);

    void Update()
    {
        if (!restCaptured || bodyBone == null) return;
        if (Application.isPlaying && cycleInPlay)
        {
            time += Time.deltaTime;
            if (time >= Length(move))
            {
                transform.position += transform.TransformDirection(fwdL) * NetForward(move);
                time = 0; move = (Move)(((int)move + 1) % 4);
            }
        }
        if (Application.isPlaying) Evaluate(time);
    }

    // ---------------- pose ----------------
    static float S01(float a, float b, float x) { var t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
    static float Lin(float a, float b, float x) => Mathf.Clamp01((x - a) / (b - a));
    static float Wob(float t, float amp, float decay, float freq) => t < 0 ? 0 : amp * Mathf.Exp(-t * decay) * Mathf.Cos(t * freq);
    Vector3 V(float right, float up, float fwd) => rightL * right + Vector3.up * up + fwdL * fwd;

    Pose Idle(float t)
    {
        var p = new Pose { squash = 1, eyeGlow = 1, rippleStart = -100 };
        float b = Mathf.Sin(t * 2 * Mathf.PI / 3.6f);
        p.squash = 1 + 0.035f * b; p.bodyOff = V(0, 0.05f * b, 0);
        p.bodyRoll = 1.3f * Mathf.Sin(t * 2 * Mathf.PI / 5.3f); p.bodyPitch = 0.9f * Mathf.Sin(t * 2 * Mathf.PI / 4.1f + 1f);
        float ph = Mathf.Repeat(t, 3.6f); p.eyeGlow = 1 - 0.85f * (S01(2.45f, 2.55f, ph) - S01(2.6f, 2.75f, ph));   // a slow "blink": the embers dim
        return p;
    }

    Pose Walk(float t)
    {
        var p = new Pose { squash = 1, eyeGlow = 1.1f, rippleStart = -100 };
        int n = Mathf.FloorToInt(t / WalkCycle); float u = (t - n * WalkCycle) / WalkCycle;
        float hs = S01(0.32f, 0.58f, u);                                   // house heaved forward
        float lift = 0.75f * Mathf.Sin(Mathf.PI * Lin(0.32f, 0.58f, u));
        float land = (u - 0.58f) * WalkCycle;
        float bs = S01(0.42f, 0.86f, u); bs += 0.07f * Mathf.Sin(Mathf.PI * Lin(0.7f, 1f, u));   // body slumps after it, a little overshoot
        p.houseOff = V(0, lift + (u > 0.58f ? Wob(land, -0.12f, 9, 30) : 0), (n + hs) * WalkStep);
        p.housePitch = -4f * Mathf.Sin(Mathf.PI * Lin(0.32f, 0.58f, u));
        float squash = u < 0.32f ? 1 - 0.13f * S01(0f, 0.32f, u)
                     : u < 0.52f ? Mathf.Lerp(0.87f, 1.12f, S01(0.32f, 0.5f, u))
                     : u < 0.64f ? Mathf.Lerp(1.12f, 0.86f, S01(0.52f, 0.64f, u))
                     : 1 - 0.14f * Mathf.Exp(-(u - 0.64f) * WalkCycle * 6f) * Mathf.Cos((u - 0.64f) * WalkCycle * 22f);
        p.squash = squash;
        p.bodyPitch = -5f * S01(0, 0.32f, u) * (1 - S01(0.32f, 0.45f, u)) + 10f * Mathf.Sin(Mathf.PI * Lin(0.36f, 0.8f, u));
        p.bodyOff = V(0, 0.55f * Mathf.Sin(Mathf.PI * Lin(0.36f, 0.72f, u)), (n + bs) * WalkStep - (n + hs) * WalkStep * 0f);
        p.bodyRoll = 2.5f * Mathf.Sin(t * Mathf.PI / WalkCycle);
        p.rippleStart = (n + 0.58f) * WalkCycle;
        return p;
    }

    // shared opening of Lift and Slam: pour off the roof to the ground behind, raise the house overhead, tremble
    void Raise(float t, ref Pose p)
    {
        float a = S01(0f, 0.9f, t);
        float arc = Mathf.Sin(Mathf.PI * Lin(0f, 0.9f, t));
        p.bodyOff = V(0, -3.5f * a + 1.5f * arc, -5.6f * a);
        p.bodyPitch = -22f * arc - 4f * a;
        float landT = t - 0.9f;
        p.squash = t < 0.9f ? 1 + 0.1f * arc : 1;
        float r = S01(0.9f, 1.9f, t);
        if (t >= 0.9f) p.squash = Mathf.Lerp(0.8f, 1.18f, S01(0.95f, 1.8f, t)) + Wob(landT, -0.06f, 6, 20);
        p.bodyOff += V(0, 0.8f * r, 0);
        p.houseOff = V(0, 7.0f * r, -1.6f * r);
        p.housePitch = -16f * r; p.houseRoll = 3f * Mathf.Sin(t * 1.7f) * r;
        float tr = S01(1.85f, 2.0f, t);
        p.houseOff += V(0.1f * Mathf.Sin(t * 41f), 0.12f * Mathf.Sin(t * 53f + 1f), 0.08f * Mathf.Sin(t * 37f)) * tr;
        p.eyeGlow = 1 + 2.2f * S01(1.6f, 2.3f, t);
    }

    Pose Lift(float t)
    {
        var p = new Pose { squash = 1, eyeGlow = 1, rippleStart = -100 };
        Raise(Mathf.Min(t, 2.4f), ref p);
        if (t > 2.4f)
        {
            float d = S01(2.4f, 3.3f, t);
            p.houseOff = Vector3.Lerp(p.houseOff, Vector3.zero, d) + V(0, Wob(t - 3.3f, -0.1f, 8, 26), 0);
            p.housePitch *= 1 - d; p.houseRoll *= 1 - d; p.eyeGlow = Mathf.Lerp(p.eyeGlow, 1, d);
            float c = S01(3.3f, 4.25f, t), arc = Mathf.Sin(Mathf.PI * Lin(3.3f, 4.25f, t));
            var from = V(0, -3.5f + 0.8f, -5.6f);
            p.bodyOff = Vector3.Lerp(from, Vector3.zero, c) + V(0, 2.2f * arc, 0);
            p.bodyPitch = Mathf.Lerp(-4f, 0f, c) + 14f * arc;
            p.squash = Mathf.Lerp(1.18f, 1f, d) * (1 + 0.1f * arc) + Wob(t - 4.25f, -0.1f, 6, 20);
            p.rippleStart = 3.3f;
        }
        return p;
    }

    Pose Slam(float t)
    {
        var p = new Pose { squash = 1, eyeGlow = 1, rippleStart = -100 };
        Raise(Mathf.Min(t, 2.4f), ref p);
        if (t > 2.4f)
        {
            float w = Lin(2.4f, 2.65f, t); float w2 = w * w;
            var hFrom = V(0, 7.0f, -1.6f);
            var hTo = V(0, -0.3f, SlamForward);
            p.houseOff = Vector3.Lerp(hFrom, hTo, w2) + V(0, 1.6f * Mathf.Sin(Mathf.PI * w) * (1 - w), 0);
            p.housePitch = Mathf.Lerp(-16f, 7f, w2); p.houseRoll = 0;
            float bodyLunge = S01(2.4f, 2.7f, t);
            var bFrom = V(0, -3.5f + 0.8f, -5.6f);
            var bLunge = V(0, -3.6f, -3.2f);
            p.bodyOff = Vector3.Lerp(bFrom, bLunge, bodyLunge);
            p.bodyPitch = Mathf.Lerp(-4f, 18f, bodyLunge);
            p.squash = Mathf.Lerp(1.18f, 1.05f, w);
            p.eyeGlow = Mathf.Lerp(3.2f, 2f, w);
            if (t > 2.65f)
            {
                float k = t - 2.65f;
                p.houseOff = hTo + V(0, Wob(k, -0.25f, 7, 24) + 0.3f * S01(0, 0.6f, k), 0);
                p.housePitch = 7f * Mathf.Exp(-k * 6) * Mathf.Cos(k * 20);
                p.squash = 1 - 0.32f * Mathf.Exp(-k * 4.5f) * Mathf.Cos(k * 16f);
                p.eyeGlow = Mathf.Lerp(2f, 1f, S01(0, 1.2f, k));
                p.rippleStart = 2.65f;
                // climb back on top of the house where it landed
                float c = S01(3.4f, 4.6f, t), arc = Mathf.Sin(Mathf.PI * Lin(3.4f, 4.6f, t));
                p.bodyOff = Vector3.Lerp(bLunge, V(0, 0, SlamForward), c) + V(0, 2.4f * arc, 0);
                p.bodyPitch = Mathf.Lerp(Mathf.Lerp(18f, 2f, S01(2.65f, 3.3f, t)), 0f, c) + 12f * arc;
                p.squash *= 1 + 0.1f * arc;
                p.squash += Wob(t - 4.6f, -0.1f, 6, 20);
                p.houseOff = Vector3.Lerp(p.houseOff, V(0, 0, SlamForward), S01(3.3f, 4.0f, t));
                if (t > 4.6f) p.rippleStart = 4.6f;
            }
        }
        return p;
    }

    public void Evaluate(float t)
    {
        if (!restCaptured || bodyBone == null) return;
        time = t;
        if (pattern > 0) { var pp = PatternPose(pattern, t); SpringPose(ref pp, t); Apply(pp, t); PatternFX(pattern, t); return; }
        FxBegin(); FxEnd();
        Pose p = move == Move.Idle ? Idle(t) : move == Move.Walk ? Walk(t) : move == Move.Lift ? Lift(t) : Slam(t);
        // idle breathing keeps going under every move
        if (move != Move.Idle) { var i = Idle(t); p.squash *= 1 + (i.squash - 1) * 0.5f; }
        Apply(p, t);
    }

    // rubber arms + jelly body: the hands and the body lean/squash chase their keyed targets through damped springs, so a
    // wind-up overshoots backwards, a stop wobbles and a slam bounces. Re-simulated from t=0 each call so any frame can be
    // scrubbed (patterns are a few seconds; 60 Hz steps are cheap).
    void SpringPose(ref Pose p, float t)
    {
        const float dt = 1f / 60f;
        float wH = 2 * Mathf.PI * 2.6f, zH = 0.42f, wB = 2 * Mathf.PI * 1.8f, zB = 0.32f;
        var p0 = PatternPose(pattern, 0);
        Vector3 xa = p0.wA > 0 ? p0.handA : HandRest(true), va = Vector3.zero, xb = p0.wB > 0 ? p0.handB : HandRest(false), vb = Vector3.zero;
        float pitch = p0.bodyPitch, vp = 0, sq = p0.squash, vs = 0, roll = p0.bodyRoll, vr = 0;
        int steps = Mathf.CeilToInt(t / dt);
        for (int i = 1; i <= steps; i++)
        {
            var q = PatternPose(pattern, Mathf.Min(t, i * dt));
            var ta = q.wA > 0 ? Vector3.Lerp(HandRest(true), q.handA, q.wA) : HandRest(true); var tb = q.wB > 0 ? Vector3.Lerp(HandRest(false), q.handB, q.wB) : HandRest(false);
            va += (wH * wH * (ta - xa) - 2 * zH * wH * va) * dt; xa += va * dt;
            vb += (wH * wH * (tb - xb) - 2 * zH * wH * vb) * dt; xb += vb * dt;
            vp += (wB * wB * (q.bodyPitch - pitch) - 2 * zB * wB * vp) * dt; pitch += vp * dt;
            vr += (wB * wB * (q.bodyRoll - roll) - 2 * zB * wB * vr) * dt; roll += vr * dt;
            vs += (wB * wB * (q.squash - sq) - 2 * zB * wB * vs) * dt; sq += vs * dt;
        }
        xa.y = Mathf.Max(xa.y, 0.3f); xb.y = Mathf.Max(xb.y, 0.3f);
        // the spring already carries the blend toward the rest grip, so drive the hands fully while anything moves them
        if (p.wA > 0) { p.handA = xa; p.wA = 1; } if (p.wB > 0) { p.handB = xb; p.wB = 1; }
        p.bodyPitch = pitch; p.bodyRoll = roll; p.squash = sq;
    }

    void Apply(Pose p, float t)
    {
        var T = transform;
        bodyBone.position = T.TransformPoint(bodyRestPos + p.bodyOff);
        bodyBone.rotation = T.rotation * Quaternion.AngleAxis(p.bodyPitch, rightL) * Quaternion.AngleAxis(p.bodyRoll, fwdL) * bodyRestRot;
        float s = Mathf.Max(0.3f, p.squash), xz = 1f / Mathf.Sqrt(s);
        bodyBone.localScale = Vector3.Scale(bodyRestScale, new Vector3(xz, s, xz));
        house.position = T.TransformPoint(houseRestPos + p.houseOff);
        house.rotation = T.rotation * Quaternion.AngleAxis(p.housePitch, rightL) * Quaternion.AngleAxis(p.houseRoll, fwdL) * houseRestRot;

        var bodyM = bodyBone.localToWorldMatrix * bodyRestM.inverse;      // boss-local rest -> world, following the body
        var houseM = house.localToWorldMatrix * houseRestM.inverse;       // ... following the house
        var bodyQ = bodyBone.rotation * Quaternion.Inverse(T.rotation * bodyRestRot);
        var houseQ = house.rotation * Quaternion.Inverse(T.rotation * houseRestRot);
        bool lIsA = ArmLIsA();
        if (p.rotA == default) p.rotA = Quaternion.identity; if (p.rotB == default) p.rotB = Quaternion.identity;
        PlaceArm(armL, restPosL, restRotL, bodyM, houseM, bodyQ, houseQ, t, 0f, lIsA ? p.handA : p.handB, lIsA ? p.rotA : p.rotB, lIsA ? p.wA : p.wB);
        PlaceArm(armR, restPosR, restRotR, bodyM, houseM, bodyQ, houseQ, t, 1.7f, lIsA ? p.handB : p.handA, lIsA ? p.rotB : p.rotA, lIsA ? p.wB : p.wA);

        if (body != null)
        {
            var mpb = new MaterialPropertyBlock(); body.GetPropertyBlock(mpb);
            mpb.SetFloat("_RippleStart", p.rippleStart);
            var origin = bodyBone.position;   // ring spreads from the base of the mass
            mpb.SetVector("_RippleOrigin", origin);
            body.SetPropertyBlock(mpb);
        }
        foreach (var e in eyes)
        {
            if (e == null) continue;
            var mpb = new MaterialPropertyBlock(); e.GetPropertyBlock(mpb); mpb.SetFloat("_Glow", p.eyeGlow); e.SetPropertyBlock(mpb);
        }
        Shader.SetGlobalFloat("_CS_Time", t);
    }

    void PlaceArm(Transform[] arm, Vector3[] rp, Quaternion[] rr, Matrix4x4 bodyM, Matrix4x4 houseM, Quaternion bodyQ, Quaternion houseQ, float t, float phase,
                  Vector3 handLocal, Quaternion handRot, float handW)
    {
        int n = arm.Length; if (n == 0 || rp == null || rp.Length != n) return;
        var T = transform;
        var pos = new Vector3[n];
        // shoulder (bone 0) rides the body, the hand (bones H..) the house. The free bones between them sit on a smooth
        // curve whose start tangent turns toward the hand, so a big reach (house overhead) bends evenly over every joint
        // instead of folding at one joint (linear skinning collapses a 150-degree fold into a flat ribbon).
        int H = Mathf.Min(4, n - 1);
        pos[0] = bodyM.MultiplyPoint3x4(rp[0]);
        handW = Mathf.Clamp01(handW);
        for (int k = H; k < n; k++)
        {
            pos[k] = houseM.MultiplyPoint3x4(rp[k]);
            if (handW > 0) pos[k] = Vector3.Lerp(pos[k], T.TransformPoint(handLocal + handRot * (rp[k] - rp[H])), handW);
        }
        var handQ = Quaternion.Slerp(houseQ, T.rotation * handRot * Quaternion.Inverse(T.rotation), handW);
        var p0 = pos[0]; var p3 = pos[H]; float d = Vector3.Distance(p0, p3);
        var t0 = (bodyQ * T.TransformDirection(rp[1] - rp[0])).normalized;
        var t3 = (handQ * T.TransformDirection(rp[H] - rp[H - 1])).normalized;
        var toHand = d > 1e-4f ? (p3 - p0) / d : t0;
        var st = Vector3.Slerp(t0, toHand, 0.45f);
        var c1 = p0 + st * d * 0.38f; var c2 = p3 - t3 * d * 0.38f;
        var cum = new float[H + 1]; for (int k = 1; k <= H; k++) cum[k] = cum[k - 1] + Vector3.Distance(rp[k], rp[k - 1]);
        const int S = 48; var bp = new Vector3[S + 1]; var bl = new float[S + 1];
        for (int i = 0; i <= S; i++)
        {
            float u = i / (float)S, v = 1 - u;
            bp[i] = p0 * (v * v * v) + c1 * (3 * v * v * u) + c2 * (3 * v * u * u) + p3 * (u * u * u);
            if (i > 0) bl[i] = bl[i - 1] + Vector3.Distance(bp[i], bp[i - 1]);
        }
        for (int k = 1; k < H; k++)
        {
            float target = cum[k] / cum[H] * bl[S]; int i = 1; while (i < S && bl[i] < target) i++;
            float f = Mathf.InverseLerp(bl[i - 1], bl[i], target);
            pos[k] = Vector3.Lerp(bp[i - 1], bp[i], f);
            // a slow ripple travels down the free part of the arm
            float free = Mathf.Sin(Mathf.PI * k / H);
            pos[k] += (T.TransformDirection(rightL) * Mathf.Sin(t * 1.9f + k * 0.9f + phase) + Vector3.up * Mathf.Sin(t * 1.3f + k * 0.7f + phase)) * 0.12f * free;
        }
        // rotations by parallel transport from the shoulder: each bone keeps its rest twist relative to the previous one and
        // only bends (minimal rotation) to follow the new chain, so a long stretch never wrings the arm into a flat ribbon
        var prev = Quaternion.identity;
        for (int k = 0; k < n; k++)
        {
            int k0 = Mathf.Min(k, n - 2);
            var restW = T.rotation * rr[k];
            var localDir = Quaternion.Inverse(restW) * T.TransformDirection(rp[k0 + 1] - rp[k0]);
            var q = k == 0 ? bodyQ * restW : prev * (Quaternion.Inverse(rr[k - 1]) * rr[k]);
            var want = pos[k0 + 1] - pos[k0];
            if (want.sqrMagnitude > 1e-6f) q = Quaternion.FromToRotation(q * localDir, want) * q;
            arm[k].SetPositionAndRotation(pos[k], q);
            prev = q;
        }
    }
}
