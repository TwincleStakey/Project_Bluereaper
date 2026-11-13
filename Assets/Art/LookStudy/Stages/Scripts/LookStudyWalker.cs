// Stage test character: WASD or gamepad, follow camera, splashes and rings when wading through water.
using UnityEngine;
using UnityEngine.InputSystem;

public class LookStudyWalker : MonoBehaviour
{
    public float speed = 5.5f;
    public Vector3 cameraOffset = new Vector3(0, 19.5f, -13.5f);
    public Material splashMat, mistMat;
    [Tooltip("Authored splash effect; empty = Resources/WaterFX_Splash. Falls back to the old droplets when neither exists.")]
    public GameObject splashPrefab;
    WaterSplashPool splashPool;
    public System.Func<float, float, float> heightAt;
    public System.Func<Vector3> autoTarget;
    public enum Surface { Water, Dirt, Gravel, Grass, Stone }
    public System.Func<float, float, Surface> surfaceAt;
    public Vector2 trailCenter; public float trailSize = 84f;
    public Material dustMat, gritMat;
    ParticleSystem dust, grit, blades;
    bool leftFoot;
    Camera cam;
    ParticleSystem droplets, foam;
    readonly Vector4[] ripples = new Vector4[8];
    int nextRipple;
    float stepTimer;
    Vector3 lastPos;
    CharacterController cc;
    Transform follow;
    public Rect bounds;

