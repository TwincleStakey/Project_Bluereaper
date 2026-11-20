// 장산범 fog dash pattern (11-19 Kido: "크고 짙은 안개 속에 숨어들어 사라졌다가 랜덤 위치에서 빨간 텔레그래프 나타났다가 박치기").
// Structure taken from the fog-circle phase of an ARPG wolf boss: a ring of dense fog closes round the arena, the boss slips into
// it and vanishes, then several times a red strip appears on the ground along a random line through the player, the boss shows
// in the fog at the head of the strip in its play-bow crouch (rump wiggling) and shoots across the arena flat and stretched,
// back into the fog on the far side. After the last pass it lunges back to the centre and the fog clears.
// Everything is a function of time (Sample), so the capture tools and Play mode run the same thing.
using System.Collections.Generic;
using UnityEngine;

public class JangsanbeomFogDash : MonoBehaviour
{
    public AnimationClip standClip, crouchClip, dashClip;
    // 11-20 Kido ("처음 안개 속으로 들어갈 때 뒷걸음질 치면서 몸 점점 낮아지면서 숨을 수 있어?"): the opening is a retreat - still
    // facing the player it backs away step by step (the walk played backwards), sinking lower and lower into a crouch, until
    // the fog swallows it.
    public AnimationClip walkClip;
    [Tooltip("Seconds the backing-away into the fog takes")] public float retreatTime = 4.2f;
    [Tooltip("How far into the crouch it sinks while backing away (1 = full crouch, belly on the floor)")] [Range(0, 1)] public float retreatCrouch = 0.45f;
    [Tooltip("Metres a walk cycle covers going forward (jsb2_walk_lion: 13.07 m per cycle)")] public float walkCycleDist = 13.07f;
    [Tooltip("Gallop cycle: each bound of the pass plays one cycle")] public AnimationClip gallopClip;
    // 11-20 Kido ("딱 레퍼런스인 poe2처럼, 안개 퀄 좋은 거"): seen in the reference fight footage - a cool blue-grey, billowing,
    // rolling fog bank round a ragged clear patch; the wolf is a dark shape inside it and drags fog across the floor when it
    // comes through. Sprite: Unity Particle Pack "SmokeLoop02" (6x6 animated smoke loop, the pack's own ground-fog texture).
    public Material fogMaterial;          // animated smoke flipbook, alpha blended
    public int fogTiles = 6;
    // 11-20 Kido ("안개 퀄이 너무 별로"): the bank itself is now a stack of fog sheets (JangsanbeomFogRing shader); the smoke
    // sprites are only used for fog the boss drags out and blows out on a landing
    public Material fogSheetMaterial;
    Transform sheetRoot;
    public int fogSheets = 10;
    public float fogTop = 10f;
    public Material dustMaterial;         // smoke flipbook (4x4) for the dust the paws kick up
    public Material telegraphMaterial;    // additive HDR lines (JangsanbeomTrail, hard edge): the border of the strip
    public Material telegraphFillMaterial;// alpha-blended, vertex coloured: the solid red of the strip
    [Tooltip("Red shock ring on the floor at a landing (11-20 Kido: off - \"바닥 칠 때 주위로 링 같은 거 날리지 마\")")] public bool landingRing = false;
    [Tooltip("Height of each bound (world units)")] public float boundHeight = 5.2f;
    // 11-19 Kido: two ways to cross. 0 = "흔들다가 확 점프해서 한 번 가운데 착지하고 지나가는": wiggle, explode off the mark, land once in
    // the middle, leap on. 1 = "저 뒤에서부터 포물선 그리면서 앞으로 크게 한 번": one great arcing leap from the back that comes down past
    // the middle, then it runs out into the fog.
    public int leapStyle = 0;
    [Tooltip("Height of the single great leap (style 1)")] public float bigLeapHeight = 7.5f;
    public Transform player;
    public Vector3 arenaCentre = Vector3.zero;
    public float arenaRadius = 30f;
    [Tooltip("The fog ring starts here; inside it only a thin haze")] public float fogInner = 15f;
    public int dashes = 4;
    public float fogIn = 1.4f, telegraphTime = 1.1f, dashTime = 0.85f, gap = 0.5f, fogOut = 1.6f;
    public float stripWidth = 8f;
    [Tooltip("How far beside the player the line passes (0 = straight through the player)")] public float aimOffset = 0f;
    public int seed = 7;
    public Color fogColor = new Color(0.40f, 0.47f, 0.60f, 0.55f);   // the map is nearly black: a dark fog cannot be seen at all, so it is a pale, heavy grey
    [ColorUsage(false, true)] public Color telegraphColor = new Color(6f, 0.1f, 0.05f, 1f);
    public System.Action<Vector3> onHit;   // called once per pass when the body runs over the player

