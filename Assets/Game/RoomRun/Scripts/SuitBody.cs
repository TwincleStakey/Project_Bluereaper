// 11-19 suit body test (Kido ref: modern 저승사자 in a black suit + ankle-length coat). The Mixamo "Joe" suit model is put
// next to the player's hero model and copies its whole humanoid pose every frame (HumanPoseHandler), before the whip's
// own bone edits (WhipCombat runs at 260 and is pointed at Joe's Animator), so the whip arm, body follow and the coat all
// act on Joe. The hero's renderers are hidden. Toggle RR.Suit (dev panel 플레이어 > 정장 몸) or shots token suit=1/0
// (before whip=1). Editor-only asset load: it is a look test, not shipped.
using UnityEngine;

namespace RoomRun
{
    [DefaultExecutionOrder(255)]   // after SplitBody/LSPoseFix/ArmStretch/eye flames, before WhipCombat (260)
    public sealed class SuitBody : MonoBehaviour
    {
        public const string Key = "RR.Suit";
        public static bool? Force;
        public static bool On => Force ?? PlayerPrefs.GetInt(Key, 1) != 0;   // 11-20 Kido: the suit + long coat is the default character (RR.Suit / RR.Coat = 0 turns it off)
        const string FbxPath = "Assets/Game/RoomRun/ThirdParty/MixamoJoe/Joe.fbx";

        Animator src, dst;
        HumanPoseHandler hSrc, hDst;
        HumanPose pose;
        readonly System.Collections.Generic.List<Renderer> hidden = new System.Collections.Generic.List<Renderer>();
        public Animator Body => dst;

        /// Adds the suit body to the hero and returns its Animator (or the hero's own when off / missing).
        public static Animator Attach(Animator hero)
        {
            if (!On || !hero) return hero;
            var s = hero.GetComponent<SuitBody>();
            if (!s) { s = hero.gameObject.AddComponent<SuitBody>(); if (!s.Build(hero)) { Destroy(s); return hero; } }
            return s.dst ? s.dst : hero;
        }

