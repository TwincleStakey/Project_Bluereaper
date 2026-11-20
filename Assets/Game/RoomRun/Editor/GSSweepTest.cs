// 11-15 sweep fix check (Kido: body jitters back and forth while slashing, slashes not wide, body stays on the cursor
// while running sideways). rr shots token "grass#": cursor fixed north; runs 8 directions, then a held 3-hit combo standing,
// then the combo while moving east. Every captured frame logs root / hips / feet / blade tip / grip in world space to
// ArtSource/GSHero/ingame_shots/sweep_fix/<tag>_log.csv (frame numbers match rr_clip/<tag>_NNN.jpg).
using System.IO;
using UnityEngine;

namespace RoomRun
{
    public sealed class SweepTest : ICommandSource
    {
        readonly float t0 = Time.time;
        public float T => Time.time - t0;
        public string Phase { get; private set; } = "";
        public const float RunEnd = 10.2f, StandCombo = 10.8f, MoveCombo = 14.2f, Turns = 16.6f, End = 21.2f;

        // 11-16 VFX size check (token vfxs=1): standing, cursor north, 0.8 s idle, a 3-hit combo, 0.8 s idle, a second combo.
        // Fire is held 0.5 s only, so the buffer chains hit 1 -> 2 -> 3 and the combo stops after hit 3.
        public static bool VfxMode;
        public const float VfxA = 0.8f, VfxB = 2.95f;

        // 11-16 wide sweep capture (token wide=N, cursor fixed north): 1 = two 3-hit combos at close zoom (z0.8), two at the
        // default zoom (1.6), then a walk N / E / S / W at close zoom; 2 = the walk only (close); 3 = one combo at the default
        // zoom (with enemies round the player from "calm=1 grass+25*"). Fire is held 0.5 s, so the buffer chains 1 -> 2 -> 3.
        public static int WideMode;
        public static float ComboHold = 0.5f;   // s the fire button is held per combo (token hold=<s>; 1.7 chains all three V Rising-paced hits)
        public const float WideClose = 0.8f, WideDefault = 1.6f;
        static readonly float[] WideCombos1 = { 0.6f, 3.4f, 6.6f, 9.4f };
        public const float WideWalk1 = 12.4f, WideWalkLen = 1.4f;
        static void Zoom(float z) { var rig = RunDirector.Instance ? RunDirector.Instance.Rig : null; if (rig && Mathf.Abs(rig.Zoom - z) > 1e-3f) { rig.Zoom = z; rig.Snap(); } }