    public bool Visible { get; private set; } = true;
    /// <summary>0..1 camera shake wanted right now (the landings); the game camera or a capture tool applies it.</summary>
    public float Shake { get; private set; }
    Transform[] poseBones; Vector3[] poseP; Quaternion[] poseR;
    /// <summary>True while the boss is in the air on a pass (trails and after-images key off this).</summary>
    public bool Airborne { get; private set; }
    bool scaleKept;
    Vector3 baseScale = Vector3.one, fwdLocal = Vector3.forward; bool ringed; float ringT = -99f; Vector3 ringPos; LineRenderer ring;
    public bool Running { get; private set; }
    public float Duration => fogIn + passes.Count * (telegraphTime + dashTime + gap) + (walkClip ? retreatTime - (telegraphTime + dashTime + gap) : 0f) + fogOut;

    class Pass { public float t0; public Vector3 from, to; public bool set, hit; }
    readonly List<Pass> passes = new List<Pass>();
    Transform head, hips; Quaternion baseRot = Quaternion.identity; Vector3 homePos; Quaternion homeRot;
    ParticleSystem fog, dust; LineRenderer strip, fill, edgeA, edgeB; float gallopT0; Transform[] hindToes, allToes; bool landed; float lastSim = -1, startTime; System.Random rng; Renderer[] rends;

    public void Build()
    {
        foreach (Transform c in transform) if (c.name.StartsWith("FogDashFX")) { if (Application.isPlaying) Destroy(c.gameObject); else DestroyImmediate(c.gameObject); }
        var all = GetComponentsInChildren<Transform>(true);
        head = System.Array.Find(all, t => t.name == "Head"); hips = System.Array.Find(all, t => t.name == "Hips");
        rends = GetComponentsInChildren<Renderer>(true);
        poseBones = System.Array.FindAll(all, t => t != transform && t.GetComponent<Renderer>() == null);
        poseP = new Vector3[poseBones.Length]; poseR = new Quaternion[poseBones.Length];
        homePos = transform.position; homeRot = transform.rotation; if (!scaleKept) { baseScale = transform.localScale; scaleKept = true; } transform.localScale = baseScale;
        if (standClip) standClip.SampleAnimation(gameObject, 0f);
        if (head && hips)
        {   // which way the model looks, so the root can be turned along a pass
            var f = transform.InverseTransformDirection(head.position - hips.position); f.y = 0; if (f.sqrMagnitude > 1e-6f) fwdLocal = f.normalized;
            if (f.sqrMagnitude > 1e-6f) baseRot = Quaternion.Inverse(Quaternion.LookRotation(f.normalized));
        }
        rng = new System.Random(seed);
        passes.Clear();
        float t = fogIn;
        for (int i = 0; i < dashes + 2; i++) { passes.Add(new Pass { t0 = t }); t += i == 0 && walkClip ? retreatTime : telegraphTime + dashTime + gap; }

        var root = new GameObject("FogDashFX") { hideFlags = HideFlags.DontSave }; root.transform.SetParent(transform, false);
        fog = NewPS(root.transform, "Fog", fogMaterial, 4400, fogTiles, 0.35f); dust = NewPS(root.transform, "Dust", fogMaterial, 1200, fogTiles, 1f);
        // the bank must not ride on the boss (it moves, and is dropped out of sight while hidden): its own scene object
        if (sheetRoot != null) { if (Application.isPlaying) Destroy(sheetRoot.gameObject); else DestroyImmediate(sheetRoot.gameObject); }
        if (gameObject.scene.IsValid())   // a bank left over from an earlier capture (OnDestroy does not run in edit mode)
            foreach (var g in gameObject.scene.GetRootGameObjects()) if (g.name == "FogDashFX_Bank") { if (Application.isPlaying) Destroy(g); else DestroyImmediate(g); }
        if (fogSheetMaterial)
        {
            sheetRoot = new GameObject("FogDashFX_Bank") { hideFlags = HideFlags.DontSave }.transform;
            if (gameObject.scene.IsValid()) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(sheetRoot.gameObject, gameObject.scene);
            BuildSheets(sheetRoot);
        }
        strip = NewLine(root.transform, "Strip", telegraphFillMaterial ? telegraphFillMaterial : telegraphMaterial); fill = NewLine(root.transform, "Fill", telegraphFillMaterial ? telegraphFillMaterial : telegraphMaterial);
        edgeA = NewLine(root.transform, "EdgeA", telegraphMaterial); edgeB = NewLine(root.transform, "EdgeB", telegraphMaterial);
        ring = NewLine(root.transform, "Ring", telegraphMaterial); ring.loop = true; ring.positionCount = 64; ringT = -99f; ringed = false;
        // the gallop cycle starts where the hind paws are planted (the push-off)
        hindToes = new[] { System.Array.Find(all, t => t.name == "HindToes.L"), System.Array.Find(all, t => t.name == "HindToes.R") };
        allToes = new[] { hindToes[0], hindToes[1], System.Array.Find(all, t => t.name == "FrontToes.L"), System.Array.Find(all, t => t.name == "FrontToes.R") };
        gallopT0 = 0f;
        if (gallopClip && hindToes[0] && hindToes[1])
        {
            float best = float.MaxValue;
            for (int k = 0; k < 40; k++)
            {
                float tt = k / 40f * gallopClip.length; gallopClip.SampleAnimation(gameObject, tt);
                float h = hindToes[0].position.y + hindToes[1].position.y;
                if (h < best) { best = h; gallopT0 = tt; }
            }
            if (standClip) standClip.SampleAnimation(gameObject, 0f);
        }
        lastSim = -1; Running = false;
    }