        bool Build(Animator hero)
        {
            GameObject prefab = null;
#if UNITY_EDITOR
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
#endif
            if (!prefab) { Debug.LogWarning("[RoomRun] suit body: Joe.fbx not found"); return false; }
            src = hero;
            var go = Instantiate(prefab, hero.transform.parent);
            go.name = "SuitBody_Joe";
            go.transform.localPosition = hero.transform.localPosition;
            go.transform.localRotation = hero.transform.localRotation;
            dst = go.GetComponent<Animator>();
            if (!dst || !dst.avatar || !dst.avatar.isHuman || !src.avatar || !src.avatar.isHuman) { Destroy(go); return false; }
            dst.runtimeAnimatorController = null;
            dst.applyRootMotion = false;
            dst.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // same height as the hero: scale by the hips height
            float hs = src.GetBoneTransform(HumanBodyBones.Hips).position.y - hero.transform.position.y;
            float hd = dst.GetBoneTransform(HumanBodyBones.Hips).position.y - go.transform.position.y;
            float k = hd > 0.01f ? hs / hd : 1f;
            // 11-20 Kido "3개 다 통일해, 몸 사이즈랑 바디 프로포션": the hips height above was read from whatever pose the hero
            // had when the weapon first attached the body (a crouch or a lunge gave a smaller Joe), so each weapon could get its
            // own size. Use the avatars' T-pose hips heights instead (Animator.humanScale): the same Joe size for every weapon.
            if (src.humanScale > 0.01f && dst.humanScale > 0.01f)
            {
                float kPose = k;
                k = src.humanScale / dst.humanScale;   // Joe sits beside the hero (same parent, scale = hero scale * k)
                Debug.Log($"[SuitBody] size from T-pose hips: k {k:0.000} (the attach-moment pose would have given {kPose:0.000})");
            }
            go.transform.localScale = hero.transform.localScale * Mathf.Clamp(k * SizeMul, 0.4f, 2.5f);
            // 11-20 Kido "전에 거보다 왜 이리 커졌어": matching the HIPS of the chibi hero (big head, short legs) made the adult Joe
            // far taller than the old character. Match the whole height instead: rest-pose mesh bounds (head top to sole) of the
            // hero's renderers vs Joe's, times HeightMul. Same rule for every weapon (it only looks at the meshes, not the pose).
            if (MatchHeight)
            {
                // v3 (Kido 15:16 "왜 이렇게 거인이야"): hero = rest mesh bounds of its skinned meshes (2.883 m, about the hub
                // scarecrow's height, i.e. right); Joe = his HeadTop_End bone above his feet (fresh bind pose, after the scale
                // above). The vertex bakes gave 0.018 m / 455 m for the hero (its skinning scale) and are not used.
                float hH = MeshBoundsHeight(hero.gameObject), jH = 0f;
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name.Contains("HeadTop")) { jH = t.position.y - go.transform.position.y; break; }
                Debug.Log($"[SuitBody] height probe: hero mesh bounds {hH:0.000} m, Joe head top {jH:0.000} m (baked hero {RestHeight(hero.gameObject):0.000}, bounds {BoundsHeight(hero.gameObject):0.000})");
                if (hH > 0.05f && jH > 0.05f && hH / jH > 0.4f && hH / jH < 2.5f)   // sanity: a bad measure must never blow Joe up
                {
                    go.transform.localScale *= hH / jH * HeightMul;
                    Debug.Log($"[SuitBody] whole-height match: hero {hH:0.000} m, Joe {jH:0.000} m -> x{hH / jH * HeightMul:0.000}");
                }
                else Debug.LogWarning($"[SuitBody] whole-height match skipped: hero {hH:0.000} m, Joe {jH:0.000} m (ratio out of 0.4..2.5)");
            }
            hSrc = new HumanPoseHandler(src.avatar, src.transform);
            hDst = new HumanPoseHandler(dst.avatar, dst.transform);
            // the blade lives under the hero's right hand (the whip cuts its mesh into the plates): move it to Joe's hand
            var hHand = src.GetBoneTransform(HumanBodyBones.RightHand); var jHand = dst.GetBoneTransform(HumanBodyBones.RightHand);
            Transform sword = null;
            if (hHand) foreach (var tr in hHand.GetComponentsInChildren<Transform>(true)) if (tr.name == "Greatsword") { sword = tr; break; }
            // v49 (Kido 20:53 "칼부터 손에 잘 맞게 들어, 이상해"): copying the LOCAL grip from the hero's hand bone was wrong,
            // the two rigs' hand bones have different axes. The move now happens on the first frame both bodies share the
            // pose: the sword keeps its WORLD rotation and its offset from the palm, then is parented to Joe's hand.
            pendingSword = sword; heroHand = hHand; joeHand = jHand;
            foreach (var r in hero.GetComponentsInChildren<Renderer>(true)) if (r.enabled && !(KeepSword && sword && r.transform.IsChildOf(sword))) { r.enabled = false; hidden.Add(r); }   // KeepSword: the greatsword keeps its blade visible (the whip hides it for its plates)
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
#if UNITY_EDITOR
            if (!darkTex) darkTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/RoomRun/ThirdParty/MixamoJoe/Textures/Ch33_1001_Diffuse_darkface.png");   // 11-20 Kido "얼굴은 검고 손은 안 검지 않았어?": face-only dark (hands keep skin)
#endif
            if (Bulk) BulkUp();
            if (!heroTemplate)
                foreach (var r in hidden)
                    foreach (var m in r ? r.sharedMaterials : new Material[0])
                        if (m && m.shader && m.shader.name == "GSHero/Readable" && (!m.HasProperty("_Metal") || m.GetFloat("_Metal") < 0.5f) && (!m.HasProperty("_Steel") || m.GetFloat("_Steel") < 0.5f)) { heroTemplate = m; break; }
            Debug.Log($"[SuitBody] hero look template: {(heroTemplate ? heroTemplate.name : "none (hero has no GSHero/Readable body material)")}");
            Matte(go);
            if (Plume) HeadPlume.Attach(dst);
            NeckTie.Attach(dst);   // 11-20 proposal: plain white shirt + one cloth necktie (NeckTie.On, token tie=1)
            return true;
        }
        public static bool KeepSword;   // 11-20 greatsword thread: set by GreatswordCombat before Attach so the sword is not hidden with the hero
        public static float SizeMul = 1.15f;
        public static bool MatchHeight = true;    // 11-20: Joe's whole height = the old hero's whole height (SizeMul/hips no longer decide it)
        public static float HeightMul = 0.85f;    // 11-20 Kido "왜 아직도 이렇게 커": the hero's 2.88 m includes the 삿갓; 0.85 until Kido gives a number
        // rest-pose height of everything skinned under root: each SkinnedMeshRenderer's mesh bounds (bind space) in world
        static float MeshBoundsHeight(GameObject root)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!r.sharedMesh || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var b = r.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float y = r.transform.TransformPoint(c).y;
                    lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                }
            }
            return hi > lo ? hi - lo : 0f;
        }
        static float BoundsHeight(GameObject root)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!r.sharedMesh || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var b = r.bounds; lo = Mathf.Min(lo, b.min.y); hi = Mathf.Max(hi, b.max.y);
            }
            return hi > lo ? hi - lo : 0f;
        }
        static float RestHeight(GameObject root)
        {
            // 11-20 v2: real vertices (BakeMesh = the skinned shape as it stands now), not the mesh bounds box: the hero's
            // bounds said 2.88 m and the match changed nothing. The hero stands in its idle; Joe is fresh in its bind T pose.
            float lo = float.MaxValue, hi = float.MinValue;
            var tmp = new Mesh();
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!r.sharedMesh || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                r.BakeMesh(tmp, true);   // scaled bake (v2a: the unscaled bake + TransformPoint gave 455 m / 5.7 m): rotate + offset only
                var vs = tmp.vertices; var rp = r.transform.position; var rq = r.transform.rotation;
                for (int i = 0; i < vs.Length; i += 3) { float y = (rp + rq * vs[i]).y; if (y < lo) lo = y; if (y > hi) hi = y; }
            }
            Destroy(tmp);
            return hi > lo ? hi - lo : 0f;
        }   // the hero is a chibi with a short torso; a little taller reads as an adult

        // v42 (Kido 16:13 "무사처럼 크고 강하게 상체 벌크업"): bigger chest and shoulders by scaling the spine chain up and giving
        // the head back its size (a smaller head on a big chest reads heroic); arms come out broader and a little heavier.
        public static bool Bulk = true, Plume = true;
        public static float ChestMul = 1.16f, SpineMul = 1.06f, UpperChestMul = 1.05f, ArmMul = 0.94f, HeadKeep = 0.92f;
        void BulkUp()
        {
            void S(HumanBodyBones b, float m) { var t = dst.GetBoneTransform(b); if (t) t.localScale *= m; }
            float spineChain = SpineMul * ChestMul * (dst.GetBoneTransform(HumanBodyBones.UpperChest) ? UpperChestMul : 1f);
            S(HumanBodyBones.Spine, SpineMul);
            S(HumanBodyBones.Chest, ChestMul);
            S(HumanBodyBones.UpperChest, UpperChestMul);
            S(HumanBodyBones.Neck, HeadKeep / spineChain);          // head a bit smaller than before relative to the body
            S(HumanBodyBones.LeftUpperArm, ArmMul); S(HumanBodyBones.RightUpperArm, ArmMul);   // arms thick, not too long
        }

        // v42 (Kido: "너무 반짝거리는 것 좀 줄이고"): matte cloth and skin: low smoothness, no specular highlights or reflections
        public static float Smooth = 0.12f;
        static void Matte(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.materials;   // instances
                foreach (var m in ms)
                {
                    if (!m) continue;
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", Smooth);
                    if (m.HasProperty("_MetallicGlossMap")) m.SetTexture("_MetallicGlossMap", null);
                    if (m.HasProperty("_SpecGlossMap")) m.SetTexture("_SpecGlossMap", null);
                    if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                    m.DisableKeyword("_METALLICSPECGLOSSMAP"); m.DisableKeyword("_SPECGLOSSMAP");
                    if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);
                    m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
                    m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                }
                // v43 (Kido 17:35 "왜 머리가 저래"): the pale face/hands read as a white blob from above; the ref 저승사자 has a
                // black face. Skin is painted near-black in Ch33_1001_Diffuse_dark (made offline from the original by a skin
                // colour mask), hair black, tie red.
                foreach (var m in ms)
                {
                    if (!m) continue;
                    if (DarkBody && darkTex && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") && m.GetTexture("_BaseMap").name == "Ch33_1001_Diffuse") m.SetTexture("_BaseMap", darkTex);
                    if (r.name.Contains("Hair") || r.name.Contains("Eyelash")) m.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.025f));
                    if (r.name.Contains("Tie")) { m.SetColor("_BaseColor", new Color(0.75f, 0.03f, 0.03f)); }
                }
                r.materials = ms;
                if (HeroLook) ApplyHeroLook(r, heroTemplate);
                ms = r.materials;
                foreach (var m in ms) if (m) Debug.Log($"[SuitBody] {r.name}: {m.name} base={(m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") ? m.GetTexture("_BaseMap").name : "-")} col={(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "-")} dark={(darkTex ? darkTex.name : "null")}");
            }
        }
        public static bool DarkBody = true;
        // 11-20 Kido "옷이 게임하고 안 어울려, 매터리얼이 안 들어간 느낌 ... 세계관하고 어울려 보이게": the matte URP/Lit near-black suit
        // read flat. The old player's shader (GSHero/Readable: wrapped moon light, lift on the darkest albedo, sky fill, warm
        // rim, top key) is what every hero shot of this game was tuned with; Joe's suit, hair, tie and the long coat now use it,
        // copied from the hero's own body material at attach (the 저승사자 colours stay: dark suit, black hair, red tie).
        public static bool HeroLook = true;
        static Material heroTemplate;
        public static void ApplyHeroLook(Renderer r, Material template)
        {
            if (!r || !template) return;
            var ms = r.sharedMaterials; bool changed = false;
            for (int i = 0; i < ms.Length; i++)
            {
                var m = ms[i];
                if (!m || m.shader == template.shader) continue;
                if (m.shader && (m.shader.name.Contains("Particles") || m.shader.name.Contains("Unlit"))) continue;   // glows / feathers keep their own look
                var n = new Material(template) { name = (m.name.Replace(" (Instance)", "")) + "_HeroLook" };
                Texture tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                n.SetTexture("_BaseMap", tex ? tex : Texture2D.whiteTexture);
                var col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                n.SetColor("_BaseColor", col);

                if (n.HasProperty("_HatAmount")) n.SetFloat("_HatAmount", 0f);    // Joe has no vertex colours (white = all hat)
                if (n.HasProperty("_EmissionColor")) n.SetColor("_EmissionColor", Color.black);
                n.SetTexture("_EmissionMap", null); n.DisableKeyword("_EMISSION");
                if (m.HasProperty("_Cull") && n.HasProperty("_Cull")) n.SetFloat("_Cull", m.GetFloat("_Cull"));   // the coat is two-sided
                if (n.HasProperty("_RimStrength")) n.SetFloat("_RimStrength", 0f);   // Kido "하얀 외곽 라인 전부 없애": no rim edge on the clothes
                if (n.HasProperty("_Metal")) n.SetFloat("_Metal", 0f);
                if (n.HasProperty("_Steel")) n.SetFloat("_Steel", 0f);
                if (n.HasProperty("_EdgeGlint")) n.SetFloat("_EdgeGlint", 0f);
                ms[i] = n; changed = true;
            }
            if (changed) r.sharedMaterials = ms;
        }
        // 11-20 Kido "플레이어 맞으면 왜 칼이 깜빡거려": the hurt blink (PlayerController.SetVisible) toggles the hero's renderers,
        // which still include the greatsword now sitting in Joe's hand, while Joe himself never blinked: only the blade did.
        // Joe (body, coat, hat) now blinks with it.
        PlayerController owner; readonly System.Collections.Generic.List<Renderer> blinked = new System.Collections.Generic.List<Renderer>();
        void Blink()
        {
            if (!owner) owner = src ? src.GetComponentInParent<PlayerController>() : null;
            bool off = owner && owner.BlinkOff;
            if (off && blinked.Count == 0)
            {
                foreach (var r in dst.GetComponentsInChildren<Renderer>()) if (r.enabled && !hidden.Contains(r)) { r.enabled = false; blinked.Add(r); }
            }
            else if (!off && blinked.Count > 0)
            {
                foreach (var r in blinked) if (r) r.enabled = true;
                blinked.Clear();
            }
        }
        // re-run for renderers added after the build (the long coat, the hat)
        float lookCheck; int lookCount = -1;
        void RefreshHeroLook()
        {
            if (!HeroLook || !heroTemplate || !dst || (lookCheck -= Time.deltaTime) > 0f) return;
            lookCheck = 0.5f;
            var rs = dst.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == lookCount) return;
            lookCount = rs.Length;
            // 11-20 Kido "캐릭터 매터리얼 텍스처 좀 맞춰": every part (suit, shirt, coat, hat) on the one hero shader, own colour/texture kept;
            // glow/feather materials (Particles/Unlit) are skipped inside ApplyHeroLook
            foreach (var r in rs) if (!(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer)) ApplyHeroLook(r, heroTemplate);
        }
        Transform pendingSword, heroHand, joeHand;
        public static bool HangDown = false, WhipMode;   // v56: off; SplitBody re-seats the sword every frame, the whip uses WhipCombat.GripDir instead
        public static bool FlipGrip = false;   // v53 (Kido 11-20: blade must hang DOWN from the hand like image 2): the flip made it point up; the wind-up issue was the driver, fixed there
        static Texture2D darkTex;

        void LateUpdate()
        {
            if (!dst || hSrc == null) return;
            RefreshHeroLook();
            Blink();
            if (NeckTie.On) NeckTie.Attach(dst);   // capture tokens are read after the body is built: attach late (no-op once attached)
            dst.transform.localPosition = src.transform.localPosition;
            dst.transform.localRotation = src.transform.localRotation;
            hSrc.GetHumanPose(ref pose);
            hDst.SetHumanPose(ref pose);
            if (pendingSword && heroHand && joeHand)
            {
                // palm centres (hand -> middle finger base), so the hilt sits in Joe's fist, not at his wrist
                Vector3 Palm(Animator an, Transform h) { var m = an.GetBoneTransform(HumanBodyBones.RightMiddleProximal); return m ? Vector3.Lerp(h.position, m.position, 0.6f) : h.position; }
                var hp = Palm(src, heroHand); var jp = Palm(dst, joeHand);
                var off = pendingSword.position - hp;
                pendingSword.SetParent(joeHand, true);
                pendingSword.position = jp + off;
                // v52 (Kido 11-20 "누가 칼을 저렇게 쥐고 있어"): the blade came out of the thumb side pointing forward/up in the
                // wind-up; turned 180 deg round the finger line so it lies back over the shoulder when the hand is raised
                // v53 (Kido 11-20 image 2: the blade should hang DOWN from the hand at rest): the copied grip had it lying
                // forward; tip it down (a little forward) about the palm, whip mode only (the greatsword thread seats its own)
                if (HangDown && (WhipMode || !KeepSword))   // WhipMode: the greatsword component also runs (and sets KeepSword) while the whip is the weapon
                {
                    var mf = pendingSword.GetComponentInChildren<MeshFilter>();
                    if (mf && mf.sharedMesh)
                    {
                        var b = mf.sharedMesh.bounds.size;
                        Vector3 la = b.x >= b.y && b.x >= b.z ? Vector3.right : b.y >= b.z ? Vector3.up : Vector3.forward;
                        var tipW = mf.transform.TransformDirection(la);
                        // which end is the tip: the mesh centre lies toward the blade
                        if (Vector3.Dot(mf.transform.TransformPoint(mf.sharedMesh.bounds.center) - jp, tipW) < 0f) tipW = -tipW;
                        // in the HAND's own frame (pose independent; the first frame can be the spawn pose): between the thumb
                        // line (blade out of the top of the fist) and the finger line, so it hangs down-forward with the arm
                        // relaxed and lies back over the shoulder when the hand is raised
                        var mid = dst.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                        var th = dst.GetBoneTransform(HumanBodyBones.RightThumbProximal);
                        var fingers = mid ? (mid.position - joeHand.position).normalized : -joeHand.up;
                        var thumb = th ? (th.position - joeHand.position) : joeHand.forward;
                        thumb = Vector3.ProjectOnPlane(thumb, fingers).normalized;
                        var want = (fingers * 0.85f - thumb * 0.55f).normalized;   // v54 (Kido 11-20 red line): the opposite side, down and BACK from the hand at rest
                        var axis = Vector3.Cross(tipW, want);
                        if (axis.sqrMagnitude > 1e-6f) pendingSword.RotateAround(jp, axis.normalized, Vector3.Angle(tipW, want));
                    }
                }
                if (FlipGrip)
                {
                    var mid = dst.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                    var fingerAxis = mid ? (mid.position - joeHand.position).normalized : joeHand.right;
                    pendingSword.RotateAround(jp, fingerAxis, 180f);
                }
                Debug.Log($"[SuitBody] sword moved to Joe's hand (world rot kept, palm offset {off.magnitude:0.000} m)");
                pendingSword = null;
            }
            // keep the hidden hero hidden (other scripts may re-enable renderers)
            foreach (var r in hidden) if (r && r.enabled) r.enabled = false;
        }

        void OnDestroy()
        {
            hSrc?.Dispose(); hDst?.Dispose();
            foreach (var r in hidden) if (r) r.enabled = true;
            if (dst) Destroy(dst.gameObject);
        }
    }
}
