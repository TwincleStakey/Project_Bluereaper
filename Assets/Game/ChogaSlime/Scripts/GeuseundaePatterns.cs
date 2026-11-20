// 어둠의 그슨대 (design doc 1.3, 11-18): previs of the 10 patterns on the 초가집 boss rig.
// Each pattern is a pure function of time: Pn(t, false) gives the pose (hand targets, body lean/squash, eyes),
// Pn(t, true) draws the marks (telegraphs, cracks, bullets, wisps, gourds) with an immediate-mode pool, so edit-mode
// captures can scrub any frame. Coordinates: S(x, up, fwd) in metres, x = screen right when the boss faces the camera,
// fwd = out of the house front toward the alley (players stand at fwd 13-16).
using System.Collections.Generic;
using UnityEngine;

public partial class ChogaSlimeBoss
{
    public Transform[] players = new Transform[0];
    public Transform[] roofGourds = new Transform[0];
    public Material fxMat, fxMatAlpha, gourdMat, gourdStemMat;
    public Mesh gourdMesh;
    public float gourdScale = 0.8f;
    public Camera fxCamera;
    [SerializeField, HideInInspector] Vector3[] playerRest = new Vector3[0];
    [SerializeField, HideInInspector] Vector3[] gourdRest = new Vector3[0];
    [SerializeField, HideInInspector] Quaternion[] gourdRestRot = new Quaternion[0];

    public static readonly string[] PatternNames = { "", "연귀의 귀수신축", "흉박투척", "파옥분쇄", "염령의 부유흉박", "협항압살",
        "[협동 1] 옹살형", "[협동 2] 명화낙인", "파포비산", "벽영귀화", "식영귀화" };
    static readonly float[] PatLen = { 0f, 4.2f, 4.6f, 5.2f, 6.0f, 4.6f, 6.8f, 5.2f, 5.0f, 6.4f, 5.4f };
    public static float PatternLength(int p) => PatLen[Mathf.Clamp(p, 0, 10)];

    static readonly Color Red = new Color(2.2f, 0.09f, 0.04f), DarkRed = new Color(0.9f, 0.03f, 0.02f), Blood = new Color(1.4f, 0.02f, 0.05f),
        Blue = new Color(0.35f, 0.9f, 3.2f), Yellow = new Color(2.6f, 1.7f, 0.15f), Dust = new Color(0.22f, 0.2f, 0.18f), White = new Color(2.5f, 2.2f, 2f);

    public void CapturePatternRest()
    {
        var T = transform;
        playerRest = new Vector3[players.Length];
        for (int i = 0; i < players.Length; i++) playerRest[i] = T.InverseTransformPoint(players[i].position);
        gourdRest = new Vector3[roofGourds.Length]; gourdRestRot = new Quaternion[roofGourds.Length];
        for (int i = 0; i < roofGourds.Length; i++) { gourdRest[i] = T.InverseTransformPoint(roofGourds[i].position); gourdRestRot[i] = Quaternion.Inverse(T.rotation) * roofGourds[i].rotation; }
    }

    // ---------------- helpers ----------------
    public Vector3 ScreenToWorld(float x, float up, float fwd) => transform.TransformPoint(S(x, up, fwd));
    // Blender model space (house front = -Y, Z up) -> world, using the rig's own left/right as the reference
    public Vector3 BlenderToWorld(Vector3 b) { float sg = ArmLIsA() ? -1f : 1f; return transform.TransformPoint(rightL * (b.x * sg) + Vector3.up * b.z + fwdL * (-b.y)); }
    Vector3 S(float x, float up, float fwd) => V(-x, up, fwd);
    bool ArmLIsA() => restPosL != null && restPosL.Length > 4 && Vector3.Dot(restPosL[4], rightL) > 0;
    Vector3 HandRest(bool a) { var rp = (a == ArmLIsA()) ? restPosL : restPosR; return rp[Mathf.Min(4, rp.Length - 1)]; }
    static float J(float t, float seed) => Mathf.PerlinNoise(t * 9f, seed * 7.3f) * 2f - 1f;
    static float EaseIn(float u) => u * u;
    Vector3 PlayerRest(int i) => i < playerRest.Length ? playerRest[i] : S(i * 4 - 4, 0, 14);
    Vector3 GourdRest(int i) => i < gourdRest.Length ? gourdRest[i] : S(i - 2, 4.4f, 2.2f);
    Vector3 HeadLocal()
    {
        var m = Vector3.zero; foreach (var e in eyes) m += transform.InverseTransformPoint(e.transform.position);
        return eyes.Length > 0 ? m / eyes.Length : S(0, 11, 0);
    }
    Vector3 Ground(Vector3 v) { v.y = 0; return v; }

    Pose Base(float t)
    {
        var p = Idle(t); p.eyeGlow = 1; p.rippleStart = -100; p.rotA = p.rotB = Quaternion.identity; return p;
    }

    // ---------------- dispatch ----------------
    Pose PatternPose(int n, float t) => Run(n, t, false);
    void PatternFX(int n, float t)
    {
        FxBegin();
        for (int i = 0; i < players.Length; i++) if (players[i] != null) { players[i].position = transform.TransformPoint(PlayerRest(i)); Tint(i, Color.clear); }
        for (int i = 0; i < roofGourds.Length; i++) ShowRoofGourd(i, true);
        Run(n, t, true);
        FxEnd();
    }
    Pose Run(int n, float t, bool fx)
    {
        switch (n)
        {
            case 1: return P1(t, fx); case 2: return P2(t, fx); case 3: return P3(t, fx); case 4: return P4(t, fx); case 5: return P5(t, fx);
            case 6: return P6(t, fx); case 7: return P7(t, fx); case 8: return P8(t, fx); case 9: return P9(t, fx); default: return P10(t, fx);
        }
    }

