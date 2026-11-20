using UnityEngine;
using RoomRun;

namespace Hub
{
    // 거점 허수아비 (training dummy). Never dies: every hit refills its Health, flashes, and kicks a spring
    // that bends the straw body away from the player (pivot at the ankles), lags the head, flaps the tied
    // arms and swings the paper talismans hanging under them. Bones come from ArtSource/Scarecrow/scarecrow_rig.py.
    [DisallowMultipleComponent]
    public sealed class TrainingScarecrow : MonoBehaviour
    {
        [Header("Bones (filled by ScarecrowSetup)")]
        public Transform legs, spine, chest, head, armL, armR, talL, talR;
        [Tooltip("11-20: whole cross + body pivot at the foot of the post; takes swayShare of the lean")]
        public Transform sway;
        [Range(0, 1)] public float swayShare = 0.6f;
        public Renderer[] renderers;
        public Material hitFlash;

        [Header("Body spring")]
        [Tooltip("degrees of total lean per hit")] public float kick = 26f;
        public float maxLean = 34f;
        public float stiffness = 40f;
        public float damping = 3.2f;
        [Tooltip("share of the lean per bone: legs, spine, chest")] public Vector3 split = new Vector3(0.3f, 0.4f, 0.3f);

        [Header("Head / arms / talismans")]
        public float headLag = 0.9f;
        public float armFlap = 20f;
        public float talSwing = 2f;

        [Header("Ghost glow (11-20 Kido: 허수아비도 뭔가 빛을줘)")]
        public Light glowLight;
        [ColorUsage(false, true)] public Color glowColor = new Color(0.55f, 1f, 0.95f) * 2.2f;
        public float lightIntensity = 1.6f;
        public float pulseSpeed = 0.55f;
        [Tooltip("extra glow right after a hit, decays")] public float hitGlow = 1.2f;
        float hitGlowLeft;
        MaterialPropertyBlock mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        [Header("Idle breeze")]
        public float breeze = 1.2f;

        Health health;
        Vector2 lean, leanVel;            // x = world X tilt, y = world Z tilt (degrees, lean toward that direction)
        Vector2 headOff, headVel;
        float flapL, flapLVel, flapR, flapRVel;
        Vector2 talLA, talLV, talRA, talRV;
        Vector2 prevLeanVel;
        Quaternion rSway, rLegs, rSpine, rChest, rHead, rArmL, rArmR, rTalL, rTalR;
        Material[][] originalMats, flashMats;
        float flashLeft, flashCool, seed;
        public int Hits { get; private set; }

        bool ready;
        void Awake() => Init();
        public void Init()
        {
            if (ready) return; ready = true;
            health = GetComponent<Health>();
            if (!health) health = gameObject.AddComponent<Health>();
            health.Init(Team.Enemy, 100000);
            health.Damaged += OnDamaged;
            gameObject.layer = Ground.ActorLayer;
            Store(sway, ref rSway); Store(legs, ref rLegs); Store(spine, ref rSpine); Store(chest, ref rChest); Store(head, ref rHead);
            Store(armL, ref rArmL); Store(armR, ref rArmR); Store(talL, ref rTalL); Store(talR, ref rTalR);
            if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>();
            seed = Random.value * 10f;
        }

        static void Store(Transform t, ref Quaternion q) { if (t) q = t.localRotation; }

        void OnDamaged(int amount)
        {
            health.Heal(amount);
            Vector3 from = transform.position + transform.forward;
            var run = RunDirector.Instance;
            if (run && run.Player) from = run.Player.transform.position;
            Kick(from, amount);
        }

        // knock the dummy away from a world point (the attacker)
        public void Kick(Vector3 from, int amount = 1)
        {
            Hits++;
            Vector3 d = transform.position - from; d.y = 0;
            if (d.sqrMagnitude < 1e-4f) d = transform.forward;
            d.Normalize();
            float k = kick * Mathf.Lerp(1f, 1.6f, Mathf.Clamp01(amount / 5f));
            leanVel += new Vector2(d.x, d.z) * k * Mathf.Sqrt(stiffness);   // peak lean ~ k degrees before damping
            // arms flap up/down opposite each other, a little random so repeated hits differ
            float side = Vector3.Dot(d, transform.right);
            flapLVel += (armFlap * (0.8f + 0.4f * Random.value) * (1f + side)) * 9f;
            flapRVel += (armFlap * (0.8f + 0.4f * Random.value) * (1f - side)) * 9f;
            hitGlowLeft = 1f;
            if (flashCool <= 0f) { flashLeft = 0.05f; flashCool = 0.3f; ApplyFlash(true); }
        }

        void Update() => Step(Time.deltaTime);
        void LateUpdate() => Pose(Time.time);

