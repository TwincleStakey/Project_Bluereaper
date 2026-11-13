using System;
using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.Player
{
    public enum LifeState { Alive, Downed, Dead }

    /// <summary>
    /// 플레이어 생존 상태. 정상 → (HP 0) 빈사 → (동료 구조) 정상 / (시간 초과) 사망.
    /// 규칙: 홍유정 UI 기획 "파티원 실시간 상태창". 수치는 [가안].
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class PlayerLife : MonoBehaviour
    {
        [Tooltip("빈사 후 이 시간 안에 구조 못 받으면 사망 [가안: 기획서 예시 15초]")]
        [SerializeField] float bleedOutTime = 15f;
        [Tooltip("구조 게이지를 다 채우는 데 걸리는 시간 [가안]")]
        [SerializeField] float rescueTime = 3f;
        [Tooltip("구조되면 최대 체력의 이 비율로 일어난다 [가안]")]
        [SerializeField, Range(0.01f, 1f)] float reviveHealthRatio = 0.3f;

        Health health;

        public LifeState State { get; private set; } = LifeState.Alive;
        /// <summary>사망까지 남은 시간(초). UI 카운트다운용.</summary>
        public float BleedOutLeft { get; private set; }
        /// <summary>구조 게이지 0~1.</summary>
        public float RescueProgress { get; private set; }

        public event Action<LifeState> StateChanged;

        void Awake() => health = GetComponent<Health>();

        void OnEnable() => health.Died += OnHealthDepleted;

        void OnDisable() => health.Died -= OnHealthDepleted;

        void Update()
        {
            if (State != LifeState.Downed) return;

            BleedOutLeft -= Time.deltaTime;
            if (BleedOutLeft <= 0f) SetState(LifeState.Dead);
        }

        /// <summary>구조하는 플레이어가 버튼을 누르고 있는 동안 매 프레임 부른다. 다 차면 일어난다.</summary>
        public void AddRescueProgress(float deltaTime)
        {
            if (State != LifeState.Downed) return;

            RescueProgress = Mathf.Min(1f, RescueProgress + deltaTime / rescueTime);
            if (RescueProgress < 1f) return;

            // Revive가 Health.Revived를 먼저 쏘고, 그다음 상태를 Alive로
            health.Revive(reviveHealthRatio);
            SetState(LifeState.Alive);
        }

        void OnHealthDepleted()
        {
            if (State != LifeState.Alive) return;

            BleedOutLeft = bleedOutTime;
            RescueProgress = 0f;
            SetState(LifeState.Downed);
        }

        void SetState(LifeState next)
        {
            State = next;
            StateChanged?.Invoke(next);
            // 적 추적 목록에서 빼는 건 StageController가 Health.Died로 처리 (구조 후 다시 넣기는 Health.Revived — 성규님과 협의)
            if (next == LifeState.Dead) GameEvents.RaisePlayerDied(transform);
        }
    }
}