    // ---------------- 1. 연귀의 귀수신축: pull back, half-beat freeze with a shiver, 15 m sweep right -> left ----------------
    Vector3 SweepArc(float u) => S(Mathf.Lerp(11.5f, -11.5f, u), 0.7f, 8f + 5.5f * Mathf.Sin(Mathf.PI * u));
    Pose P1(float t, bool fx)
    {
        var p = Base(t);
        float wind = S01(0.35f, 1.1f, t), back = 1 - S01(2.5f, 3.5f, t);
        float shiver = S01(1.05f, 1.2f, t) * (1 - S01(1.66f, 1.7f, t));
        var pulled = S(13.5f, 6.5f, -2.5f) + S(J(t, 1), J(t, 2), J(t, 3) * 0.6f) * 0.45f * shiver;
        float u = Lin(1.7f, 2.02f, t); float ue = u * u * (3 - 2 * u);
        if (t < 1.7f) { p.handB = Vector3.Lerp(HandRest(false), pulled, wind); p.wB = 1; if (wind <= 0) p.wB = 0; }
        else { p.handB = SweepArc(ue); p.wB = back; p.rotB = Quaternion.AngleAxis(-40f * Mathf.Sin(Mathf.PI * ue), Vector3.up); }
        p.bodyRoll = -9f * wind * (1 - u) + 12f * Mathf.Sin(Mathf.PI * Mathf.Min(1, u * 1.3f)) * back;
        p.bodyPitch = -4f * wind + 8f * ue * back;
        p.squash *= 1 + 0.05f * wind - 0.08f * Mathf.Sin(Mathf.PI * u);
        p.eyeGlow = 1 + 1.6f * shiver + 1.2f * Mathf.Sin(Mathf.PI * u);
        if (fx)
        {
            if (t > 1.7f)
            {   // the swept band on the alley floor
                float fade = 1 - S01(2.4f, 3.3f, t); int seg = 18; float uu = Mathf.Min(1, ue);
                for (int i = 0; i < seg; i++)
                {
                    float a = uu * i / seg, b = uu * (i + 1) / seg;
                    Line(Ground(SweepArc(a)), Ground(SweepArc(b)), 3.2f, Blood * fade, 6, 1, 0, true);
                    Line(Ground(SweepArc(a)), Ground(SweepArc(b)), 0.6f, Red * fade, 6, 1, 0.3f, true);
                }
                if (u < 1) Decal(Ground(SweepArc(ue)), 4, 4, 0, Dust * 3f, 1);
                for (int i = 0; i < 6; i++) { float ui = i / 6f, ti = 1.7f + 0.32f * ui; if (t > ti && ue >= ui) Smoke(Ground(SweepArc(ui)), t - ti, 2.2f, 3, 11 + i); }
                if (t > 1.85f) Debris(Ground(SweepArc(0.5f)), t - 1.85f, 10, 21, 9f, Soil, 0.35f);
            }
            if (shiver > 0) Orb(HeadLocal(), 0.9f + 0.1f * J(t, 5), Red * 0.25f * shiver);
        }
        return p;
    }

    // ---------------- 2. 흉박투척: tear a gourd off the eave, hurl it at the aggro player, burst into a ring of seeds ----------------
    Pose P2(float t, bool fx)
    {
        var p = Base(t);
        var g = GourdRest(0); var tgt = Ground(PlayerRest(2));
        var over = S(-4f, 10.5f, -0.8f); var rel = S(-1.5f, 8f, 5.5f);
        Vector3 h;
        if (t < 0.9f) h = Vector3.Lerp(HandRest(true), g + Vector3.up * 0.7f, S01(0.3f, 0.85f, t));
        else if (t < 1.6f) h = Vector3.Lerp(g + Vector3.up * 0.7f, over, S01(0.9f, 1.55f, t)) + S(J(t, 4), J(t, 5), 0) * 0.15f * S01(1.3f, 1.55f, t);
        else if (t < 1.85f) h = Vector3.Lerp(over, rel, EaseIn(Lin(1.6f, 1.85f, t)));
        else h = Vector3.Lerp(rel, HandRest(true), S01(2.2f, 3.2f, t));
        p.handA = h; p.wA = S01(0.3f, 0.6f, t) * (1 - S01(2.6f, 3.2f, t));
        p.bodyPitch = -7f * S01(0.9f, 1.6f, t) * (1 - Lin(1.6f, 1.85f, t)) + 9f * Mathf.Sin(Mathf.PI * Lin(1.6f, 2.4f, t));
        p.bodyRoll = 6f * S01(0.9f, 1.6f, t) * (1 - S01(1.7f, 2.3f, t));
        if (fx)
        {
            ShowRoofGourd(0, t < 0.9f || t > 4.2f);
            const float rel0 = 1.82f, hit = 2.62f;
            if (t >= 0.9f && t < rel0) { GourdAt(h + Vector3.down * 0.95f, Quaternion.Euler(0, t * 90, 0), Red * 0.45f); Orb(h + Vector3.down * 0.95f, 1.0f, Red * 0.12f); }
            if (t >= rel0 && t < hit)
            {
                float u = Lin(rel0, hit, t); var a = Vector3.Lerp(rel, tgt + Vector3.up * 0.6f, u) + Vector3.up * (7f * 4 * u * (1 - u));
                GourdAt(a, Quaternion.Euler(t * 500, 0, t * 210), Red * 0.6f); Orb(a, 1.0f, Red * 0.15f);
            }
            if (t > 1.0f && t < hit + 0.15f) Decal(tgt, 6, 6, 0, Red, 0, Lin(1.0f, hit, t), t > hit ? 1 : 0);
            if (t >= hit) { Burst(tgt, t - hit, 12, 9.5f, 1.3f); Debris(tgt, t - hit, 14, 3, 10f, Shard, 0.4f); Smoke(tgt, t - hit, 3f, 6, 5); }
        }
        return p;
    }

