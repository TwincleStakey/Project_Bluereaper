using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.Player
{
    /// <summary>
    /// 빈사 연출 (시안 A, 2026-11-13 선택): 옆으로 쓰러지며 회색으로, 머리 위 링에 남은 시간·구조 게이지·남은 초,
    /// 구조할 수 있으면 내 머리 위에 F 안내, 일어날 때 발밑 번쩍.
    /// 보여 주기만 한다. 상태·시간·게이지는 PlayerLife가 정한다 (네트워크로 받은 값도 그대로 그림).
    /// </summary>
    [RequireComponent(typeof(PlayerLife), typeof(Health))]
    public class PlayerDownedView : MonoBehaviour
    {
        [Header("쓰러짐")]
        [SerializeField] float fallTime = 0.45f;
        [Tooltip("누웠을 때 몸 중심 높이 (캡슐 반지름)")]
        [SerializeField] float lyingHeight = 0.5f;
        [SerializeField] Color downedTint = new(0.42f, 0.43f, 0.45f);

        [Header("머리 위 링")]
        [SerializeField] float ringHeight = 1.75f;
        [SerializeField] float ringRadius = 0.5f;
        [SerializeField, ColorUsage(true, true)] Color timerColor = new(0.45f, 1.9f, 1.75f, 1f);
        [SerializeField, ColorUsage(true, true)] Color rescueColor = new(0.45f, 2.6f, 0.7f, 1f);

        [Header("F 안내 (구조하는 쪽)")]
        [SerializeField] float promptHeight = 2.8f;

        const float ReviveFlashTime = 0.6f;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color RingBackColor = new(0.05f, 0.08f, 0.09f, 0.8f);

        PlayerLife life;
        PlayerRescuer rescuer;
        Health health;

        // 쓰러뜨릴 몸 조각 (프리팹의 Body, Nose 등 렌더러가 붙은 자식)
        Transform[] parts;
        Vector3[] partPositions;
        Quaternion[] partRotations;
        Renderer[] partRenderers;
        Color[] partColors;
        MaterialPropertyBlock block;

        Transform hud, prompt;
        LineRenderer ringBack, ringTimer, ringRescue, promptDisc, flash;
        TextMesh countdown;
        float fallen;      // 0 = 서 있음, 1 = 누움
        float flashLeft;

        void Awake()
        {
            life = GetComponent<PlayerLife>();
            health = GetComponent<Health>();
            rescuer = GetComponent<PlayerRescuer>();
            block = new MaterialPropertyBlock();
            CacheBodyParts();
            BuildHud();
        }

        void OnEnable() => health.Revived += OnRevived;

        void OnDisable() => health.Revived -= OnRevived;

        void LateUpdate()
        {
            bool down = life.State != LifeState.Alive;
            fallen = Mathf.MoveTowards(fallen, down ? 1f : 0f, Time.deltaTime / fallTime);
            ApplyPose(Mathf.SmoothStep(0f, 1f, fallen));

            Camera cam = Camera.main;
            if (cam == null) return;
            Quaternion facing = cam.transform.rotation;
            // 링은 XZ 평면에 그리므로, 카메라를 향하게 눕힌다
            Quaternion ringFacing = facing * Quaternion.Euler(-90f, 0f, 0f);

            UpdateHeadRing(facing, ringFacing);
            UpdatePrompt(facing, ringFacing);
            UpdateFlash();
        }

        void UpdateHeadRing(Quaternion facing, Quaternion ringFacing)
        {
            bool show = life.State == LifeState.Downed;
            hud.gameObject.SetActive(show);
            if (!show) return;

            hud.SetPositionAndRotation(transform.position + Vector3.up * ringHeight, ringFacing);
            DrawArc(ringBack, ringRadius, 1f, RingBackColor);
            DrawArc(ringTimer, ringRadius, life.BleedOutLeft / Mathf.Max(0.01f, life.BleedOutTime), timerColor);
            DrawArc(ringRescue, ringRadius + 0.14f, life.RescueProgress, rescueColor);
            countdown.text = Mathf.CeilToInt(life.BleedOutLeft).ToString();
            countdown.transform.rotation = facing;
        }

        void UpdatePrompt(Quaternion facing, Quaternion ringFacing)
        {
            // 내가 조작하는 캐릭터에만 보인다 (네트워크 안 켠 샌드박스에선 조작이 켜진 캐릭터)
            bool mine = rescuer != null && rescuer.enabled && (!rescuer.IsSpawned || rescuer.IsOwner);
            bool show = mine && rescuer.Target != null;
            prompt.gameObject.SetActive(show);
            if (!show) return;

            float pulse = rescuer.IsRescuing ? 0.9f + 0.1f * Mathf.Abs(Mathf.Sin(Time.time * 10f)) : 1f;
            prompt.SetPositionAndRotation(transform.position + Vector3.up * promptHeight, ringFacing);
            prompt.localScale = Vector3.one * pulse;
            // 글자는 카메라를 정면으로, 원판보다 살짝 카메라 쪽에 (같은 자리면 원판에 가려짐)
            Transform key = prompt.GetChild(1);
            key.rotation = facing;
            key.position = prompt.position - facing * Vector3.forward * 0.05f;
        }

        void UpdateFlash()
        {
            flash.enabled = flashLeft > 0f;
            if (flashLeft <= 0f) return;

            flashLeft -= Time.deltaTime;
            float k = 1f - Mathf.Clamp01(flashLeft / ReviveFlashTime);
            flash.transform.SetPositionAndRotation(transform.position + Vector3.up * 0.05f, Quaternion.identity);
            DrawArc(flash, Mathf.Lerp(0.4f, 2.6f, k), 1f, new Color(2.5f, 3f, 2.5f, 1f - k));
        }

        void OnRevived() => flashLeft = ReviveFlashTime;

        /// <summary>몸 조각을 발밑 기준으로 옆으로 눕히고 회색으로 물들인다. k: 0 서 있음 ~ 1 누움.</summary>
        void ApplyPose(float k)
        {
            Quaternion tip = Quaternion.AngleAxis(88f * k, Vector3.forward);
            Vector3 lift = Vector3.up * (lyingHeight * k);
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i].localPosition = tip * partPositions[i] + lift;
                parts[i].localRotation = tip * partRotations[i];

                partRenderers[i].GetPropertyBlock(block);
                block.SetColor(BaseColorId, Color.Lerp(partColors[i], downedTint, k));
                partRenderers[i].SetPropertyBlock(block);
            }
        }

        void CacheBodyParts()
        {
            var renderers = new System.Collections.Generic.List<Renderer>();
            foreach (Transform child in transform)
                if (child.TryGetComponent(out Renderer r) && r is MeshRenderer) renderers.Add(r);

            partRenderers = renderers.ToArray();
            parts = new Transform[partRenderers.Length];
            partPositions = new Vector3[parts.Length];
            partRotations = new Quaternion[parts.Length];
            partColors = new Color[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = partRenderers[i].transform;
                partPositions[i] = parts[i].localPosition;
                partRotations[i] = parts[i].localRotation;
                Material m = partRenderers[i].sharedMaterial;
                partColors[i] = m != null && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
            }
        }

        void BuildHud()
        {
            hud = new GameObject("DownedRing").transform;
            hud.SetParent(transform, false);
            ringBack = MakeLine(hud, 0.09f);
            ringTimer = MakeLine(hud, 0.09f);
            ringRescue = MakeLine(hud, 0.12f);
            countdown = MakeText(hud, Color.white);
            hud.gameObject.SetActive(false);

            prompt = new GameObject("RescuePrompt").transform;
            prompt.SetParent(transform, false);
            // 굵기를 반지름의 2배로 그린 링 = 꽉 찬 원판
            promptDisc = MakeLine(prompt, 0.28f);
            DrawArc(promptDisc, 0.14f, 1f, new Color(0.92f, 0.92f, 0.92f, 1f));
            TextMesh key = MakeText(prompt, new Color(0.08f, 0.08f, 0.08f));
            key.text = "F";
            prompt.gameObject.SetActive(false);

            flash = MakeLine(transform, 0.18f);
            flash.enabled = false;
        }

        static LineRenderer MakeLine(Transform parent, float width)
        {
            var go = new GameObject("Ring");
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            line.startWidth = line.endWidth = width;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        static TextMesh MakeText(Transform parent, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var textRenderer = go.GetComponent<MeshRenderer>();
            textRenderer.sharedMaterial = text.font.material;
            // 링·원판(같은 반투명 큐)보다 나중에 그려서 글자가 위로 오게
            textRenderer.sortingOrder = 10;
            text.fontSize = 64;
            text.characterSize = 0.045f;
            text.anchor = TextAnchor.MiddleCenter;
            text.fontStyle = FontStyle.Bold;
            text.color = color;
            return text;
        }

        /// <summary>XZ 평면에 위(12시)부터 시계 방향으로 fill(0~1)만큼 원호를 그린다.</summary>
        static void DrawArc(LineRenderer line, float radius, float fill, Color color)
        {
            line.enabled = fill > 0.001f;
            if (!line.enabled) return;

            line.sharedMaterial.color = color;
            int segments = Mathf.Max(2, Mathf.CeilToInt(72 * fill));
            line.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.PI / 2f - 2f * Mathf.PI * fill * i / segments;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
            }
        }
    }
}
