/* Steers the eye-flame particles so they never pass through the hat or the head:
   they leave the eyes forward, slide out under the brim, rise past the rim and flow back over the hat. */
using UnityEngine;

namespace GSHero
{
    // after LSPoseFix (100): it straightens the spine in LateUpdate, so the hat must be read in its final pose (11-15:
    // run before it, the flames were steered against the leaned head and ended up under the brim in front of the face)
    [DefaultExecutionOrder(200)]
    public class GSEyeFlameFlow : MonoBehaviour
    {
        public Transform head;
        public ParticleSystem[] systems;
        public Vector3 apexLocal, axisLocal = Vector3.up, forwardLocal = Vector3.forward, headCenterLocal;
        public float slope = 0.62f, rimRadius = 0.2f, headRadius = 0.115f, margin = 0.022f;
        public float buoyancy = 3.2f, backPull = 5.0f, outPush = 2.4f, drag = 4.0f, hug = 4.5f;
        // distances/forces above are at character scale 1; scaleRoot's lossy y / builtScale rescales them (in-game player)
        public Transform scaleRoot;
        public float builtScale = 1f;
        // in-game (fast dashes and swings): lateCarry = share of the head's motion old particles still follow (0 = the
        // preview's free trail); maxDist = particles farther than this from the hat apex die (0 = off). Both scale with S.
        public float lateCarry = 0f, maxDist = 0f;

        ParticleSystem.Particle[] buf;
        Vector3 lastHead;
        bool has;

        void LateUpdate()
        {
            Step(Time.deltaTime);
        }

        public void Step(float dt)
        {
            if (!head || systems == null || dt <= 0f) return;
            Vector3 hv = has ? (head.position - lastHead) / dt : Vector3.zero;
            if (hv.sqrMagnitude > 900f) hv = Vector3.zero;
            lastHead = head.position;
            has = true;
            Vector3 A = head.TransformPoint(apexLocal);
            Vector3 U = head.TransformDirection(axisLocal).normalized;
            Vector3 F = head.TransformDirection(forwardLocal).normalized;
            Vector3 C = head.TransformPoint(headCenterLocal);
            float k = Mathf.Sqrt(1f + slope * slope);
            float damp = Mathf.Exp(-drag * dt);
            float S = scaleRoot ? Mathf.Abs(scaleRoot.lossyScale.y) / Mathf.Max(1e-4f, builtScale) : 1f;
            float rimRadius = this.rimRadius * S, headRadius = this.headRadius * S, margin = this.margin * S;
            float buoyancy = this.buoyancy * S, backPull = this.backPull * S, outPush = this.outPush * S, hug = this.hug * S;
            foreach (var ps in systems)
            {
                if (!ps) continue;
                int max = ps.main.maxParticles;
                if (buf == null || buf.Length < max) buf = new ParticleSystem.Particle[max];
                int n = ps.GetParticles(buf);
                for (int i = 0; i < n; i++)
                {
                    var p = buf[i];
                    float a = 1f - p.remainingLifetime / Mathf.Max(0.0001f, p.startLifetime);
                    Vector3 pos = p.position, vel = p.velocity;
                    float carry = 1f - (1f - lateCarry) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.32f, a));
                    pos += hv * dt * carry;
                    if (maxDist > 0f && (pos - A).sqrMagnitude > maxDist * maxDist * S * S) p.remainingLifetime = 0f;

                    Vector3 rel = pos - A;
                    float d = -Vector3.Dot(rel, U);
                    Vector3 rv = rel + U * d;
                    float r = rv.magnitude;
                    Vector3 rdir = r > 0.0005f ? rv / r : F;
                    float s = (slope * r - d) / k;
                    Vector3 nUp = (U + rdir * slope) / k;
                    bool under = s < 0f && r < rimRadius;

                    if (under)
                    {
                        if (s > -margin) pos -= nUp * (margin + s);
                        float vn = Vector3.Dot(vel, nUp);
                        if (vn > 0f) vel -= nUp * vn;
                        vel += (rdir + F * 0.7f - U * 0.15f).normalized * outPush * dt;
                    }
                    else
                    {
                        vel += U * buoyancy * dt;
                        if (r < rimRadius + 0.06f * S)
                        {
                            if (s < margin) pos += nUp * (margin - s);
                            float vn = Vector3.Dot(vel, nUp);
                            if (vn < 0f) vel -= nUp * vn;
                            if (s < 0.13f * S) vel -= nUp * hug * dt;
                        }
                        vel -= F * backPull * dt;
                    }

                    Vector3 hc = pos - C;
                    float hr = hc.magnitude;
                    if (hr < headRadius && hr > 0.0001f)
                    {
                        Vector3 hn = hc / hr;
                        pos = C + hn * headRadius;
                        float vn = Vector3.Dot(vel, hn);
                        if (vn < 0f) vel -= hn * vn;
                    }

                    vel *= damp;
                    p.position = pos;
                    p.velocity = vel;
                    buf[i] = p;
                }
                ps.SetParticles(buf, n);
            }
        }
    }
}