    // ---------------- 3. 파옥분쇄: both arms up, hold 1 s, slam the yard; 1 s later three quake lines rip down the alley ----------------
    static readonly float[] CrackAng = { -26f, 0f, 26f };
    Vector3 CrackPt(int i, float d) { float a = CrackAng[i] * Mathf.Deg2Rad; return S(Mathf.Sin(a) * d, 0, 7.5f + Mathf.Cos(a) * d); }
    Pose P3(float t, bool fx)
    {
        var p = Base(t);
        float up = S01(0.2f, 1.0f, t), slam = EaseIn(Lin(2.0f, 2.2f, t)), ret = S01(3.5f, 4.6f, t);
        var tr = S(J(t, 7), J(t, 8), 0) * 0.18f * S01(1.0f, 1.2f, t) * (1 - slam);
        for (int s = 0; s < 2; s++)
        {
            float sx = s == 0 ? -1 : 1;
            var hi = S(4.8f * sx, 13.5f, 0.2f) + tr; var lo = S(3.4f * sx, 0.5f, 6.5f);
            var h = Vector3.Lerp(Vector3.Lerp(HandRest(s == 0), hi, up), lo, slam);
            float w = up * (1 - ret);
            if (s == 0) { p.handA = h; p.wA = w; } else { p.handB = h; p.wB = w; }
        }
        p.bodyPitch = -7f * up * (1 - slam) + 15f * slam * (1 - ret);
        p.squash *= (1 + 0.1f * up * (1 - slam)) * (t > 2.2f ? 1 - 0.18f * Mathf.Exp(-(t - 2.2f) * 5) * Mathf.Cos((t - 2.2f) * 18) : 1);
        p.rippleStart = 2.2f;
        p.eyeGlow = 1 + 1.5f * up * (1 - ret);
        if (fx)
        {
            var yard = S(0, 0, 7f);
            if (t > 2.2f)
            {
                float k = t - 2.2f; Decal(yard, 9 + 10 * k, 9 + 10 * k, 0, White * Mathf.Exp(-k * 8), 1);
                Smoke(yard, k, 6.5f, 14, 31); Debris(yard, k, 26, 32, 12f, Soil, 0.5f);
                Straw(S(0, 4.8f, 1.2f), k, 18, 33, 3.5f);   // thatch shaken off the roof
            }
            float fade = 1 - S01(4.4f, 5.0f, t);
            if (t > 2.25f)
                for (int i = 0; i < 3; i++)
                {
                    LineA(CrackPt(i, 0), CrackPt(i, 24), 2.2f, Scorch * fade, 3, Lin(2.25f, 3.2f, t));
                    Line(CrackPt(i, 0), CrackPt(i, 24), 1.1f, Blood * fade, 3, Lin(2.25f, 3.2f, t), 0, true);
                    Line(CrackPt(i, 0), CrackPt(i, 24), 0.3f, Red * fade, 3, Lin(2.25f, 3.2f, t), 0.4f, true);
                }
            if (t > 3.2f && t < 4.5f)
            {
                float d = 24f * Lin(3.2f, 4.2f, t);
                for (int i = 0; i < 3; i++)
                {
                    var c = CrackPt(i, d); float e = 1 - S01(4.1f, 4.5f, t);
                    Decal(c, 4.5f, 4.5f, 0, Red * e, 1);
                    for (int k = 0; k < 3; k++)
                    {   // dark spikes punching up behind the wave front
                        var b = CrackPt(i, Mathf.Max(0, d - 1.2f - k * 1.6f)) + S((k - 1) * 0.5f, 0, 0);
                        float hgt = (2.2f - k * 0.5f) * e;
                        Orb(b + Vector3.up * hgt * 0.5f, 0.35f, DarkRed * e, 0, new Vector3(0.7f, hgt, 0.7f));
                    }
                    for (int k = 0; k < 4; k++)
                    {   // earth thrown up as the wave passes each quarter of the line
                        float dk = 5f + 5f * k, tk = 3.2f + dk / 24f;
                        if (t > tk) { Debris(CrackPt(i, dk), t - tk, 6, 40 + i * 7 + k, 7f, Soil, 0.4f); Smoke(CrackPt(i, dk), t - tk, 2f, 2, 50 + i * 7 + k); }
                    }
                }
            }
        }
        return p;
    }

    // ---------------- 4. 염령의 부유흉박: five gourds float off the roof and out of frame, then rain on red circles ----------------
    static readonly Vector2[] RainSpots = { new Vector2(-5f, 10f), new Vector2(3.5f, 12.5f), new Vector2(-1f, 16.5f), new Vector2(5.5f, 19f), new Vector2(-4.5f, 22f) };
    Pose P4(float t, bool fx)
    {
        var p = Base(t);
        float lift = S01(0.3f, 1.0f, t) * (1 - S01(3.6f, 4.6f, t));
        p.handA = S(-6f, 8.5f, 0.8f) + S(0, Mathf.Sin(t * 2.1f), 0) * 0.3f; p.wA = lift * 0.85f;
        p.handB = S(6f, 8.5f, 0.8f) + S(0, Mathf.Sin(t * 2.1f + 1), 0) * 0.3f; p.wB = lift * 0.85f;
        p.rotA = Quaternion.AngleAxis(-60 * lift, rightL); p.rotB = p.rotA;
        p.bodyPitch = -9f * lift; p.eyeGlow = 1 + 2.4f * lift;
        if (fx)
        {
            for (int i = 0; i < 5; i++)
            {
                float t0 = 0.5f + 0.13f * i, td = 2.8f + 0.38f * i;
                var tgt = S(RainSpots[i].x, 0, RainSpots[i].y);
                ShowRoofGourd(i, t < t0 || t > 5.6f);
                if (t >= t0 && t < t0 + 1.3f)
                {
                    float u = Lin(t0, t0 + 1.3f, t);
                    var a = GourdRest(i) + Vector3.up * (32f * u * u + 1.2f * Mathf.Sin(u * 3)) + S(Mathf.Sin(t * 3 + i) * 0.3f, 0, 0);
                    GourdAt(a, Quaternion.Euler(t * 60 * (i + 1), t * 90, 0), Red * (0.25f + 0.5f * u)); Orb(a, 1.0f, Red * 0.12f);
                    if (u < 0.4f) Orb(a, 1.0f, Red * 0.12f);
                }
                if (t > 1.8f && t < td + 0.1f) Decal(tgt, 5, 5, 0, Red, 0, Lin(1.8f, td, t), t > td - 0.05f ? 1 : 0);
                if (t > td - 0.38f && t < td)
                {
                    float u = Lin(td - 0.38f, td, t);
                    GourdAt(tgt + Vector3.up * (0.7f + 26f * (1 - u * u)), Quaternion.Euler(t * 300, 0, t * 170), Red * 0.7f); Orb(tgt + Vector3.up * (0.7f + 26f * (1 - u * u)), 1.0f, Red * 0.15f);
                }
                if (t >= td) { Burst(tgt, t - td, 0, 0, 0.9f, 2.5f); Debris(tgt, t - td, 10, 60 + i, 8f, Shard, 0.35f); Smoke(tgt, t - td, 2.6f, 5, 70 + i); }
            }
        }
        return p;
    }

    // ---------------- 5. 협항압살: arms stretch to the screen edges, one of three lanes lights, clap ----------------
    static readonly float[] Lanes = { 8.5f, 15f, 21.5f };
    Pose P5(float t, bool fx)
    {
        var p = Base(t);
        float lane = Lanes[1];
        float reach = S01(0.3f, 1.3f, t), clap = EaseIn(Lin(2.2f, 2.36f, t)), ret = S01(2.9f, 3.9f, t);
        var shiv = S(0, J(t, 9), J(t, 10)) * 0.25f * S01(1.3f, 1.5f, t) * (1 - clap);
        for (int s = 0; s < 2; s++)
        {
            float sx = s == 0 ? -1 : 1;
            var edge = S(19.5f * sx, 1.8f, lane) + shiv; var mid = S(2.6f * sx, 1.8f, lane);
            var h = Vector3.Lerp(Vector3.Lerp(HandRest(s == 0), edge, reach), mid, clap);
            if (s == 0) { p.handA = h; p.wA = reach * (1 - ret); p.rotA = Quaternion.AngleAxis(90 * reach, fwdL); }
            else { p.handB = h; p.wB = reach * (1 - ret); p.rotB = Quaternion.AngleAxis(-90 * reach, fwdL); }
        }
        p.bodyPitch = 7f * reach * (1 - ret); p.squash *= 1 - 0.06f * reach * (1 - ret);
        p.eyeGlow = 1 + 1.2f * reach * (1 - ret);
        if (fx)
        {
            if (t > 0.8f && t < 2.5f)
                for (int i = 0; i < 3; i++)
                {
                    bool on = i == 1;
                    Decal(S(0, 0, Lanes[i]), 17.5f, 5.6f, 0, on ? Red * 0.45f : DarkRed * 0.3f, 2, on ? Lin(0.9f, 2.36f, t) : 0, on && t > 2.3f ? 0.6f : 0);
                }
            if (t > 2.36f)
            {
                float k = t - 2.36f;
                Decal(S(0, 0, lane), 8 + 20 * k, 4, 0, Red * Mathf.Exp(-k * 6), 1);
                Decal(S(0, 0, lane), 10 + 8 * k, 10 + 8 * k, 0, Dust * 3 * Mathf.Exp(-k * 3), 1);
                Orb(S(0, 1.8f, lane), 1.4f + k * 3, White * 0.6f * Mathf.Exp(-k * 9));
                for (int i = 0; i < 7; i++) Smoke(S(-8f + i * 2.7f, 0, lane), k, 2.4f, 2, 80 + i);
                Debris(S(0, 1.0f, lane), k, 16, 90, 11f, Soil, 0.4f);
            }
        }
        return p;
    }

