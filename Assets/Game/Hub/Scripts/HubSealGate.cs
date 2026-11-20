// 거점 sealed portal gate (11-20, Kido: "맨 왼쪽문은 우선 닫혀있는걸로 ... 사슬 끊어지면서 문 열리고 포탈열리면서 들어가게").
// Starts sealed with ropes, paper shide and talismans. Open(): talismans burn to ash, the ropes snap and burn, then a heavy
// shake and the doors grind open slowly, gathering speed a little; then the portal grows open and its light comes up. Walking into the portal trigger jumps to targetRoom.
// Open condition for now: stand within interactRadius and press the interact key (HubInput, F). Other systems can call Open() instead.
// Dev: touching ArtSource/Hub/open.trigger (editor only) opens it too, for captures.
using System.Collections;
using RoomRun;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hub
{
    public sealed class HubSealGate : MonoBehaviour
    {
        public Transform doorL, doorR;           // pivots on the hinge edges, closed at local yaw 0
        public float doorSwing = 105f;           // degrees, sign chosen by the builder so the leaves swing away from the court
        public Rigidbody[] chains;
        public GameObject[] burnAway;            // 11-20: shimenawa ropes + paper strips that burn off when the seal breaks
        public ParticleSystem[] talismans;       // the paper strips (static particles) - cleared on open
        public ParticleSystem[] burst;           // burning paper bits + sparks
        public ParticleSystem fireFx;            // flame flipbook, world space, emitted where things burn
        public ParticleSystem ashFx;             // dark flakes falling from what burnt
        public float talismanBurn = 1.4f, ropeBurn = 1.3f, doorOpen = 3.6f;
        public Transform portal;                 // swirl card, scaled up from 0
        public Renderer portalRenderer;
        public Light portalLight;
        public float portalLightMax = 3.2f;
        public Renderer daisGlow;                // optional, dais emission comes up with the portal
        public HubPortalTrigger enter;
        public string targetRoom = "grass";
        public float interactRadius = 3.2f;
        public bool startOpen;

        public bool Opened { get; private set; }
        Vector3 portalScale = Vector3.one;
        static readonly int OpenId = Shader.PropertyToID("_Open");
        MaterialPropertyBlock mpb;
        Vector3[] baseSize;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (portal) portalScale = portal.localScale;
            SetPortal(0f);
            if (enter) { enter.gate = this; enter.gameObject.SetActive(false); }
            if (startOpen) Open();
        }

        void Update()
        {
            if (Opened)
            {
                // distance check as well as the trigger (the player moves by CharacterController)
                var r = RunDirector.Instance; var pl = r ? r.Player : null;
                if (pl && enter && enter.gameObject.activeSelf)
                {
                    var dp = pl.transform.position - enter.transform.position; dp.y = 0;
                    if (dp.magnitude < 1.1f && !r.Transitioning && !string.IsNullOrEmpty(targetRoom)) r.JumpTo(targetRoom);
                }
                return;
            }
#if UNITY_EDITOR
            if (Time.frameCount % 15 == 0)
            {
                var trig = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource", "Hub", "open.trigger");
                if (System.IO.File.Exists(trig)) { Open(); return; }
            }
#endif
            var run = RunDirector.Instance;
            var player = run ? run.Player : null;
            if (!player) return;
            var d = player.transform.position - transform.position; d.y = 0;
            if (d.magnitude > interactRadius) return;
            if (!HubDialogue.IsOpen && HubInput.InteractPressed()) Open();   // 11-20 Kido: the interact key is F (HubInput)
        }

        public void Open()
        {
            if (Opened) return;
            Opened = true;
            StartCoroutine(OpenRoutine());
        }

        // 11-20 v2 (Kido: talismans burn to ash first -> the ropes snap and burn to ash -> the doors open, not at one speed but
        // heavy: a big shake, then slow, gathering speed only a little - make it grand)
        IEnumerator OpenRoutine()
        {
            var rig = RunDirector.Instance ? RunDirector.Instance.Rig : null;
            // ---- 1. talismans (and the paper shide) catch fire one after another, char, fall as ash
            var paper = new System.Collections.Generic.List<(Vector3 p, float delay)>();
            if (talismans != null)
                foreach (var ts in talismans)
                {
                    if (!ts) continue;
                    var parts = new ParticleSystem.Particle[ts.particleCount]; int n = ts.GetParticles(parts);
                    for (int i = 0; i < n; i++) paper.Add((ts.transform.TransformPoint(parts[i].position), Random.Range(0f, talismanBurn * 0.55f)));
                }
            var shide = new System.Collections.Generic.List<GameObject>(); var ropes = new System.Collections.Generic.List<GameObject>();
            if (burnAway != null) foreach (var b in burnAway) if (b) (b.name.StartsWith("Shide") ? shide : ropes).Add(b);
            foreach (var sd in shide) paper.Add((sd.GetComponent<Renderer>() ? sd.GetComponent<Renderer>().bounds.center : sd.transform.position, Random.Range(0f, talismanBurn * 0.55f)));
            var shideBase = shide.ConvertAll(x => x.transform.localScale);
            if (burst != null) foreach (var b in burst) if (b) b.Play(true);
            for (float t = 0; t < talismanBurn; t += Time.deltaTime)
            {
                // talisman particles: glow orange -> char black -> gone, each on its own delay
                if (talismans != null)
                    foreach (var ts in talismans)
                    {
                        if (!ts) continue;
                        var parts = new ParticleSystem.Particle[ts.particleCount]; int n = ts.GetParticles(parts);
                        for (int i = 0; i < n; i++)
                        {
                            float d = (i * 0.37f % 1f) * talismanBurn * 0.55f, k = Mathf.Clamp01((t - d) / (talismanBurn * 0.45f));
                            var c = Color.Lerp(Color.white, new Color(1f, 0.55f, 0.2f), Mathf.Clamp01(k * 3f));
                            c = Color.Lerp(c, new Color(0.08f, 0.06f, 0.05f), Mathf.Clamp01(k * 2f - 0.6f));
                            c.a = 1f - Mathf.Clamp01(k * 1.6f - 0.6f);
                            parts[i].startColor = c;
                            if (t == 0f || baseSize == null || baseSize.Length != n) { baseSize = new Vector3[n]; for (int q = 0; q < n; q++) baseSize[q] = parts[q].startSize3D; }
                            parts[i].startSize3D = new Vector3(baseSize[i].x * c.a, baseSize[i].y * (1f - 0.35f * Mathf.Clamp01(k * 2f - 0.5f)) * c.a, 1f);   // curls up and is gone
                        }
                        ts.SetParticles(parts, n);
                    }
                for (int i = 0; i < shide.Count; i++)
                {
                    float k = Mathf.Clamp01((t - paper[paper.Count - shide.Count + i].delay) / (talismanBurn * 0.45f));
                    shide[i].transform.localScale = Vector3.Scale(shideBase[i], new Vector3(1f, 1f - 0.9f * Mathf.Clamp01(k * 1.5f - 0.3f), 1f));
                    if (k >= 1f && shide[i].activeSelf) shide[i].SetActive(false);
                }
                foreach (var (pp, delay) in paper)
                {
                    float k = (t - delay) / (talismanBurn * 0.45f);
                    if (k > 0f && k < 0.8f) { Emit(fireFx, pp + Random.insideUnitSphere * 0.15f, 1, 0.35f); }
                    if (k > 0.4f && k < 1.1f && Random.value < 0.5f) Emit(ashFx, pp + Random.insideUnitSphere * 0.2f, 1, 0.08f);
                }
                yield return null;
            }
            if (talismans != null) foreach (var ts in talismans) if (ts) ts.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var sd in shide) sd.SetActive(false);
            yield return new WaitForSeconds(0.25f);
            // ---- 2. the ropes: a snap at the middle (jolt), both halves sag and burn along their length, char and drop as ash
            if (rig) rig.Shake(0.35f);
            var ropeBase = ropes.ConvertAll(x => (pos: x.transform.position, rot: x.transform.rotation, scl: x.transform.localScale));
            var mpbR = new MaterialPropertyBlock();
            for (float t = 0; t < ropeBurn; t += Time.deltaTime)
            {
                float k = t / ropeBurn;
                for (int i = 0; i < ropes.Count; i++)
                {
                    var r = ropes[i]; if (!r) continue;
                    var rr = r.GetComponentInChildren<Renderer>();
                    // sag + drop: slack first, then falling, shrinking as it burns
                    float drop = 0.25f * Mathf.Clamp01(k * 3f) + 1.6f * Mathf.Pow(Mathf.Clamp01(k * 1.4f - 0.3f), 2f);
                    r.transform.position = ropeBase[i].pos + Vector3.down * drop;
                    r.transform.localScale = Vector3.Scale(ropeBase[i].scl, new Vector3(1f, 1f + 0.8f * Mathf.Clamp01(k * 3f), 1f - 0.7f * Mathf.Clamp01(k * 1.5f - 0.4f)));
                    if (rr)
                    {
                        rr.GetPropertyBlock(mpbR);
                        mpbR.SetColor("_BaseColor", Color.Lerp(Color.white, new Color(0.06f, 0.05f, 0.04f), Mathf.Clamp01(k * 1.6f)));
                        mpbR.SetColor("_EmissionColor", new Color(1f, 0.35f, 0.1f) * 1.5f * Mathf.Sin(Mathf.Clamp01(k * 1.3f) * Mathf.PI));
                        rr.SetPropertyBlock(mpbR);
                        var b = rr.bounds;
                        for (int f = 0; f < 3; f++)
                        {
                            var p = new Vector3(Random.Range(b.min.x, b.max.x), Random.Range(b.min.y, b.max.y), b.center.z);
                            if (k < 0.85f) Emit(fireFx, p, 1, 0.45f);
                            if (k > 0.3f && Random.value < 0.6f) Emit(ashFx, p, 1, 0.1f);
                        }
                    }
                }
                yield return null;
            }
            foreach (var r in ropes) if (r) r.SetActive(false);
            if (chains != null) foreach (var c in chains) if (c) Destroy(c.gameObject);
            yield return new WaitForSeconds(0.45f);
            // ---- 3. the doors: a heavy jolt and rumble, then a slow swing that gathers speed only a little
            if (rig) rig.Shake(1.1f);
            yield return new WaitForSeconds(0.35f);
            for (float t = 0; t < doorOpen; t += Time.deltaTime)
            {
                float k = t / doorOpen;
                // slow start, gently accelerating, soft settle at the end (no snap)
                float e = Mathf.Pow(k, 1.7f); e = e * (1f - 0.25f * Mathf.Pow(k, 6f)) + 0.25f * Mathf.Pow(k, 6f) * Mathf.SmoothStep(0f, 1f, k);
                SetDoors(e * doorSwing);
                if (rig && Time.frameCount % 9 == 0) rig.Shake(0.28f * (1f - k) + 0.08f);   // rumble while the leaves grind open
                if (k > 0.45f) SetPortal(Mathf.SmoothStep(0f, 0.4f, (k - 0.45f) / 0.55f));
                yield return null;
            }
            SetDoors(doorSwing);
            if (rig) rig.Shake(0.5f);
            // ---- 4. portal grows open
            for (float t = 0; t < 1.1f; t += Time.deltaTime)
            {
                float k = t / 1.1f;
                float back = 1f + 2.2f * Mathf.Pow(k - 1f, 3f) + 1.2f * Mathf.Pow(k - 1f, 2f);   // ease-out-back
                SetPortal(Mathf.Lerp(0.4f, 1f, back));
                yield return null;
            }
            SetPortal(1f);
            if (enter) enter.gameObject.SetActive(true);
        }

        static void Emit(ParticleSystem ps, Vector3 worldPos, int count, float size)
        {
            if (!ps) return;
            var ep = new ParticleSystem.EmitParams { position = worldPos, applyShapeToPosition = false, startSize = size * Random.Range(0.8f, 1.25f) };
            ps.Emit(ep, count);
        }

        void SetDoors(float a)
        {
            if (doorL) doorL.localRotation = Quaternion.Euler(0f, -a, 0f);
            if (doorR) doorR.localRotation = Quaternion.Euler(0f, a, 0f);
        }

        void SetPortal(float k)
        {
            if (portal) { portal.localScale = portalScale * Mathf.Max(0.001f, Mathf.Clamp(k, 0f, 1.2f)); portal.gameObject.SetActive(k > 0.001f); }
            if (portalRenderer) { portalRenderer.GetPropertyBlock(mpb); mpb.SetFloat(OpenId, Mathf.Clamp01(k)); portalRenderer.SetPropertyBlock(mpb); }
            if (portalLight) { portalLight.intensity = portalLightMax * Mathf.Clamp01(k); portalLight.enabled = k > 0.01f; }
            if (daisGlow)
            {
                daisGlow.GetPropertyBlock(mpb);
                mpb.SetColor("_EmissionColor", new Color(0.1f, 0.75f, 0.85f) * 1.4f * Mathf.Clamp01(k));
                daisGlow.SetPropertyBlock(mpb);
            }
        }

        internal void Enter(Collider other)
        {
            if (!Opened || other.GetComponentInParent<PlayerController>() == null) return;
            var run = RunDirector.Instance;
            if (run && !run.Transitioning && !string.IsNullOrEmpty(targetRoom)) run.JumpTo(targetRoom);
        }
    }
}
