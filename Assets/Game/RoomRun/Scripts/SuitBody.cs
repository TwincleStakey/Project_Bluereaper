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
        public static bool On => Force ?? PlayerPrefs.GetInt(Key, 0) != 0;
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
            go.transform.localScale = hero.transform.localScale * Mathf.Clamp(k * SizeMul, 0.4f, 2.5f);
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
            foreach (var r in hero.GetComponentsInChildren<Renderer>(true)) if (r.enabled) { r.enabled = false; hidden.Add(r); }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
#if UNITY_EDITOR
            if (!darkTex) darkTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/RoomRun/ThirdParty/MixamoJoe/Textures/Ch33_1001_Diffuse_dark.png");
#endif
            if (Bulk) BulkUp();
            Matte(go);
            if (Plume) HeadPlume.Attach(dst);
            return true;
        }
        public static float SizeMul = 1.15f;   // the hero is a chibi with a short torso; a little taller reads as an adult

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
                foreach (var m in ms) if (m) Debug.Log($"[SuitBody] {r.name}: {m.name} base={(m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") ? m.GetTexture("_BaseMap").name : "-")} col={(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : "-")} dark={(darkTex ? darkTex.name : "null")}");
            }
        }
        public static bool DarkBody = true;
        Transform pendingSword, heroHand, joeHand;
        static Texture2D darkTex;

        void LateUpdate()
        {
            if (!dst || hSrc == null) return;
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