    // ---------------- 6. [협동 1] 옹살형: one hand snatches a player and hangs them from the eave; the other hand rests in the
    // yard with a yellow break gauge. The two others shoot the hand -> released, boss groggy 2 s ----------------
    Pose P6(float t, bool fx)
    {
        var p = Base(t);
        var victim = PlayerRest(0) - Vector3.up * 0.9f; var eave = S(-2.6f, 4.6f, 2.9f); var yardHand = S(3.2f, 0.35f, 7.5f);
        float snatch = S01(1.0f, 1.45f, t), carry = S01(1.55f, 2.4f, t), release = S01(5.0f, 5.2f, t), groggy = S01(5.15f, 5.6f, t) * (1 - S01(6.3f, 6.8f, t));
        var ha = Vector3.Lerp(Vector3.Lerp(HandRest(true), victim + Vector3.up * 1.6f, snatch), eave, carry);
        p.handA = ha; p.wA = snatch * (1 - S01(5.3f, 6.2f, t));
        p.handB = yardHand + Vector3.up * 0.15f * Mathf.Sin(t * 5) * (1 - release); p.wB = S01(1.2f, 2.0f, t) * (1 - S01(5.6f, 6.6f, t));
        p.rotB = Quaternion.AngleAxis(25, rightL);
        p.bodyPitch = 5f * carry + 14f * groggy; p.squash *= 1 - 0.14f * groggy; p.bodyRoll = 6f * groggy * Mathf.Sin(t * 2);
        p.eyeGlow = Mathf.Lerp(1 + 1.2f * S01(0f, 0.8f, t), 0.15f, groggy);
        if (fx)
        {
            if (t < 1.5f)
            {   // red chain on the chosen player
                var hd = HeadLocal(); float on = S01(0.1f, 0.5f, t) * (1 - S01(1.3f, 1.5f, t));
                for (int i = 0; i < 10; i++) { var a = Vector3.Lerp(hd, victim + Vector3.up, i / 10f); var b = Vector3.Lerp(hd, victim + Vector3.up, (i + 0.6f) / 10f); Line(a, b, 0.35f, Red * on, 3, 1, Mathf.Sin(t * 12) * 0.3f, false); }
                Decal(victim, 3, 3, 0, Red * on, 0, 1, 0.3f * Mathf.Sin(t * 10));
            }
            if (players.Length > 0 && players[0] != null)
            {
                Vector3 pv = PlayerRest(0);
                if (t > 1.45f && t < 5.0f) pv = ha + Vector3.down * 1.9f + S(0, 0.12f * Mathf.Sin(t * 4), 0);
                else if (t >= 5.0f) { float u = Lin(5.0f, 5.4f, t); var from = eave + Vector3.down * 1.9f; pv = Vector3.Lerp(from, Ground(from) + S(0, 0.9f, 1.2f), u * u); }
                players[0].position = transform.TransformPoint(pv + (t > 1.45f && t < 5.0f ? Vector3.zero : Vector3.zero));
                if (t > 1.45f && t < 5.0f) Tint(0, Red * 0.5f);
            }
            // the two free players run to the yard hand and shoot it
            for (int i = 1; i < Mathf.Min(3, players.Length); i++)
            {
                var from = PlayerRest(i); var to = yardHand + S(i == 1 ? -2.6f : 4.4f, 0, 3.4f); to.y = 0.9f;
                var pp = Vector3.Lerp(from, to, S01(1.9f, 2.6f, t)); players[i].position = transform.TransformPoint(pp);
                if (t > 2.7f && t < 5.0f && Mathf.Repeat(t * 7 + i * 0.5f, 1) < 0.5f) Line(pp + Vector3.up * 1.0f, yardHand + Vector3.up * 0.8f, 0.18f, Yellow * 0.6f, 3, 1, 0, false);
            }
            if (t > 2.0f && t < 5.2f)
            {
                float gauge = 1 - Lin(2.7f, 5.0f, t);
                var gp = yardHand + Vector3.up * 3.2f;
                Billboard(gp, 4.6f, 0.9f, Yellow * 0.12f, 2, 1);
                Billboard(gp, 4.6f, 0.9f, Yellow, 2, gauge);
                if (t > 2.7f) Orb(yardHand + Vector3.up * 0.8f, 1.1f, Yellow * 0.25f * Mathf.Abs(Mathf.Sin(t * 20)));
            }
            if (t > 5.15f && t < 6.6f)
            {   // groggy: stars of dim light circling the head, free hit window
                var hd = HeadLocal() + Vector3.up * 2.6f;
                for (int i = 0; i < 4; i++) { float a = t * 3 + i * Mathf.PI / 2; Orb(hd + S(Mathf.Cos(a) * 2, 0.3f * Mathf.Sin(a * 2), Mathf.Sin(a) * 1.2f), 0.28f, Yellow * 0.8f); }
            }
        }
        return p;
    }

