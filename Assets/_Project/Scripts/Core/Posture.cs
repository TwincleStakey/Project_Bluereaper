using System;
using UnityEngine;

namespace BlueReaper.Core
{
    public class Posture : MonoBehaviour
    {
        public float CurrentPosture => posture;
        public float MaxPosture => maxPosture;
        public bool IsGroggy => Time.time < groggyUntil;
        public event Action GroggyStarted;

        [SerializeField, Min(0f)]
        private float maxPosture = 120f;
        [SerializeField, Min(0f)]
        private float refillGraceSec = 5f;
        [SerializeField, Min(0f), Tooltip("자세 0에서 최대치까지 회복하는 데 걸리는 시간(초).")]
        private float refillDuration = 40f;
        [SerializeField, Min(0f)]
        private float groggyTime = 4f;
        [SerializeField, Min(0.1f)]
        private float postureDamageRatio = 1.0f;

        private bool postureRefillFromGroggy = false;

        private float posture = 0f;
        private float refillAt;
        private float groggyUntil;
        private Health health;

        private event Action<float> onChangedPosture = null;

        public void AddListener(Action<float> onChanged)
        {
            onChangedPosture += onChanged;
        }

        public void RemoveListener(Action<float> onChanged)
        {
            onChangedPosture -= onChanged;
        }

        public void AllRemoveListner()
        {
            onChangedPosture = null;
        }

        public void DamagedToPosture(float postureDamage)
        {
            if (!isActiveAndEnabled || postureRefillFromGroggy || !(postureDamage > 0f) || posture <= 0f || IsGroggy) return;

            float nextPosture = Mathf.Max(0f, posture - postureDamage);
            if (nextPosture >= posture) return;

            float now = Time.time;
            posture = nextPosture;
            refillAt = now + refillGraceSec;
            if (posture <= 0f && !IsGroggy)
            {
                postureRefillFromGroggy = true;
                groggyUntil = now + groggyTime;
                GroggyStarted?.Invoke();
            }
            onChangedPosture?.Invoke(posture);
        }

        public void FillPosture()
        {
            posture = maxPosture;
            postureRefillFromGroggy = false;

            onChangedPosture?.Invoke(posture);
        }

        private void OnEnable()
        {
            health = GetComponent<Health>();
            if (health) health.Damaged += OnHealthDamaged;

            refillAt = 0f;
            groggyUntil = 0f;
            FillPosture();
        }

        private void OnDisable()
        {
            if (health) health.Damaged -= OnHealthDamaged;
        }

        private void OnHealthDamaged(float amount)
        {
            if (!health.IsDead)
                DamagedToPosture(amount * postureDamageRatio);
        }

        private void Update()
        {
            if (postureRefillFromGroggy)
            {
                if (!IsGroggy) FillPosture();
                return;
            }

            if (posture >= maxPosture || Time.time < refillAt) return;
            if (refillDuration <= 0f)
            {
                FillPosture();
                return;
            }

            float recoveryTime = Mathf.Min(Time.deltaTime, Time.time - refillAt);
            if (recoveryTime <= 0f) return;

            float nextPosture = Mathf.Min(maxPosture, posture + maxPosture / refillDuration * recoveryTime);
            if (nextPosture <= posture) return;

            posture = nextPosture;
            onChangedPosture?.Invoke(posture);
        }
    }
}