        // 11-16 single big hit capture: 4 = three single swings close (z0.8), two at the default zoom (1.6), then the walk
        // N / E / S / W close (walking hand check); 5 = one swing at the default zoom (enemies from "calm=1 grass+25*");
        // 6 = the walk only, close. Fire is a 0.15 s click, so each click is exactly one hit 1.
        static readonly float[] Single4 = { 0.6f, 2.3f, 4.0f, 6.2f, 7.9f };
        public const float SingleWalk4 = 9.8f;
        public static bool AimFollowMove;   // shots token aimmove=1
        public static float HandZoom = 0.3f;   // mode 6: tight on the hero for the walking-hand check
        PlayerCommand ReadSingle(float t, PlayerCommand c)
        {
            float walkAt = WideMode == 4 ? SingleWalk4 : WideMode == 6 || WideMode == 7 ? 0.6f : 99f;
            // 11-17 mode 7: the walk in 8 directions (N, NE, E, SE, S, SW, W, NW), close
            if (WideMode == 7)
            {
                Zoom(HandZoom);
                if (t >= walkAt && t < walkAt + 8f * WideWalkLen)
                {
                    int k = (int)((t - walkAt) / WideWalkLen);
                    float ang = k * 45f * Mathf.Deg2Rad;
                    string[] n8 = { "walkN", "walkNE", "walkE", "walkSE", "walkS", "walkSW", "walkW", "walkNW" };
                    c.move = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang)); Phase = n8[k];
                    if (AimFollowMove) c.aimDir = new Vector3(c.move.x, 0f, c.move.y);   // 11-20: the cursor turns with the walk (pose per direction)
                    return c;
                }
                Phase = "idle"; return c;
            }
            if (WideMode == 4) Zoom(t < 5.6f || t >= walkAt - 0.4f ? WideClose : WideDefault);
            else Zoom(WideMode == 6 ? HandZoom : WideDefault);
            var clicks = WideMode == 4 ? Single4 : WideMode == 5 ? new[] { 1.0f } : new float[0];
            foreach (var a in clicks) if (t >= a && t < a + 0.15f) { Phase = "hit"; c.fire = true; return c; }
            if (t >= walkAt && t < walkAt + 4f * WideWalkLen)
            {
                int i = (int)((t - walkAt) / WideWalkLen);
                Vector2[] d = { new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(-1f, 0f) };
                string[] n = { "walkN", "walkE", "walkS", "walkW" };
                c.move = d[i]; Phase = n[i]; return c;
            }
            Phase = gs && gs.Attacking ? "swing" : "idle";
            return c;
        }

        PlayerCommand ReadWide(float t, PlayerCommand c)
        {
            // 11-19 mode 8 (Kido: the legs keep WASD while the greatsword swings): fire held the whole time (chained 3-hit
            // combos), cursor north, running N (forward), E (strafe), S (backpedal), W, NE, SW, 1.5 s each, default zoom
            // 11-19 mode 9 (gs_legs_v3 video): close zoom, cursor north; run N 1.5 s without attacking, then N with fire held
            // 3 s, then E 1.5 s plain and 3 s attacking
            if (WideMode == 9)
            {
                Zoom(WideClose);
                float u = t - 0.4f;
                float[] ends = { 1.5f, 4.5f, 6f, 9f };
                Vector2[] dv = { new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(1f, 0f) };
                string[] nm = { "runN", "atkN", "runE", "atkE" };
                for (int k = 0; k < ends.Length; k++)
                    if (u >= 0f && u < ends[k]) { c.move = dv[k]; c.fire = (k & 1) == 1; Phase = nm[k]; return c; }
                Phase = "idle"; return c;
            }
            if (WideMode == 8)
            {
                Zoom(WideDefault);
                float u = t - 0.4f;
                Vector2[] d8 = { new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(-1f, 0f), new Vector2(0.7071f, 0.7071f), new Vector2(-0.7071f, -0.7071f) };
                string[] n8 = { "atkN", "atkE", "atkS", "atkW", "atkNE", "atkSW" };
                int i = u < 0f ? -1 : (int)(u / 1.5f);
                if (i >= 0 && i < d8.Length) { c.move = d8[i]; c.fire = true; Phase = n8[i]; return c; }
                Phase = "idle"; return c;
            }
            if (WideMode >= 4) return ReadSingle(t, c);
            float walkAt = WideMode == 1 ? WideWalk1 : WideMode == 2 ? 0.6f : 99f;
            if (WideMode == 1) Zoom(t < 6.2f || t >= walkAt - 0.4f ? WideClose : WideDefault);
            else Zoom(WideMode == 2 ? WideClose : WideDefault);
            var combos = WideMode == 1 ? WideCombos1 : WideMode == 3 ? new[] { 1.0f } : new float[0];
            foreach (var a in combos) if (t >= a && t < a + ComboHold) { Phase = "combo"; c.fire = true; return c; }
            if (t >= walkAt && t < walkAt + 4f * WideWalkLen)
            {
                int i = (int)((t - walkAt) / WideWalkLen);
                Vector2[] d = { new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(-1f, 0f) };
                string[] n = { "walkN", "walkE", "walkS", "walkW" };
                c.move = d[i]; Phase = n[i]; return c;
            }
            Phase = "idle";
            return c;
        }

        public PlayerCommand Read(PlayerController p, Camera cam)
        {
            float t = T;
            var c = new PlayerCommand { aimDir = Vector3.forward };
            if (WideMode > 0) return ReadWide(t, c);
            if (VfxMode)
            {
                bool fireA = t >= VfxA && t < VfxA + 0.5f, fireB = t >= VfxB && t < VfxB + 0.5f;
                Phase = fireA || fireB ? "combo" : "idle";
                c.fire = fireA || fireB;
                return c;
            }
            if (t < 0.6f) { Phase = "stand"; return c; }
            if (t < RunEnd)
            {
                float u = t - 0.6f; int i = (int)(u / 1.2f);
                if (i < 8 && u - i * 1.2f < 1f)
                {
                    float a = i * 45f * Mathf.Deg2Rad;
                    c.move = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                    Phase = "run" + (i * 45);
                }
                else Phase = "stop";
                return c;
            }
            if (t < StandCombo) { Phase = "stop"; return c; }
            if (t < StandCombo + 1.75f) { Phase = "comboStand"; c.fire = true; return c; }
            if (t < MoveCombo) { Phase = "recover"; return c; }
            if (t < MoveCombo + 1.75f) { Phase = "comboMoveE"; c.fire = true; c.move = new Vector2(1f, 0f); return c; }
            if (t < Turns) { Phase = "runE"; c.move = new Vector2(1f, 0f); return c; }
            // direction changes with the cursor held north: east, north, sharp reverse to south-west, stop
            if (t < Turns + 1.2f) { Phase = "turnE"; c.move = new Vector2(1f, 0f); return c; }
            if (t < Turns + 2.4f) { Phase = "turnN"; c.move = new Vector2(0f, 1f); return c; }
            if (t < Turns + 3.2f) { Phase = "turnSW"; c.move = new Vector2(-0.7071f, -0.7071f); return c; }
            Phase = "stop";
            return c;
        }

        // ---- per-frame log ----
        StreamWriter log;
        Animator an;
        GreatswordCombat gs;
        Transform tipT, vis;
        float bladeLen = 1.2f;
        Vector3 faceAxis = Vector3.right;

        public void OpenLog(string path, PlayerController p)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            log = new StreamWriter(path, false);
            an = p.GetComponentInChildren<Animator>();
            gs = p.GetComponent<GreatswordCombat>();
            vis = an ? an.transform.parent : null;
            foreach (var t in p.GetComponentsInChildren<Transform>()) if (t.name == "Greatsword") { tipT = t; break; }
            if (tipT) bladeLen = BladeTrail.MeasureBlade(tipT);
            // 11-16 flat hold check: the blade's face normal = the thinner of the sword meshes' local X / Y extents
            faceAxis = Vector3.right;
            if (tipT)
            {
                var mn = Vector3.one * 1e9f; var mx = -mn;
                foreach (var mf in tipT.GetComponentsInChildren<MeshFilter>())
                {
                    if (!mf.sharedMesh) continue;
                    var b = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = tipT.InverseTransformPoint(mf.transform.TransformPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z)));
                        mn = Vector3.Min(mn, c); mx = Vector3.Max(mx, c);
                    }
                }
                faceAxis = (mx.x - mn.x) < (mx.y - mn.y) ? Vector3.right : Vector3.up;
                Debug.Log($"[RoomRun] sweep log: sword extents {mx - mn}, face normal local {faceAxis}");
                foreach (var r in tipT.GetComponentsInChildren<Renderer>(true))
                {
                    var m = r.sharedMaterial;
                    Texture t = m && m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                    Debug.Log($"[RoomRun] sword renderer {r.name}: enabled {r.enabled}, mats {r.sharedMaterials.Length}, mat {(m ? m.name : "null")}, shader {(m && m.shader ? m.shader.name : "-")}, _BaseMap {(t ? t.name + " " + t.width : "none")}, MPB {r.HasPropertyBlock()}, _Steel {(m && m.HasProperty("_Steel") ? m.GetFloat("_Steel") : -1f)}");
                }
            }
            log.WriteLine("frame,time,phase,step,stepT,animT,rootX,rootZ,hipsX,hipsY,hipsZ,lfX,lfY,lfZ,rfX,rfY,rfZ,gripX,gripY,gripZ,tipX,tipY,tipZ,visYaw,velX,velZ,timeScale,tipR,vfxR,vfxScale,lights,hipsYaw,chestYaw,bladeYaw,layerW,spinYaw,clipF,stretch,bladePitch,chestY,gripSlip,wristBend,bladeOff,bladeFlat,bladeLowY,flatRoll,longToBody,faceToCam");
        }

        readonly System.Collections.Generic.Dictionary<Mesh, float> meshR = new System.Collections.Generic.Dictionary<Mesh, float>();
        ParticleSystem.Particle[] parts = new ParticleSystem.Particle[0];

        public void LogFrame(int f, PlayerController p)
        {
            if (log == null || !an) return;
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            Vector3 P(HumanBodyBones b) { var tr = an.GetBoneTransform(b); return tr ? tr.position : Vector3.zero; }
            var root = p.transform.position;
            var hips = P(HumanBodyBones.Hips); var lf = P(HumanBodyBones.LeftFoot); var rf = P(HumanBodyBones.RightFoot);
            var grip = tipT ? tipT.position : Vector3.zero; var tip = tipT ? tipT.position + tipT.forward * bladeLen : Vector3.zero;
            float animT = -1f;
            int L = an.GetLayerIndex("AttackFull");
            if (L >= 0) { var st = an.GetCurrentAnimatorStateInfo(L); animT = st.normalizedTime; }
            var fw = vis ? vis.forward : Vector3.forward;
            var v = p.Velocity;
            // tip radius = horizontal distance root -> blade tip; vfx radius = the farthest axis extent of the live slash
            // particles' bounds round the root (the arc crosses both axes, so that is its outer radius); lights = enabled
            // Light components anywhere (particle Lights module copies included), directional excluded
            var dt = tip - root; dt.y = 0f;
            float vr = 0f, vs = 0f;
            foreach (var pr in Object.FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None))
            {
                if (!pr || !pr.enabled || !pr.transform.root.name.EndsWith("(pool)")) continue;
                var ps = pr.GetComponent<ParticleSystem>();
                if (!ps || ps.particleCount == 0 || pr.renderMode != ParticleSystemRenderMode.Mesh) continue;
                // exact: farthest mesh vertex (mesh space) x particle size x the pool's scale, plus the particle's offset
                var mesh = pr.mesh;
                if (!mesh) continue;
                if (!meshR.TryGetValue(mesh, out float mr))
                {
                    mr = 0f;
                    if (mesh.isReadable) foreach (var vtx in mesh.vertices) mr = Mathf.Max(mr, vtx.magnitude);
                    else mr = mesh.bounds.extents.magnitude;
                    meshR[mesh] = mr;
                }
                if (parts.Length < ps.main.maxParticles) parts = new ParticleSystem.Particle[ps.main.maxParticles];
                int n = ps.GetParticles(parts);
                float ls = Mathf.Abs(ps.transform.lossyScale.x);
                for (int i = 0; i < n; i++)
                {
                    var sz = parts[i].GetCurrentSize3D(ps);
                    float r = mr * Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z)) * ls;
                    var pp = ps.main.simulationSpace == ParticleSystemSimulationSpace.World ? parts[i].position : ps.transform.TransformPoint(parts[i].position);
                    pp -= root; pp.y = 0f;
                    vr = Mathf.Max(vr, r + pp.magnitude);
                }
                vs = Mathf.Max(vs, Mathf.Abs(pr.transform.root.localScale.x));
            }
            int nl = 0;
            foreach (var l in Resources.FindObjectsOfTypeAll<Light>())
                if (l && l.enabled && l.gameObject.activeInHierarchy && l.type != LightType.Directional && !UnityEditor.EditorUtility.IsPersistent(l)) nl++;
            log.WriteLine(string.Format(ic, "{0},{1:0.000},{2},{3},{4:0.000},{5:0.000},{6:0.0000},{7:0.0000},{8:0.0000},{9:0.0000},{10:0.0000},{11:0.0000},{12:0.0000},{13:0.0000},{14:0.0000},{15:0.0000},{16:0.0000},{17:0.0000},{18:0.0000},{19:0.0000},{20:0.0000},{21:0.0000},{22:0.0000},{23:0.0},{24:0.00},{25:0.00},{26:0.00},{27:0.000},{28:0.000},{29:0.00},{30}",
                f, T, Phase, gs ? gs.StepIndex : -1, gs ? gs.StepT : 0f, animT, root.x, root.z, hips.x, hips.y, hips.z, lf.x, lf.y, lf.z, rf.x, rf.y, rf.z,
                grip.x, grip.y, grip.z, tip.x, tip.y, tip.z, Mathf.Atan2(fw.x, fw.z) * Mathf.Rad2Deg, v.x, v.z, Time.timeScale,
                dt.magnitude, vr, vs, nl) + Extra(p));
        }

        // 11-16 wide sweep: body / chest / blade yaw in world (0 = +Z, + = east) to compare with the clip sampled in edit mode
        string Extra(PlayerController p)
        {
            float Yw(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 1e-8f ? 0f : Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg; }
            var h = an.GetBoneTransform(HumanBodyBones.Hips);
            var l = an.GetBoneTransform(HumanBodyBones.LeftUpperArm); var r = an.GetBoneTransform(HumanBodyBones.RightUpperArm);
            float chest = l && r ? Yw(Vector3.Cross(r.position - l.position, Vector3.up)) : 0f;
            int L = an.GetLayerIndex("AttackFull");
            var st = p.GetComponent<ArmStretch>(); var sb = p.GetComponent<SplitBody>();
            var uc = an.GetBoneTransform(HumanBodyBones.UpperChest) ?? an.GetBoneTransform(HumanBodyBones.Chest);
            float pitch = tipT ? Mathf.Asin(Mathf.Clamp(tipT.forward.y, -1f, 1f)) * Mathf.Rad2Deg : 0f;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, ",{0:0.0},{1:0.0},{2:0.0},{3:0.00},{4:0.0},{5:0.0},{6:0.00},{7:0.0},{8:0.000},{9:0.0},{10:0.0},{11:0.0},{12:0.00},{13:0.000},{14:0.0},{15:0.00},{16:0.00}",
                h ? Yw(h.forward) : 0f, chest, tipT ? Yw(tipT.forward) : 0f, L >= 0 ? an.GetLayerWeight(L) : -1f, gs ? gs.SpinYaw : 0f,
                gs ? gs.ClipFrame : -1f, st ? st.Amount : 0f, pitch, uc ? uc.position.y : 0f, sb ? sb.GripSlip : 0f, sb ? sb.WristBend : 0f, sb ? sb.BladeOffset : 0f,
                tipT ? Mathf.Abs(Vector3.Dot(tipT.TransformDirection(faceAxis), Vector3.up)) : 0f, tipT ? Mathf.Min(tipT.position.y, (tipT.position + tipT.forward * bladeLen).y) - p.transform.position.y : 0f,
                sb ? sb.FlatRollDeg : 0f, sb ? sb.LongToBody : 0f, sb ? sb.FaceToCam : 0f);
        }

        public void CloseLog() { if (log != null) { log.Close(); log = null; } }
    }
}