    // ---------------- 7. [협동 2] 명화낙인: red eye-light floods the alley, then freezes all it touches; one player stands in
    // front and takes it, the two behind stay in the shadow ----------------
    Pose P7(float t, bool fx)
    {
        var p = Base(t);
        float charge = S01(0.2f, 1.6f, t), flash = t > 2.6f ? Mathf.Exp(-(t - 2.6f) * 4) : 0, end = S01(3.8f, 4.8f, t);
        p.bodyPitch = 9f * charge * (1 - end); p.eyeGlow = 1 + 5f * charge * (1 - end) + 4 * flash;
        p.squash *= 1 + 0.05f * charge * (1 - end);
        if (fx)
        {
            var front = S(0, 0.9f, 10.5f); var b1 = S(-0.8f, 0.9f, 13.2f); var b2 = S(0.8f, 0.9f, 13.4f);
            if (players.Length > 2) { players[0].position = transform.TransformPoint(front); players[1].position = transform.TransformPoint(b1); players[2].position = transform.TransformPoint(b2); }
            float flood = S01(0.8f, 2.6f, t) * (1 - end);
            float reach = 4f + 26f * S01(0.8f, 2.4f, t);
            // lit alley: everything up to the front player, then both sides of the shadow lane behind them
            float nearEnd = Mathf.Min(reach, 10.0f);
            Decal(S(0, 0, (4 + nearEnd) / 2), 17, nearEnd - 4, 0, Red * 0.55f * flood, 2, 1, flash);
            if (reach > 10.5f)
            {
                float farLen = reach - 10.5f, cz = 10.5f + farLen / 2;
                Decal(S(-5f, 0, cz), 7f, farLen, 0, Red * 0.55f * flood, 2, 1, flash);
                Decal(S(5f, 0, cz), 7f, farLen, 0, Red * 0.55f * flood, 2, 1, flash);
            }
            var hd = HeadLocal();
            foreach (var e in new[] { S(-8, 0, 30), S(8, 0, 30), S(-8, 0, 5), S(8, 0, 5) }) Line(hd, Vector3.Lerp(hd, e, Mathf.Min(1, reach / 30f)), 1.6f, Red * 0.35f * flood, 5, 1, 0, false);
            if (t > 2.6f && players.Length > 0) { Tint(0, Red * (0.7f + 0.3f * Mathf.Sin(t * 14)) * (1 - end)); Orb(front, 1.3f, Red * 0.35f * (1 - end), flash); }
        }
        return p;
    }

    // ---------------- 8. 파포비산: roll a gourd down the alley, it blinks red, bursts into a 180-degree fan of seeds ----------------
    Pose P8(float t, bool fx)
    {
        var p = Base(t);
        var g = GourdRest(1); var drop = S(-1.2f, 0.75f, 4.6f); var stop = S(0, 0.75f, 12f);
        Vector3 h = t < 0.85f ? Vector3.Lerp(HandRest(true), g + Vector3.up * 0.7f, S01(0.3f, 0.8f, t)) : Vector3.Lerp(g + Vector3.up * 0.7f, drop + Vector3.up * 0.9f, S01(0.85f, 1.3f, t));
        if (t > 1.3f) h = Vector3.Lerp(drop + Vector3.up * 0.9f, drop + S(0, 0.9f, 2.5f), EaseIn(Lin(1.3f, 1.45f, t)));
        p.handA = h; p.wA = S01(0.3f, 0.6f, t) * (1 - S01(1.7f, 2.6f, t));
        p.bodyPitch = 8f * S01(0.8f, 1.4f, t) * (1 - S01(1.8f, 2.6f, t));
        if (fx)
        {
            ShowRoofGourd(1, t < 0.85f || t > 4.6f);
            const float boom = 3.2f;
            float blink = t > 2.4f && t < boom ? (Mathf.Repeat((t - 2.4f) * 3.75f, 1) < 0.45f ? 1 : 0) : 0;
            if (t >= 0.85f && t < 1.42f) GourdAt(h + Vector3.down * 0.95f, Quaternion.identity, Red * 0.4f);
            if (t >= 1.42f && t < boom)
            {
                float u = S01(1.42f, 2.4f, t); var gp = Vector3.Lerp(drop, stop, u);
                GourdAt(gp, Quaternion.AngleAxis(Vector3.Distance(drop, gp) / 0.75f * Mathf.Rad2Deg, rightL), Red * (0.4f + 2.2f * blink)); Orb(gp, 1.0f, Red * (0.1f + 0.3f * blink));
                if (blink > 0) Decal(Ground(gp), 3.5f, 3.5f, 0, Red * 0.6f, 1);
            }
            if (t >= boom)
            {
                float k = t - boom; var c = Ground(stop);
                Decal(c, 7 + 8 * k, 7 + 8 * k, 0, White * Mathf.Exp(-k * 8), 1);
                Debris(c, k, 16, 95, 11f, Shard, 0.4f); Smoke(c, k, 3f, 6, 96);
                for (int i = 0; i < 19; i++)
                {
                    float a = Mathf.Lerp(-90f, 90f, i / 18f) * Mathf.Deg2Rad; float d = 1 + 14f * k;
                    var dir = S(Mathf.Sin(a), 0, Mathf.Cos(a)); var pos = c + dir * d + Vector3.up * 0.8f; float e = 1 - S01(1.2f, 1.7f, k);
                    Orb(pos, 0.3f, Red * e); Line(pos - dir * 1.4f, pos, 0.25f, Blood * e, 3, 1, 0, false);
                }
            }
        }
        return p;
    }

    // ---------------- 9. 벽영귀화: boss and house shudder, six dark-red orbs fly out from behind and zigzag down the alley,
    // bouncing off the walls three times ----------------
    const float WallX = 8.2f;
    Vector3 OrbPath(int i, float k, out float alpha)
    {
        // launch arc from behind the head into the alley mouth, then straight runs with wall reflections
        float side = (i % 2 == 0 ? -1 : 1);
        var start = S(side * (0.8f + 0.5f * (i / 2)), 10f, -2.5f); var land = S(side * (1.5f + 1.6f * (i / 2)), 1.2f, 5.5f);
        alpha = 1;
        if (k < 0.5f) { float u = k / 0.5f; return Vector3.Lerp(start, land, u) + Vector3.up * 3f * Mathf.Sin(Mathf.PI * u); }
        float ang = (30f + 9f * (i / 2)) * Mathf.Deg2Rad; float vx = side * Mathf.Sin(ang) * 13f, vz = Mathf.Cos(ang) * 13f;
        float x = -side * (1.5f + 1.6f * (i / 2)), z = 5.5f, rem = k - 0.5f; int bounces = 0;
        x = side * (1.5f + 1.6f * (i / 2));
        const float dt = 1f / 120f;
        for (float s = 0; s < rem; s += dt)
        {
            x += vx * dt; z += vz * dt;
            if (x > WallX) { x = 2 * WallX - x; vx = -vx; bounces++; }
            if (x < -WallX) { x = -2 * WallX - x; vx = -vx; bounces++; }
            if (bounces >= 3) { alpha = Mathf.Max(0, 1 - (s - 0) * 0); break; }
        }
        if (bounces >= 3) alpha = 0;
        return S(x, 1.2f, z);
    }
    Pose P9(float t, bool fx)
    {
        var p = Base(t);
        float shake = S01(0.2f, 0.5f, t) * (1 - S01(1.3f, 1.5f, t));
        p.bodyRoll = 3f * J(t * 2.5f, 11) * shake; p.bodyPitch = 2f * J(t * 2.5f, 12) * shake;
        p.houseOff = V(J(t * 3, 13), Mathf.Abs(J(t * 3, 14)) * 0.5f, J(t * 3, 15)) * 0.09f * shake;
        p.squash *= 1 + 0.04f * J(t * 3, 16) * shake;
        p.eyeGlow = 1 + 1.5f * shake;
        if (fx && t > 0.25f) Straw(S(0, 5.0f, 1.0f), t - 0.25f, 22, 120, 1.6f);
        if (fx)
        {
            for (int i = 0; i < 6; i++)
            {
                float k = t - 1.45f - 0.12f * i; if (k < 0) continue;
                var pos = OrbPath(i, k, out float al); if (al <= 0) continue;
                Orb(pos, 0.65f, Blood * 1.2f); Orb(pos, 1.3f, DarkRed * 0.25f);
                for (int j = 1; j <= 6; j++)
                {
                    float kj = Mathf.Max(0, k - j * 0.04f); var q = OrbPath(i, kj, out float aj); if (aj <= 0) break;
                    Orb(q, 0.55f - j * 0.07f, Blood * (0.6f - j * 0.08f));
                }
            }
        }
        return p;
    }