        // one physics step; public so edit-mode captures can drive it
        public void Step(float rawDt)
        {
            float dt = Mathf.Min(rawDt, 1f / 30f);
            if (dt <= 0f) return;
            // body: damped spring back to upright
            Vector2 acc = -stiffness * lean - damping * leanVel;
            leanVel += acc * dt; lean += leanVel * dt;
            if (lean.magnitude > maxLean) { lean = lean.normalized * maxLean; leanVel *= 0.5f; }
            // head trails the body
            Vector2 hTarget = -leanVel * 0.04f * headLag;
            headVel += (120f * (hTarget - headOff) - 9f * headVel) * dt; headOff += headVel * dt;
            // arms: own springs
            flapLVel += (-160f * flapL - 7f * flapLVel) * dt; flapL += flapLVel * dt;
            flapRVel += (-160f * flapR - 7f * flapRVel) * dt; flapR += flapRVel * dt;
            // talismans: pendulums pushed by the body's acceleration
            Vector2 bodyAcc = (leanVel - prevLeanVel) / dt; prevLeanVel = leanVel;
            Pend(ref talLA, ref talLV, bodyAcc, dt); Pend(ref talRA, ref talRV, bodyAcc, dt);

            hitGlowLeft = Mathf.Max(0f, hitGlowLeft - dt * 1.8f);
            if (flashCool > 0f) flashCool -= dt;
            if (flashLeft > 0f) { flashLeft -= dt; if (flashLeft <= 0f) ApplyFlash(false); }
        }

        // slow uneven breathing glow (two sines + a flicker), brighter for a moment after each hit
        void Glow(float t)
        {
            float k = 0.78f + 0.14f * Mathf.Sin(t * pulseSpeed * 6.283f) + 0.08f * Mathf.Sin(t * 2.7f + 1.3f)
                      + 0.05f * (Mathf.PerlinNoise(t * 3f, seed) - 0.5f) + hitGlow * hitGlowLeft * hitGlowLeft;
            if (glowLight) glowLight.intensity = lightIntensity * k;
            if (renderers == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i]; if (!r) continue;
                r.GetPropertyBlock(mpb); mpb.SetColor(EmissionId, glowColor * k); r.SetPropertyBlock(mpb);
            }
        }

        void Pend(ref Vector2 a, ref Vector2 v, Vector2 drive, float dt)
        {
            v += (-45f * a - 2.2f * v - drive * 0.3f * talSwing) * dt;
            a += v * dt;
            a = Vector2.ClampMagnitude(a, 55f);
        }

        public void Pose(float time)
        {
            float t = time + seed;
            Glow(t);
            Vector2 wind = new Vector2(Mathf.Sin(t * 0.7f) + 0.4f * Mathf.Sin(t * 1.9f), 0.6f * Mathf.Sin(t * 0.5f + 1f)) * breeze;
            Vector2 l = lean + wind;
            float bodyShare = 1f;
            if (sway) { Tilt(sway, rSway, l * swayShare); bodyShare = 1f - swayShare; }
            Tilt(legs, rLegs, l * split.x * bodyShare);
            Tilt(spine, rSpine, l * split.y * bodyShare);
            Tilt(chest, rChest, l * split.z * bodyShare);
            Tilt(head, rHead, headOff + wind * 0.8f);
            Flap(armL, rArmL, flapL + Mathf.Sin(t * 1.3f) * breeze * 0.8f);
            Flap(armR, rArmR, -flapR - Mathf.Sin(t * 1.1f + 2f) * breeze * 0.8f);
            Tilt(talL, rTalL, talLA + wind * 2.5f);
            Tilt(talR, rTalR, talRA + wind * 2.5f);
        }

        // lean the bone's subtree toward world direction (x,z) by |v| degrees, on top of its bind pose
        static void Tilt(Transform b, Quaternion rest, Vector2 v)
        {
            if (!b) return;
            b.localRotation = rest;
            float ang = v.magnitude;
            if (ang < 1e-3f) return;
            Vector3 axis = Vector3.Cross(Vector3.up, new Vector3(v.x, 0, v.y) / ang);
            b.rotation = Quaternion.AngleAxis(ang, axis) * b.rotation;
        }

        // raise/lower a tied arm around the body's forward axis
        void Flap(Transform b, Quaternion rest, float deg)
        {
            if (!b) return;
            b.localRotation = rest;
            b.rotation = Quaternion.AngleAxis(deg, transform.forward) * b.rotation;
        }

        void ApplyFlash(bool on)
        {
            if (!hitFlash || renderers == null) return;
            if (originalMats == null)
            {
                originalMats = new Material[renderers.Length][]; flashMats = new Material[renderers.Length][];
                for (int i = 0; i < renderers.Length; i++)
                {
                    originalMats[i] = renderers[i].sharedMaterials;
                    flashMats[i] = new Material[originalMats[i].Length];
                    for (int k = 0; k < flashMats[i].Length; k++) flashMats[i][k] = hitFlash;
                }
            }
            for (int i = 0; i < renderers.Length; i++) if (renderers[i]) renderers[i].sharedMaterials = on ? flashMats[i] : originalMats[i];
        }

        void OnDisable() { if (flashLeft > 0f) { flashLeft = 0f; ApplyFlash(false); } }

    }
}