    ParticleSystem NewPS(Transform parent, string n, Material m, int max, int tiles, float cycles)
    {
        var go = new GameObject("FogDashFX_" + n) { hideFlags = HideFlags.DontSave }; go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = false; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = max; main.startSpeed = 0;
        var em = ps.emission; em.enabled = false; var sh = ps.shape; sh.enabled = false;
        var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.12f), new GradientAlphaKey(1, 0.85f), new GradientAlphaKey(0, 1) });
        col.color = g;
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
        var ts = ps.textureSheetAnimation; ts.enabled = tiles > 1; ts.numTilesX = tiles; ts.numTilesY = tiles; ts.animation = ParticleSystemAnimationType.WholeSheet;
        ts.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.999f); ts.cycleCount = 1;
        ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, cycles)));   // the smoke rolls as it lives
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; r.sortingFudge = -20;
        return ps;
    }

    LineRenderer NewLine(Transform parent, string n, Material mat)
    {
        var go = new GameObject("FogDashFX_" + n) { hideFlags = HideFlags.DontSave }; go.transform.SetParent(parent, false);
        go.transform.rotation = Quaternion.LookRotation(Vector3.up);   // the strip lies flat on the ground (alignment TransformZ)
        var lr = go.AddComponent<LineRenderer>(); lr.useWorldSpace = true; lr.positionCount = 2; lr.alignment = LineAlignment.TransformZ; lr.sharedMaterial = mat;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false; lr.enabled = false; lr.numCapVertices = 0;
        return lr;
    }

    public void Begin(float time) { Build(); startTime = time; Running = true; }
    void LateUpdate() { if (Application.isPlaying && Running) Sample(Time.time - startTime); }

    float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    Vector3 PlayerPos => player ? new Vector3(player.position.x, 0, player.position.z) : arenaCentre;

    // a line through p along d, cut by the arena circle (the far points, inside the fog)
    void Chord(Vector3 p, Vector3 d, out Vector3 a, out Vector3 b)
    {
        var o = p - arenaCentre; float bq = Vector3.Dot(o, d), c = Vector3.Dot(o, o) - arenaRadius * arenaRadius;
        float disc = Mathf.Sqrt(Mathf.Max(0f, bq * bq - c));
        a = p + d * (-bq - disc); b = p + d * (-bq + disc);
    }

    void Plan(int k)
    {
        var ps = passes[k]; ps.set = true;
        if (k == 0)
        {   // slip away: from where it stands straight out into the fog, away from the player
            var d = homePos - PlayerPos; d.y = 0; if (d.sqrMagnitude < 1f) d = Vector3.forward; d.Normalize();
            ps.from = new Vector3(homePos.x, 0, homePos.z);
            if (walkClip)
            {   // back away across the screen (sideways for the top-down camera, which looks along +z), so the player
                // sees it sink into the fog instead of walking off the top edge first
                var side = new Vector3(Mathf.Sign(homePos.x - PlayerPos.x == 0f ? 1f : homePos.x - PlayerPos.x), 0f, 0f);
                ps.to = ps.from + side * Mathf.Max(8f, fogInner + 6f - Mathf.Abs(ps.from.x - arenaCentre.x));   // only just into the bank, still on screen
            }
            else ps.to = arenaCentre + (ps.from - arenaCentre + d * arenaRadius * 2f).normalized * arenaRadius;
        }
        else if (k == passes.Count - 1)
        {   // home: out of the fog to where it stood
            float a = Rand(0, Mathf.PI * 2f); ps.from = arenaCentre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * arenaRadius; ps.to = new Vector3(homePos.x, 0, homePos.z);
        }
        else
        {   // a random line through (or just beside) the player
            float a = Rand(0, Mathf.PI * 2f); var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var p = PlayerPos + Vector3.Cross(Vector3.up, d) * aimOffset * (rng.Next(2) == 0 ? 1f : -1f);
            if ((p - arenaCentre).magnitude > arenaRadius * 0.8f) p = arenaCentre + (p - arenaCentre).normalized * arenaRadius * 0.8f;
            Chord(p, d, out ps.from, out ps.to);
        }
    }

    public void Sample(float t)
    {
        if (fog == null) return;
        if (lastSim >= 0 && t < lastSim) { Build(); Running = true; }
        if (lastSim < 0)
        {   // in edit mode the first Simulate restarts the system and drops anything emitted before it, so start it first
            if (!Application.isPlaying) { fog.Simulate(0.0001f, true, true, false); dust.Simulate(0.0001f, true, true, false); }
            EmitFog();
        }
        // which pass
        int k = -1; for (int i = 0; i < passes.Count; i++) if (t >= passes[i].t0) k = i;
        float cyc = telegraphTime + dashTime + gap;
        bool vis = true; float stripA = 0, fillU = 0; Shake = 0f; Airborne = false; ApplySquash(1f); DrawRing(t); Pass cur = k >= 0 ? passes[k] : null;
        if (k < 0)
        {   // the fog closes in; the boss still stands
            transform.SetPositionAndRotation(homePos, homeRot);
            if (standClip) standClip.SampleAnimation(gameObject, t % standClip.length);
        }
        else
        {
            if (!cur.set) Plan(k);
            float lt = t - cur.t0; var dir = (cur.to - cur.from).normalized; float len = (cur.to - cur.from).magnitude;
            var rot = Quaternion.LookRotation(dir) * baseRot;
            bool last = k == passes.Count - 1;
            if (k == 0 && walkClip)
            {   // backing away into the fog, facing the player, sinking
                float u = Mathf.Clamp01(lt / retreatTime);
                float e = u * u * (3f - 2f * u) * 0.35f + u * 0.65f;            // eases off, then keeps creeping
                var pos = Vector3.Lerp(cur.from, cur.to, e);
                transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-dir) * baseRot);
                float dist = len * e;
                float tw = walkClip.length - ((dist / walkCycleDist) * walkClip.length) % walkClip.length;   // walk cycle backwards, feet in step
                // 11-20 Kido ("뒤로 가는 거 바닥에 너무 크게 쳐박혀"): only half way down into the crouch, not belly in the floor
                float low = Mathf.SmoothStep(0f, 1f, u * 1.25f) * retreatCrouch;
                Pose(walkClip, tw % walkClip.length, crouchClip ? crouchClip : walkClip, crouchClip ? crouchClip.length * 0.4f : tw, low);
                // gone once it is well inside the bank
                vis = u < 0.97f;   // it is in the bank by the end; gone from then on
            }
            else if (lt < telegraphTime)
            {   // the strip fills; the boss shows at its head, crouched
                float u = lt / telegraphTime;
                transform.SetPositionAndRotation(cur.from, rot);
                if (crouchClip) crouchClip.SampleAnimation(gameObject, Mathf.Min(u * crouchClip.length, crouchClip.length));
                vis = k == 0;   // 11-20 Kido: "움찔하는 거 fog 안에서 보이거든, 안 보이게" - hidden until the instant it bursts out of the bank
                stripA = Mathf.SmoothStep(0, 1, u * 3f) * (0.55f + 0.2f * Mathf.Sin(u * u * 40f)); fillU = u;
            }
            else if (lt < telegraphTime + dashTime)
            {   // the pass. 11-19 Kido ("너무 어설퍼, 중간에 한 번 착지하고 다시 뛰어, 동물 달리기처럼"): not a slide - two bounding
                // leaps of a galloping animal. Leap, come down on the front paws in the middle (the body dips and nearly stops,
                // dust bursts), gather the hind legs under and leap again. Each leap plays one gallop cycle from the push-off.
                float u = (lt - telegraphTime) / dashTime;
                // 11-20 Kido ("아직도 역동성 많이 부족해"). Laid out frame by frame the pass was a rigid pose sliding by, seen for six
                // frames. Now: (1) the first leap is short and fast and SLAMS down in the clearing - a shock ring, fog blown out,
                // the body squashed flat, a hard shake; (2) it stays coiled there for a beat, in plain sight; (3) it explodes
                // into the second leap. In the air the body stretches long and the legs sweep (gallop cycle under the
                // stretched pose); red streaks trail from the paws (JangsanbeomClawTrails on all four).
                // 11-20 Kido ("착지할 때 부르르 떨고... 이상해", "착지할 때 확실히 슬라이딩 되지 않게, 점프 다시 하는 느낌"): the landing
                // no longer travels at all (it slid 5 % of the pass, metres) and no longer plays the crouch clip (its rump
                // wiggle read as a shudder). It is one held coil pose: impact -> settle -> load up (sinks, rump rises) -> launch.
                const float land0 = 0.30f, land1 = 0.56f, landShare = 0f;
                float half = 0.5f - landShare * 0.5f;
                float e, air, cycleU; float hgt = boundHeight; bool landing = false; float landQ = 0f;
                if (leapStyle == 1)
                {   // one great leap over 70 % of the way (62 % of the time), a heavy landing, then the run-out
                    const float fly = 0.62f, down = 0.74f, flyShare = 0.70f, downShare = 0.04f;
                    hgt = bigLeapHeight;
                    if (u < fly) { float q = u / fly; e = flyShare * (q * (1.25f - 0.25f * q)); air = 4f * q * (1f - q); cycleU = q * 0.999f; }
                    else if (u < down) { float q = (u - fly) / (down - fly); e = flyShare + downShare * q; air = -0.12f * Mathf.Sin(Mathf.PI * q); cycleU = 0.999f; landing = true; }
                    else { float q = (u - down) / (1f - down); e = flyShare + downShare + (1f - flyShare - downShare) * (last ? 1f - (1f - q) * (1f - q) : q); air = 0.25f * Mathf.Sin(Mathf.PI * q) * boundHeight / bigLeapHeight; cycleU = q; }
                }
                else if (u < land0) { float q = u / land0; e = half * q; air = Mathf.Sin(Mathf.PI * q) * (1f - 0.35f * q); cycleU = q; }   // flat out, diving in
                else if (u < land1) { float q = (u - land0) / (land1 - land0); e = half; air = 0f; cycleU = 1f; landing = true; landQ = q; }
                else { float q = (u - land1) / (1f - land1); e = half + landShare + half * (last ? 1f - (1f - q) * (1f - q) : q * (0.35f + 0.65f * q) / 1f); air = Mathf.Sin(Mathf.PI * q) * (last ? 0.7f : 1.25f); cycleU = q; }
                var pos = Vector3.Lerp(cur.from, cur.to, e) + Vector3.up * hgt * air;
                // nose up on the way up, down on the way down
                float pitch = landing ? 0f :   // held: tilting the root on the ground lifted/sank the paws
                     -Mathf.Cos(Mathf.PI * cycleU) * (leapStyle == 1 && u < 0.62f ? 16f : 20f);
                // style 0: level out at the touch-down and the take-off, so the paws meet the floor (they went 3.5 m into it
                // nose-down at the touch-down and the body popped up a frame later)
                if (leapStyle == 0 && !landing) pitch *= Mathf.SmoothStep(0f, 1f, cycleU / 0.15f) * (1f - Mathf.SmoothStep(0.8f, 1f, cycleU));
                transform.SetPositionAndRotation(pos, Quaternion.AngleAxis(pitch, Vector3.Cross(Vector3.up, dir)) * rot);
                Airborne = !landing;
                // 11-20 Kido ("뛰는 거 역동성이 안 보여"): a looping gallop cycle read as paddling. The leap is now three clear shapes:
                // coiled (the play bow, chest down, rump up) -> fully stretched in the air (front paws reaching, hind legs
                // trailing) -> front paws reaching down and the body crushing into the bow again on landing.
                if (crouchClip && dashClip)
                {
                    if (landing)
                    {   // slam flat, then gather: the rump comes up and wiggles before the second leap
                        crouchClip.SampleAnimation(gameObject, crouchClip.length * 0.4f);   // the same coil the leap ends and starts in: no pop, no wiggle
                    }
                    else
                    {
                        float stretch = Mathf.SmoothStep(0f, 1f, cycleU / 0.14f) * (1f - 0.9f * Mathf.SmoothStep(0.72f, 1f, cycleU));
                        if (gallopClip) Pose(gallopClip, (gallopT0 + cycleU * gallopClip.length) % gallopClip.length, dashClip, dashClip.length * 0.5f, 0.5f);
                        else dashClip.SampleAnimation(gameObject, dashClip.length * 0.5f);
                        StoreThenBlendFrom(crouchClip, crouchClip.length * 0.4f, stretch);
                    }
                }
                else if (gallopClip) gallopClip.SampleAnimation(gameObject, (gallopT0 + cycleU * gallopClip.length) % gallopClip.length);
                else if (dashClip) dashClip.SampleAnimation(gameObject, Mathf.Min(u * dashTime, dashClip.length));
                Shake = landing ? Mathf.Max(0f, 1f - landQ * 3.5f) : u > 0.96f && last ? 0.6f : 0f;
                // squash and stretch along the run: long in the air, flattened wide on the slam, springing back
                if (landing)
                {   // on the ground only the height squashes (scaling along the body moved the planted paws = sliding):
                    // the hit flattens it, it rises a little, then sinks to load up just before it springs
                    float hit = Mathf.Lerp(0.74f, 0.95f, Mathf.SmoothStep(0f, 0.35f, landQ));
                    float load = Mathf.Lerp(1f, 0.8f, Mathf.SmoothStep(0.5f, 1f, landQ));
                    ApplySquashY(hit * load);
                    // 11-20 probe: tipped nose-down the front paws sat 2 m under the floor and the hind paws floated; level
                    // the paws on the floor (lowest paw at 0) - pose and pitch are held, so this only follows the squash
                    if (allToes != null)
                    {
                        float lo = float.MaxValue; foreach (var tt in allToes) if (tt) lo = Mathf.Min(lo, tt.position.y);
                        if (lo < float.MaxValue) transform.position -= Vector3.up * lo;
                    }
                }
                else ApplySquash(1f + 0.16f * Mathf.Sin(Mathf.PI * cycleU) * (cycleU < 0.1f ? 1.6f : 1f));   // shoots out long off the mark
                if (landing && !ringed) { ringed = true; if (landingRing) { ringT = t; ringPos = new Vector3(pos.x, 0.15f, pos.z); } }
                if (!landing) ringed = false;
                if (last && u > 0.85f && crouchClip) crouchClip.SampleAnimation(gameObject, crouchClip.length * 0.3f * (1f - (u - 0.85f) / 0.15f) + 0.0001f);
                stripA = 1f - u * 0.6f; fillU = 1f;
                var gpos = new Vector3(pos.x, 0, pos.z);
                if (landing && !landed) { landed = true; Burst(gpos, 34); }                       // the slam: fog blown out all round
                if (!landing) landed = false;
                if (u < 0.08f || landing || u > 0.94f) EmitDust(gpos, dir, 2.5f, leapStyle == 1 && landing ? 22 : 14);          // push-off, landing, arrival
                else EmitDust(gpos, dir, len / dashTime / 30f, 3);
                if (!cur.hit && player)
                {
                    var pp = PlayerPos - gpos; pp.y = 0;
                    if (pp.magnitude < stripWidth * 0.5f) { cur.hit = true; onHit?.Invoke(pos); }
                }
            }
            else
            {   // gone into the fog (or home again)
                if (last) { transform.SetPositionAndRotation(cur.to, rot); if (standClip) standClip.SampleAnimation(gameObject, (lt - telegraphTime - dashTime) % standClip.length); if (lt > cyc + fogOut) Running = false; }
                else vis = false;
            }
            // 11-19 Kido ("텔레그래프는 좀 더 진하고 빨간 확실한 라인"): a solid dark-red band, a saturated red fill that runs along
            // it, and two hard bright border lines
            var sideV = Vector3.Cross(Vector3.up, dir) * (stripWidth * 0.5f);
            if (k == 0 && walkClip) stripA = 0f;   // no strip while it backs away
            DrawStrip(strip, cur.from, cur.to, stripWidth, stripA * 0.55f, new Color(0.55f, 0.0f, 0.0f));
            DrawStrip(fill, cur.from, Vector3.Lerp(cur.from, cur.to, fillU), stripWidth, stripA * 0.8f, new Color(1f, 0.02f, 0.02f));
            DrawStrip(edgeA, cur.from + sideV, cur.to + sideV, 0.55f, stripA > 0.01f ? 1f : 0f, new Color(telegraphColor.r, telegraphColor.g, telegraphColor.b));
            DrawStrip(edgeB, cur.from - sideV, cur.to - sideV, 0.55f, stripA > 0.01f ? 1f : 0f, new Color(telegraphColor.r, telegraphColor.g, telegraphColor.b));
        }
        if (cur == null) { strip.enabled = false; fill.enabled = false; edgeA.enabled = false; edgeB.enabled = false; }
        Visible = vis;
        if (Application.isPlaying && rends != null) foreach (var r in rends) if (r) r.enabled = vis;
        if (!Application.isPlaying)
        {
            float step = lastSim < 0 ? 0f : t - lastSim;
            if (step > 0) { fog.Simulate(step, true, false, false); dust.Simulate(step, true, false, false); }
        }
        lastSim = t;
    }

    void ApplySquashY(float y)
    {   // height only, about the root (on the floor): the paws stay exactly where they are
        transform.localScale = new Vector3(baseScale.x, baseScale.y * y, baseScale.z);
    }

    void ApplySquash(float along)
    {   // scale along the model's own forward axis, the opposite across it (volume kept, roughly)
        float across = 1f / Mathf.Sqrt(Mathf.Max(0.2f, along));
        var a = new Vector3(Mathf.Abs(fwdLocal.x), 0f, Mathf.Abs(fwdLocal.z));
        var k = new Vector3(Mathf.Lerp(across, along, a.x), Mathf.Lerp(1f, across, 0.5f), Mathf.Lerp(across, along, a.z));
        transform.localScale = Vector3.Scale(baseScale, k);
    }

    // the shock ring of a landing: a red line racing out over the floor
    void DrawRing(float t)
    {
        if (ring == null) return;
        float age = (t - ringT) / 0.55f;
        ring.enabled = age >= 0f && age < 1f; if (!ring.enabled) return;
        float e = 1f - (1f - age) * (1f - age), rad = Mathf.Lerp(3f, 26f, e);
        for (int i = 0; i < 64; i++) { float a = i / 64f * Mathf.PI * 2f; ring.SetPosition(i, ringPos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * rad); }
        ring.widthMultiplier = Mathf.Lerp(2.2f, 0.4f, age);
        var c = new Color(telegraphColor.r, telegraphColor.g * 3f, telegraphColor.b * 2f, (1f - age) * (1f - age)); ring.startColor = c; ring.endColor = c;
    }

    // keep the current pose, sample clip a, and blend a -> kept pose by w (w = 1 keeps the pose as it was)
    void StoreThenBlendFrom(AnimationClip a, float ta, float w)
    {
        if (w >= 0.999f) return;
        for (int i = 0; i < poseBones.Length; i++) { poseP[i] = poseBones[i].localPosition; poseR[i] = poseBones[i].localRotation; }
        a.SampleAnimation(gameObject, ta);
        if (w <= 0.001f) return;
        for (int i = 0; i < poseBones.Length; i++)
        {
            poseBones[i].localPosition = Vector3.Lerp(poseBones[i].localPosition, poseP[i], w);
            poseBones[i].localRotation = Quaternion.Slerp(poseBones[i].localRotation, poseR[i], w);
        }
    }

    // sample clip a, then blend every bone toward clip b by w
    void Pose(AnimationClip a, float ta, AnimationClip b, float tb, float w)
    {
        a.SampleAnimation(gameObject, ta);
        if (w <= 0.001f) return;
        for (int i = 0; i < poseBones.Length; i++) { poseP[i] = poseBones[i].localPosition; poseR[i] = poseBones[i].localRotation; }
        b.SampleAnimation(gameObject, tb);
        if (w >= 0.999f) return;
        for (int i = 0; i < poseBones.Length; i++)
        {
            poseBones[i].localPosition = Vector3.Lerp(poseP[i], poseBones[i].localPosition, w);
            poseBones[i].localRotation = Quaternion.Slerp(poseR[i], poseBones[i].localRotation, w);
        }
    }

    // a ring of fog thrown out along the ground from an impact
    void OnDestroy() { if (sheetRoot != null) { if (Application.isPlaying) Destroy(sheetRoot.gameObject); else DestroyImmediate(sheetRoot.gameObject); } }

    void BuildSheets(Transform parent)
    {
        // one annulus mesh, shared by every sheet: from just inside the clearing out past the arena
        float r0 = Mathf.Max(1f, fogInner - 8f), r1 = arenaRadius + 30f; int seg = 128, rings = 6;
        var verts = new List<Vector3>(); var tris = new List<int>();
        for (int j = 0; j <= rings; j++)
        {
            float rr = Mathf.Lerp(r0, r1, Mathf.Pow(j / (float)rings, 1.6f));
            for (int i = 0; i <= seg; i++) { float a = i / (float)seg * Mathf.PI * 2f; verts.Add(new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr)); }
        }
        for (int j = 0; j < rings; j++)
            for (int i = 0; i < seg; i++)
            {
                int a = j * (seg + 1) + i, b = a + seg + 1;
                tris.Add(a); tris.Add(b); tris.Add(a + 1); tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
            }
        var mesh = new Mesh { name = "FogRing", hideFlags = HideFlags.DontSave };
        mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(r1 * 2f, fogTop + 2f, r1 * 2f));
        for (int k = 0; k < fogSheets; k++)
        {
            float layer = fogSheets > 1 ? k / (float)(fogSheets - 1) : 0f;
            var go = new GameObject("FogDashFX_Sheet" + k) { hideFlags = HideFlags.DontSave }; go.transform.SetParent(parent, false);
            go.transform.position = arenaCentre + Vector3.up * Mathf.Lerp(0.4f, fogTop, Mathf.Pow(layer, 1.3f));
            go.transform.rotation = Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = fogSheetMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            var mpb = new MaterialPropertyBlock();
            mpb.SetFloat("_Layer", layer); mpb.SetVector("_Centre", new Vector4(arenaCentre.x, arenaCentre.y, arenaCentre.z, fogInner));
            mr.SetPropertyBlock(mpb); mr.sortingOrder = k;
        }
    }

    void Burst(Vector3 pos, int count)
    {
        var e = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            float a = (i + Random.value) / count * Mathf.PI * 2f; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            e.position = pos + d * Random.Range(2f, 4f) + Vector3.up * Random.Range(0.6f, 1.8f);
            e.velocity = d * Random.Range(14f, 24f) + Vector3.up * Random.Range(0.5f, 3f);
            e.startSize = Random.Range(7f, 12f); e.startLifetime = Random.Range(0.9f, 1.6f); e.rotation = Random.Range(0f, 360f);
            float v = Random.Range(0.85f, 1.2f); e.startColor = new Color(fogColor.r * v, fogColor.g * v, fogColor.b * v, fogColor.a);
            dust.Emit(e, 1);
        }
    }

    void DrawStrip(LineRenderer lr, Vector3 a, Vector3 b, float w, float alpha, Color rgb)
    {
        lr.enabled = alpha > 0.005f; if (!lr.enabled) return;
        a.y = b.y = 0.12f; lr.SetPosition(0, a); lr.SetPosition(1, b); lr.widthMultiplier = w;
        var c = rgb; c.a = alpha; lr.startColor = c; lr.endColor = c;
    }

    void EmitFog()
    {
        if (fogSheetMaterial) return;   // the sheets are the bank
        var e = new ParticleSystem.EmitParams(); float life = Duration;
        var r = new System.Random(seed * 31 + 5);
        System.Func<float, float, float> R = (a, b) => a + (float)r.NextDouble() * (b - a);
        // 11-19 Kido ("안개 훨씬 진하게"): a wall of soft fog sprites (downloaded pack, T_Fog_Main), several times as many and
        // stacked in layers, so nothing shows through the ring; a thick bank right at its inner edge makes the wall read
        int fogCount = Mathf.Clamp(Mathf.RoundToInt(2300f * (arenaRadius + 24f) / 46f), 2300, 4200);   // a wider ring needs more of it
        for (int i = 0; i < fogCount; i++)
        {
            bool bank = i % 4 == 0;
            float a = R(0, Mathf.PI * 2f);
            float ragged = (2.5f * Mathf.Sin(a * 3f + 1.3f) + 1.5f * Mathf.Sin(a * 7f)) * Mathf.Clamp01(fogInner / 16f);          // the clear patch is not a neat circle
            float inner = fogInner + ragged;
            float rad = bank ? R(inner + 1.5f, inner + 9f) : Mathf.Sqrt(R(inner * inner, (arenaRadius + 24f) * (arenaRadius + 24f)));
            // 11-20: with a small clear patch (reference size) the tall, wide sprites of the bank hid the whole floor from the
            // tilted camera. The fog now starts low and fine at the lip and builds up to full height over 16 m.
            float into = rad - inner;
            float edge = Mathf.InverseLerp(0f, 16f, into);
            float hgt = R(0.6f, 1.6f + 10.5f * edge);
            e.position = arenaCentre + new Vector3(Mathf.Cos(a) * rad, hgt, Mathf.Sin(a) * rad);
            e.velocity = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * R(0.5f, 1.8f) + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * R(-0.25f, 0.1f);   // rolls round, creeping inward
            e.startSize = Mathf.Lerp(R(4f, 7f), R(13f, 26f), edge); e.startLifetime = life * R(0.94f, 1f); e.rotation = R(0, 360);
            // lit from above: the tops are paler, the low fog darker and bluer
            float top = Mathf.InverseLerp(1f, 10f, hgt); float v = R(0.7f, 1.0f) * Mathf.Lerp(0.6f, 1.25f, top);
            e.startColor = new Color(fogColor.r * v, fogColor.g * v, fogColor.b * v, fogColor.a * Mathf.Lerp(0.75f, 1f, edge));
            fog.Emit(e, 1);
        }
        // a thin low haze across the middle, so the floor still reads
        for (int i = 0; i < 14; i++)
        {
            float a = R(0, Mathf.PI * 2f), rad = Mathf.Sqrt(R(0, fogInner * fogInner));
            e.position = arenaCentre + new Vector3(Mathf.Cos(a) * rad, R(0.8f, 2.5f), Mathf.Sin(a) * rad);
            e.velocity = new Vector3(R(-0.4f, 0.4f), 0, R(-0.4f, 0.4f));
            e.startSize = R(6f, 10f); e.startLifetime = life * R(0.94f, 1f); e.rotation = R(0, 360);
            e.startColor = new Color(fogColor.r, fogColor.g, fogColor.b, fogColor.a * 0.08f);
            fog.Emit(e, 1);
        }
    }

    void EmitDust(Vector3 pos, Vector3 dir, float stepLen, int count)
    {   // the fog the boss drags out of the bank and across the floor (in the reference the wolf arrives wrapped in it)
        var e = new ParticleSystem.EmitParams(); var side = Vector3.Cross(Vector3.up, dir);
        for (int i = 0; i < count; i++)
        {
            float sd = Random.Range(-1f, 1f);
            e.position = pos - dir * Random.Range(0f, stepLen + 3f) + side * sd * 4f + Vector3.up * Random.Range(0.8f, 4.5f);
            e.velocity = side * sd * Random.Range(0.5f, 3f) + Vector3.up * Random.Range(0.2f, 1.2f) + dir * Random.Range(3f, 9f);
            e.startSize = Random.Range(8f, 14f); e.startLifetime = Random.Range(1.4f, 2.4f); e.rotation = Random.Range(0f, 360f);
            float v = Random.Range(0.75f, 1.1f); e.startColor = new Color(fogColor.r * v, fogColor.g * v, fogColor.b * v, fogColor.a * 0.8f);
            dust.Emit(e, 1);
        }
    }
}
