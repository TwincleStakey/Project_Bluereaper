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

        public PlayerCommand Read(PlayerController p, Camera cam)
        {
            float t = T;
            var c = new PlayerCommand { aimDir = Vector3.forward };
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

        public void OpenLog(string path, PlayerController p)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            log = new StreamWriter(path, false);
            an = p.GetComponentInChildren<Animator>();
            gs = p.GetComponent<GreatswordCombat>();
            vis = an ? an.transform.parent : null;
            foreach (var t in p.GetComponentsInChildren<Transform>()) if (t.name == "Greatsword") { tipT = t; break; }
            if (tipT) bladeLen = BladeTrail.MeasureBlade(tipT);
            log.WriteLine("frame,time,phase,step,stepT,animT,rootX,rootZ,hipsX,hipsY,hipsZ,lfX,lfY,lfZ,rfX,rfY,rfZ,gripX,gripY,gripZ,tipX,tipY,tipZ,visYaw,velX,velZ,timeScale,tipR,vfxR,vfxScale,lights");
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
                dt.magnitude, vr, vs, nl));
        }

        public void CloseLog() { if (log != null) { log.Close(); log = null; } }
    }
}