    // ---------------- 10. 식영귀화: blue wisps rise over the head, hang 1.5 s shivering, then dart at the players off-beat ----------------
    Pose P10(float t, bool fx)
    {
        var p = Base(t);
        float rise = S01(0.2f, 1.3f, t);
        p.bodyPitch = -5f * rise * (1 - S01(3.0f, 4.0f, t)); p.eyeGlow = 1 + 1.4f * rise;
        if (fx)
        {
            var hd = HeadLocal();
            float[] groupT = { 2.9f, 3.18f, 3.62f };   // off-beat volleys
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.Lerp(-70f, 70f, i / 9f) * Mathf.Deg2Rad;
                var hover = hd + S(Mathf.Sin(a) * 7.5f, 1.8f + Mathf.Cos(a) * 1.4f, 0.6f);
                var from = hd + S(Mathf.Sin(a) * 2f, -4f, -4f);
                float ri = S01(0.2f + 0.06f * i, 1.2f + 0.06f * i, t);
                var pos = Vector3.Lerp(from, hover, ri);
                float sh = S01(1.4f, 1.6f, t);
                pos += S(J(t * 2, i), J(t * 2, i + 20), 0) * 0.18f * sh;
                int grp = i % 3; float tl = groupT[grp];
                var tgt = PlayerRest(grp) + Vector3.up * 1.0f + S((i / 3 - 1) * 0.7f, 0, 0);
                float flyT = Vector3.Distance(hover, tgt) / 45f;
                if (t < 0.2f + 0.06f * i) continue;
                if (t < tl)
                {
                    Orb(pos, 0.45f, Blue); Orb(pos, 1.1f, Blue * 0.18f);
                }
                else if (t < tl + flyT)
                {
                    float u = (t - tl) / flyT; var q = Vector3.Lerp(hover, tgt, u);
                    Orb(q, 0.4f, Blue * 1.3f); Line(Vector3.Lerp(hover, tgt, Mathf.Max(0, u - 0.35f)), q, 0.35f, Blue * 0.6f, 3, 1, 0, false);
                }
                else
                {
                    float k = t - tl - flyT; if (k < 0.5f) Decal(Ground(tgt), 2.4f + 4 * k, 2.4f + 4 * k, 0, Blue * Mathf.Exp(-k * 6), 1);
                    if (k < 1.2f && i < 3) Tint(grp, Blue * 0.35f * (1 - k / 1.2f));
                }
            }
        }
        return p;
    }

    // capture helpers: camera shake amplitude (m) and hit-stops (freeze frames) at each impact
    static readonly float[][] Impacts = {
        new float[0],
        new[] { 1.9f, 0.3f },
        new[] { 2.62f, 0.45f },
        new[] { 2.2f, 1.0f, 3.25f, 0.45f, 3.6f, 0.35f, 3.95f, 0.3f },
        new[] { 2.8f, 0.35f, 3.18f, 0.35f, 3.56f, 0.35f, 3.94f, 0.35f, 4.32f, 0.35f },
        new[] { 2.36f, 0.9f },
        new[] { 1.45f, 0.2f, 5.25f, 0.3f },
        new[] { 2.6f, 0.4f },
        new[] { 3.2f, 0.5f },
        new[] { 0.3f, 0.2f, 0.7f, 0.2f, 1.1f, 0.2f },
        new[] { 3.0f, 0.15f, 3.3f, 0.15f, 3.75f, 0.15f },
    };
    static readonly float[][] HitStops = { new float[0], new[] { 1.9f, 0.06f }, new[] { 2.62f, 0.06f }, new[] { 2.2f, 0.12f }, new float[0], new[] { 2.36f, 0.1f },
        new[] { 1.45f, 0.06f }, new[] { 2.6f, 0.1f }, new[] { 3.2f, 0.07f }, new float[0], new float[0] };
    public static float Shake(int n, float t)
    {
        float a = 0; var im = Impacts[Mathf.Clamp(n, 0, 10)];
        for (int i = 0; i + 1 < im.Length; i += 2) { float k = t - im[i]; if (k >= 0) a = Mathf.Max(a, im[i + 1] * Mathf.Exp(-k * 7f)); }
        return a;
    }
    // capture time -> pattern time with freezes; returns the extra length added by the stops
    public static float HitStopTime(int n, float ct, out float extra)
    {
        var hs = HitStops[Mathf.Clamp(n, 0, 10)]; float t = ct; extra = 0;
        for (int i = 0; i + 1 < hs.Length; i += 2) { extra += hs[i + 1]; if (ct > hs[i]) t -= Mathf.Min(ct - hs[i], hs[i + 1]); }
        return t;
    }

    static readonly Color Soil = new Color(0.075f, 0.06f, 0.05f, 1f), Shard = new Color(0.16f, 0.025f, 0.02f, 1f),
        StrawC = new Color(0.36f, 0.29f, 0.16f, 0.95f), Scorch = new Color(0.02f, 0.012f, 0.01f, 0.85f), SmokeC = new Color(0.3f, 0.27f, 0.23f, 0.55f);
    static float Rnd(int seed, int i, int j) { uint h = (uint)(seed * 73856093 ^ i * 19349663 ^ j * 83492791); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h & 0xFFFFFF) / 16777215f; }

    // dark dust puffs rolling out from c and rising (alpha-blended, they darken the scene behind them)
    void Smoke(Vector3 c, float k, float radius, int n, int seed)
    {
        if (k < 0 || k > 2.4f) return;
        for (int i = 0; i < n; i++)
        {
            float a = Rnd(seed, i, 1) * Mathf.PI * 2, sp = 0.6f + Rnd(seed, i, 2);
            float d = radius * (0.25f + 0.75f * (1 - Mathf.Exp(-k * 3f * sp)));
            var pos = c + S(Mathf.Cos(a) * d, 0.4f + k * (0.6f + 1.4f * Rnd(seed, i, 3)), Mathf.Sin(a) * d * 0.8f);
            float size = (2.2f + 2.0f * Rnd(seed, i, 4)) * (0.6f + k * 1.1f);
            float al = Mathf.Clamp01(k * 8) * (1 - Mathf.Clamp01(k / 2.4f));
            BillboardA(pos, size, size, SmokeC * new Color(1, 1, 1, al), 7, i + seed);
        }
    }
    // chunks thrown ballistically from c, bouncing to rest
    void Debris(Vector3 c, float k, int n, int seed, float speed, Color col, float size)
    {
        if (k < 0 || k > 3f) return;
        for (int i = 0; i < n; i++)
        {
            float a = Rnd(seed, i, 1) * Mathf.PI * 2, el = 0.5f + 0.9f * Rnd(seed, i, 2), v = speed * (0.4f + 0.6f * Rnd(seed, i, 3));
            var dir = S(Mathf.Cos(a) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(a) * Mathf.Cos(el));
            float tLand = 2 * dir.y * v / 20f, kk = Mathf.Min(k, tLand);
            var pos = c + new Vector3(dir.x, 0, dir.z) * v * kk + Vector3.up * (0.2f + dir.y * v * kk - 10f * kk * kk);
            if (k > tLand) pos += new Vector3(dir.x, 0, dir.z) * v * 0.15f * (1 - Mathf.Exp(-(k - tLand) * 6));
            float sz = size * (0.4f + Rnd(seed, i, 4));
            float al = 1 - Mathf.Clamp01((k - 2.2f) / 0.8f);
            OrbA(pos, new Vector3(sz, sz * (0.5f + 0.5f * Rnd(seed, i, 5)), sz * (0.6f + 0.6f * Rnd(seed, i, 6))), col * new Color(1, 1, 1, al), Quaternion.Euler(k * 400 * Rnd(seed, i, 7), k * 300, 0));
        }
    }
    // thatch straws falling and tumbling from h
    void Straw(Vector3 h, float k, int n, int seed, float spread)
    {
        if (k < 0 || k > 2.6f) return;
        for (int i = 0; i < n; i++)
        {
            float t0 = Rnd(seed, i, 9) * 0.5f, kk = k - t0; if (kk < 0) continue;
            var start = h + S((Rnd(seed, i, 1) - 0.5f) * 7f, Rnd(seed, i, 2) * 0.6f, (Rnd(seed, i, 3) - 0.3f) * spread);
            float fall = Mathf.Min(start.y - 0.05f, 2.5f * kk * kk + 1.2f * kk);
            var pos = start + Vector3.down * fall + S(Mathf.Sin(kk * 4 + i) * 0.4f, 0, kk * 0.8f * Rnd(seed, i, 4));
            float ang = kk * 6 * (Rnd(seed, i, 5) - 0.5f) + i;
            var d = S(Mathf.Cos(ang), Mathf.Sin(ang * 0.7f) * 0.5f, Mathf.Sin(ang)) * (0.35f + 0.3f * Rnd(seed, i, 6));
            LineA(pos - d, pos + d, 0.07f, StrawC * new Color(1, 1, 1, 1 - Mathf.Clamp01((kk - 1.8f) / 0.6f)), 6, 1, false);
        }
    }

    // shared: impact flash + ring of seeds (count 0 = flash only)
    void Burst(Vector3 c, float k, int seeds, float speed, float life, float radius = 3f)
    {
        float e = 1 - S01(life * 0.7f, life, k);
        Decal(c, radius * 2 + 10 * k, radius * 2 + 10 * k, 0, White * Mathf.Exp(-k * 9), 1);
        Decal(c, radius * 2.2f, radius * 2.2f, 0, Blood * e * 0.7f, 1);
        Orb(c + Vector3.up * 0.8f, radius * 0.6f + k * 4, Red * 0.7f * Mathf.Exp(-k * 7));
        for (int i = 0; i < seeds; i++)
        {
            float a = i * Mathf.PI * 2 / seeds; var dir = S(Mathf.Cos(a), 0, Mathf.Sin(a));
            Orb(c + dir * (0.5f + speed * k) + Vector3.up * 0.7f, 0.3f, Red * e);
        }
    }

    // ---------------- immediate-mode FX pool ----------------
    Transform fxRoot; readonly List<MeshRenderer> poolQ = new List<MeshRenderer>(), poolS = new List<MeshRenderer>(), poolG = new List<MeshRenderer>(),
        poolQA = new List<MeshRenderer>(), poolSA = new List<MeshRenderer>();
    int nQ, nS, nG, nQA, nSA; static Mesh quadMesh, sphereMesh;
    void FxBegin()
    {
        if (fxRoot == null)
        {
            var go = new GameObject("FX_Pool") { hideFlags = HideFlags.DontSave }; fxRoot = go.transform; fxRoot.SetParent(transform, false);
            poolQ.Clear(); poolS.Clear(); poolG.Clear(); poolQA.Clear(); poolSA.Clear();
        }
        if (quadMesh == null) quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        if (sphereMesh == null) sphereMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        nQ = nS = nG = nQA = nSA = 0;
    }
    void FxEnd()
    {
        for (int i = nQ; i < poolQ.Count; i++) if (poolQ[i]) poolQ[i].enabled = false;
        for (int i = nS; i < poolS.Count; i++) if (poolS[i]) poolS[i].enabled = false;
        for (int i = nG; i < poolG.Count; i++) if (poolG[i]) poolG[i].enabled = false;
        for (int i = nQA; i < poolQA.Count; i++) if (poolQA[i]) poolQA[i].enabled = false;
        for (int i = nSA; i < poolSA.Count; i++) if (poolSA[i]) poolSA[i].enabled = false;
    }
    MeshRenderer Get(List<MeshRenderer> pool, ref int n, Mesh mesh, Material mat)
    {
        if (n >= pool.Count || pool[n] == null)
        {
            var go = new GameObject("fx") { hideFlags = HideFlags.DontSave }; go.transform.SetParent(fxRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = mat == gourdMat ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = mat == gourdMat;
            if (n >= pool.Count) pool.Add(mr); else pool[n] = mr;
        }
        var r = pool[n++]; r.enabled = true; return r;
    }
    void SetFx(MeshRenderer r, Color c, int mode, float fill, float pulse)
    {
        var mpb = new MaterialPropertyBlock(); mpb.SetColor("_Color", c); mpb.SetFloat("_Mode", mode); mpb.SetFloat("_Fill", fill); mpb.SetFloat("_Pulse", pulse); r.SetPropertyBlock(mpb);
    }
    // flat mark on the ground, boss-local centre, size along screen-x / forward
    void Decal(Vector3 c, float sx, float sz, float yaw, Color col, int mode, float fill = 1, float pulse = 0)
    {
        if (fxMat == null || col.maxColorComponent <= 0.001f) return;
        var r = Get(poolQ, ref nQ, quadMesh, fxMat);
        var w = transform.TransformPoint(c + Vector3.up * (0.06f + 0.002f * nQ));
        r.transform.SetPositionAndRotation(w, transform.rotation * Quaternion.LookRotation(Vector3.down, fwdL) * Quaternion.Euler(0, 0, yaw));
        r.transform.localScale = new Vector3(sx, sz, 1);
        SetFx(r, col, mode, fill, pulse);
    }
    void Line(Vector3 a, Vector3 b, float width, Color col, int mode, float fill, float pulse, bool flat)
    {
        if (fxMat == null || col.maxColorComponent <= 0.001f) return;
        var wa = transform.TransformPoint(a + (flat ? Vector3.up * (0.07f + 0.002f * nQ) : Vector3.zero)); var wb = transform.TransformPoint(b + (flat ? Vector3.up * (0.07f + 0.002f * nQ) : Vector3.zero));
        var d = wb - wa; float len = d.magnitude; if (len < 1e-3f) return;
        var x = d / len;
        Vector3 nrm = flat ? Vector3.up : (fxCamera != null ? (fxCamera.transform.position - (wa + wb) * 0.5f).normalized : -transform.TransformDirection(fwdL));
        nrm = (nrm - x * Vector3.Dot(nrm, x)).normalized; var y = Vector3.Cross(nrm, x);
        var r = Get(poolQ, ref nQ, quadMesh, fxMat);
        r.transform.SetPositionAndRotation((wa + wb) * 0.5f, Quaternion.LookRotation(nrm, y));
        r.transform.localScale = new Vector3(len, width, 1);
        SetFx(r, col, mode, fill, pulse);
    }
    void Billboard(Vector3 c, float w, float h, Color col, int mode, float fill)
    {
        if (fxMat == null) return;
        var r = Get(poolQ, ref nQ, quadMesh, fxMat);
        var wp = transform.TransformPoint(c);
        var rot = fxCamera != null ? fxCamera.transform.rotation : Quaternion.LookRotation(transform.TransformDirection(-fwdL));
        r.transform.SetPositionAndRotation(wp, rot); r.transform.localScale = new Vector3(w, h, 1);
        SetFx(r, col, mode, fill, 0);
    }
    void BillboardA(Vector3 c, float w, float h, Color col, int mode, int seed)
    {
        if (fxMatAlpha == null || col.a <= 0.003f) return;
        var r = Get(poolQA, ref nQA, quadMesh, fxMatAlpha);
        var wp = transform.TransformPoint(c);
        var rot = fxCamera != null ? fxCamera.transform.rotation : Quaternion.LookRotation(transform.TransformDirection(-fwdL));
        r.transform.SetPositionAndRotation(wp, rot * Quaternion.Euler(0, 0, seed * 47f)); r.transform.localScale = new Vector3(w, h, 1);
        SetFx(r, col, mode, 1, 0); var mpb = new MaterialPropertyBlock(); r.GetPropertyBlock(mpb); mpb.SetFloat("_Seed", seed); r.SetPropertyBlock(mpb);
    }
    void LineA(Vector3 a, Vector3 b, float width, Color col, int mode, float fill, bool flat = true)
    {
        if (fxMatAlpha == null || col.a <= 0.003f) return;
        var wa = transform.TransformPoint(a + (flat ? Vector3.up * 0.05f : Vector3.zero)); var wb = transform.TransformPoint(b + (flat ? Vector3.up * 0.05f : Vector3.zero));
        var d = wb - wa; float len = d.magnitude; if (len < 1e-3f) return;
        var x = d / len;
        Vector3 nrm = flat ? Vector3.up : (fxCamera != null ? (fxCamera.transform.position - (wa + wb) * 0.5f).normalized : Vector3.up);
        nrm = (nrm - x * Vector3.Dot(nrm, x)).normalized; var y = Vector3.Cross(nrm, x);
        var r = Get(poolQA, ref nQA, quadMesh, fxMatAlpha);
        r.transform.SetPositionAndRotation((wa + wb) * 0.5f, Quaternion.LookRotation(nrm, y)); r.transform.localScale = new Vector3(len, width, 1);
        SetFx(r, col, mode, fill, 0);
    }
    void OrbA(Vector3 c, Vector3 scale, Color col, Quaternion rot)
    {
        if (fxMatAlpha == null || col.a <= 0.003f) return;
        var r = Get(poolSA, ref nSA, sphereMesh, fxMatAlpha);
        r.transform.SetPositionAndRotation(transform.TransformPoint(c), transform.rotation * rot); r.transform.localScale = scale;
        SetFx(r, col, 8, 1, 0);
    }
    void Orb(Vector3 c, float radius, Color col, float pulse = 0, Vector3? scale = null)
    {
        if (fxMat == null || col.maxColorComponent <= 0.001f) return;
        var r = Get(poolS, ref nS, sphereMesh, fxMat);
        r.transform.SetPositionAndRotation(transform.TransformPoint(c), transform.rotation);
        r.transform.localScale = scale ?? Vector3.one * radius * 2;
        SetFx(r, col, 4, 1, pulse);
    }
    void GourdAt(Vector3 c, Quaternion localRot, Color emission)
    {
        if (gourdMat == null || gourdMesh == null) return;
        var r = Get(poolG, ref nG, gourdMesh, gourdMat);
        if (gourdStemMat != null && r.sharedMaterials.Length < 2) r.sharedMaterials = new[] { gourdMat, gourdStemMat };
        r.transform.SetPositionAndRotation(transform.TransformPoint(c) + Vector3.down * 0.55f * gourdScale, transform.rotation * localRot * Quaternion.Euler(-90, 0, 0));
        r.transform.localScale = Vector3.one * gourdScale;
        var mpb = new MaterialPropertyBlock(); mpb.SetColor("_EmissionColor", emission); r.SetPropertyBlock(mpb);
    }
    void ShowRoofGourd(int i, bool on)
    {
        if (i >= roofGourds.Length || roofGourds[i] == null) return;
        foreach (var r in roofGourds[i].GetComponentsInChildren<Renderer>()) r.enabled = on;
    }
    void Tint(int i, Color c)
    {
        if (i >= players.Length || players[i] == null) return;
        var r = players[i].GetComponent<Renderer>(); if (r == null) return;
        var mpb = new MaterialPropertyBlock(); r.GetPropertyBlock(mpb); mpb.SetColor("_EmissionColor", c); r.SetPropertyBlock(mpb);
    }
}