    void Start()
    {
        // Inside the room-run game the game's own player moves and the camera belongs to it.
        // This walker then hides its body and only adds footprints, flattened grass and splashes under that player.
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (mb.GetType().Name == "PlayerController") { follow = mb.transform; break; }
        if (follow)
        {
            var body = transform.Find("Body"); if (body) body.gameObject.SetActive(false);
        }
        else
        {
            cc = gameObject.AddComponent<CharacterController>();
            cc.radius = 0.35f; cc.height = 1.6f; cc.center = new Vector3(0, 0.95f, 0); cc.stepOffset = 0.5f; cc.slopeLimit = 65f; cc.skinWidth = 0.04f;
        }
        cam = follow ? null : Camera.main;
        for (int i = 0; i < ripples.Length; i++) ripples[i] = Vector4.zero;
        droplets = LookStudyValley.Particle(transform, "Droplets", splashMat, 200, 0.35f, 0.7f, 0.05f, 0.14f, Vector3.zero, 0f, 0f, true);
        var m = droplets.main; m.gravityModifier = 1.6f; m.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.6f); m.prewarm = false; m.startColor = new Color(0.66f, 0.78f, 0.78f, 0.9f);
        var sh = droplets.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 34f; sh.radius = 0.16f; sh.rotation = new Vector3(-90, 0, 0);
        var v = droplets.velocityOverLifetime; v.enabled = false;
        droplets.Play();
        foam = LookStudyValley.Particle(transform, "Spray", mistMat, 60, 0.4f, 0.9f, 0.5f, 1.2f, Vector3.up * 0.5f, 0f, 0f, true);
        var fm = foam.main; fm.prewarm = false; fm.startColor = new Color(0.58f, 0.68f, 0.68f, 0.34f);
        foam.Play();
        if (!splashPrefab) splashPrefab = Resources.Load<GameObject>("WaterFX_Splash");
        if (splashPrefab) splashPool = WaterSplashPool.For(splashPrefab);
        lastPos = transform.position;
        LookStudyTrail.Ensure(trailCenter, trailSize).ownedByWalker = true;
        dust = LookStudyValley.Particle(transform, "Dust", dustMat ? dustMat : mistMat, 80, 0.6f, 1.4f, 0.35f, 0.9f, new Vector3(0, 0.35f, 0), 0.3f, 0f, true);
        var dm = dust.main; dm.prewarm = false;
        dust.Play();
        grit = LookStudyValley.Particle(transform, "Grit", gritMat ? gritMat : splashMat, 160, 0.3f, 0.6f, 0.03f, 0.07f, Vector3.zero, 0f, 0f, true);
        var gm = grit.main; gm.gravityModifier = 2.2f; gm.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.8f); gm.prewarm = false;
        var gsh = grit.shape; gsh.enabled = true; gsh.shapeType = ParticleSystemShapeType.Cone; gsh.angle = 38f; gsh.radius = 0.1f; gsh.rotation = new Vector3(-90, 0, 0);
        var gv = grit.velocityOverLifetime; gv.enabled = false;
        grit.Play();
        blades = LookStudyValley.Particle(transform, "Blades", gritMat ? gritMat : splashMat, 120, 0.5f, 1.0f, 0.05f, 0.11f, Vector3.zero, 0.6f, 0f, true);
        var bm = blades.main; bm.gravityModifier = 0.55f; bm.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.0f); bm.prewarm = false; bm.startColor = new Color(0.42f, 0.55f, 0.3f, 1);
        var bsh = blades.shape; bsh.enabled = true; bsh.shapeType = ParticleSystemShapeType.Cone; bsh.angle = 55f; bsh.radius = 0.25f; bsh.rotation = new Vector3(-90, 0, 0);
        var bv = blades.velocityOverLifetime; bv.enabled = false;
        var br = blades.GetComponent<ParticleSystemRenderer>(); br.renderMode = ParticleSystemRenderMode.Stretch; br.lengthScale = 2.6f; br.velocityScale = 0.05f;
        blades.Play();
    }

    void Update()
    {
        Vector2 input = Vector2.zero;
        var k = Keyboard.current;
        if (k != null)
        {
            if (k.wKey.isPressed) input.y += 1; if (k.sKey.isPressed) input.y -= 1;
            if (k.dKey.isPressed) input.x += 1; if (k.aKey.isPressed) input.x -= 1;
        }
        var pad = Gamepad.current;
        if (pad != null) input += pad.leftStick.ReadValue();
        var move = new Vector3(input.x, 0, input.y);
        if (autoTarget != null)
        {
            var to = autoTarget() - transform.position; to.y = 0;
            move = to.sqrMagnitude > 0.04f ? to.normalized : Vector3.zero;
        }
        if (move.sqrMagnitude > 1f) move.Normalize();

        Vector3 p; float h, depth;
        if (follow)
        {
            p = follow.position;
            h = heightAt != null ? heightAt(p.x, p.z) : p.y;
            depth = Mathf.Max(0f, LookStudyValley.Water - h);
            var d = p - transform.position; d.y = 0;
            move = d.sqrMagnitude > 1e-5f ? d.normalized : Vector3.zero;
            transform.position = p;
        }
        else
        {
            var start = transform.position;
            float h0 = heightAt != null ? heightAt(start.x, start.z) : 0f;
            float depth0 = Mathf.Max(0f, LookStudyValley.Water - h0);
            var delta = move * speed * Mathf.Lerp(1f, 0.7f, Mathf.Clamp01(depth0 * 4f)) * Time.deltaTime;
            if (cc) cc.Move(delta); else transform.position += delta;
            p = transform.position;
            if (bounds.width > 0f) { p.x = Mathf.Clamp(p.x, bounds.xMin, bounds.xMax); p.z = Mathf.Clamp(p.z, bounds.yMin, bounds.yMax); }
            h = heightAt != null ? heightAt(p.x, p.z) : 0f;
            depth = Mathf.Max(0f, LookStudyValley.Water - h);
            if (depth > 1.1f) { p = start; h = h0; depth = depth0; } // deep pools block the way
            p.y = h;
            transform.position = p;
        }
        if (move.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(move), Time.deltaTime * 12f);

        bool moving = (p - lastPos).sqrMagnitude > 1e-6f;
        lastPos = p;
        stepTimer -= Time.deltaTime;
        var trail = LookStudyTrail.Instance;
        if (trail) trail.SetPlayer(p, 0.95f, moving ? 1f : 0.85f);
        if (moving && stepTimer <= 0f)
        {
            stepTimer = 0.28f;
            var surf = depth > 0.03f ? Surface.Water : surfaceAt != null ? surfaceAt(p.x, p.z) : Surface.Dirt;
            leftFoot = !leftFoot;
            var side = transform.right * (leftFoot ? -0.13f : 0.13f);
            var foot = p + side + move.normalized * 0.12f;
            float yaw = transform.eulerAngles.y;
            if (surf == Surface.Water) Splash(new Vector3(p.x, LookStudyValley.Water + 0.03f, p.z), Mathf.Clamp01(depth * 2.5f + 0.4f));
            else Step(foot, yaw, surf);
        }
        Age();
        if (cam)
        {
            var target = transform.position + cameraOffset;
            cam.transform.position = Vector3.Lerp(cam.transform.position, target, 1f - Mathf.Exp(-Time.deltaTime * 6f));
            cam.transform.rotation = Quaternion.LookRotation(-cameraOffset);
        }
    }

    // Footprint plus debris that matches the ground.
    void Step(Vector3 at, float yaw, Surface s)
    {
        var trail = LookStudyTrail.Instance;
        float print = s == Surface.Dirt ? 1f : s == Surface.Gravel ? 0.65f : s == Surface.Grass ? 0.45f : 0.22f;
        if (trail) trail.Foot(at, yaw, print);
        var up = at + Vector3.up * 0.05f;
        dust.transform.position = up; grit.transform.position = up; blades.transform.position = up + Vector3.up * 0.15f;
        var dm = dust.main; var gm = grit.main;
        switch (s)
        {
            case Surface.Dirt:
                dm.startColor = new Color(0.46f, 0.38f, 0.3f, 0.42f); dust.Emit(3);
                gm.startColor = new Color(0.42f, 0.34f, 0.26f, 1); grit.Emit(9);
                break;
            case Surface.Gravel:
                dm.startColor = new Color(0.52f, 0.5f, 0.46f, 0.26f); dust.Emit(2);
                gm.startColor = new Color(0.6f, 0.58f, 0.54f, 1); grit.Emit(12);
                break;
            case Surface.Grass:
                blades.Emit(6); dm.startColor = new Color(0.38f, 0.4f, 0.32f, 0.16f); dust.Emit(1);
                break;
            default:
                dm.startColor = new Color(0.4f, 0.38f, 0.37f, 0.3f); dust.Emit(2);
                break;
        }
    }

    public void Splash(Vector3 at, float strength)
    {
        if (splashPool) { splashPool.Play(at, strength, Random.Range(0f, 360f)); return; }
        droplets.transform.position = at; foam.transform.position = at;
        droplets.Emit(Mathf.RoundToInt(10 + 14 * strength));
        foam.Emit(Mathf.RoundToInt(2 + 3 * strength));
        ripples[nextRipple] = new Vector4(at.x, at.z, 0f, strength);
        nextRipple = (nextRipple + 1) % ripples.Length;
    }

    void Age()
    {
        for (int i = 0; i < ripples.Length; i++)
        {
            if (ripples[i].w <= 0f) continue;
            ripples[i].z += Time.deltaTime;
            if (ripples[i].z > 1.6f) ripples[i].w = 0f;
        }
        Shader.SetGlobalVectorArray("_LSRipples", ripples);
    }
}
