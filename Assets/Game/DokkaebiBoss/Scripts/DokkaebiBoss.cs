using UnityEngine;

namespace Dokkaebi
{
    // Dokkaebi brute: walks toward the target, then slams the club into the ground.
    // The slam telegraphs where the club will land, and at the impact frame fires DokkaebiSlamFx there.
    [RequireComponent(typeof(Animator))]
    public sealed class DokkaebiBoss : MonoBehaviour
    {
        public Transform target;
        public DokkaebiSlamFx slamFx;
        public Renderer clubRenderer;

        [Header("Movement")]
        public float walkSpeed = 1.4f;
        public float turnSpeed = 140f;
        public float attackRange = 6f;
        public float attackCooldown = 2.2f;

        [Header("Slam timing (seconds into the slam clip)")]
        public float impactTime = 0.93f;
        public float slamLength = 2.27f;
        public float telegraphLead = 0.6f;
        public Vector3 localImpact = new Vector3(0f, 0f, 2.4f);   // filled by the builder from the clip

        public string walkState = "Walk";
        public string slamState = "Slam";

        Animator anim;
        float cooldown, attackT;
        bool attacking, telegraphed, impacted;

        void Awake()
        {
            anim = GetComponent<Animator>();
            anim.applyRootMotion = false;
            cooldown = attackCooldown * 0.5f;
            // decals and projectiles must stay where they land, not ride along with the boss
            if (slamFx && slamFx.transform.IsChildOf(transform) && Application.isPlaying) slamFx.transform.SetParent(null, true);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (attacking) { TickAttack(dt); return; }

            cooldown -= dt;
            if (!target) return;
            Vector3 to = target.position - transform.position; to.y = 0;
            float dist = to.magnitude;
            if (dist > 0.01f)
            {
                var want = Quaternion.LookRotation(to / dist, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * dt);
            }
            if (dist <= attackRange && cooldown <= 0f) { StartSlam(); return; }
            if (dist > attackRange * 0.6f) transform.position += transform.forward * walkSpeed * dt;
        }

        public void StartSlam()
        {
            attacking = true; telegraphed = false; impacted = false; attackT = 0f;
            anim.CrossFadeInFixedTime(slamState, 0.12f);
        }

        void TickAttack(float dt)
        {
            attackT += dt;
            if (!telegraphed && attackT >= impactTime - telegraphLead)
            {
                telegraphed = true;
                if (slamFx) slamFx.Telegraph(Ground(transform.TransformPoint(localImpact)), telegraphLead);
            }
            if (!impacted && attackT >= impactTime)
            {
                impacted = true;
                if (slamFx) slamFx.Impact(Ground(ClubHead()));
            }
            if (attackT >= slamLength)
            {
                attacking = false; cooldown = attackCooldown;
                anim.CrossFadeInFixedTime(walkState, 0.25f);
            }
        }

        Vector3 headLocal; bool headKnown;

        // The club mesh origin is the grip, so the head is the farthest bounds extent from it.
        public Vector3 ClubHead()
        {
            if (!clubRenderer) return transform.TransformPoint(localImpact);
            if (!headKnown)
            {
                headKnown = true;
                var mf = clubRenderer.GetComponent<MeshFilter>();
                var b = mf && mf.sharedMesh ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
                headLocal = Vector3.zero; float best = 0;
                for (int a = 0; a < 3; a++)
                {
                    if (Mathf.Abs(b.max[a]) > best) { best = Mathf.Abs(b.max[a]); headLocal = Vector3.zero; headLocal[a] = b.max[a] * 0.9f; }
                    if (Mathf.Abs(b.min[a]) > best) { best = Mathf.Abs(b.min[a]); headLocal = Vector3.zero; headLocal[a] = b.min[a] * 0.9f; }
                }
            }
            return clubRenderer.transform.TransformPoint(headLocal);
        }

        Vector3 Ground(Vector3 p) { p.y = transform.position.y; return p; }
    }
}
