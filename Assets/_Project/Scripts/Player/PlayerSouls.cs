using System;
using System.Collections.Generic;
using BlueReaper.Core;
using Unity.Netcode;
using UnityEngine;

namespace BlueReaper.Player
{
    public sealed class PlayerSouls : NetworkBehaviour
    {
        [SerializeField, Min(0)] private int normalSouls = 1;
        [SerializeField, Min(0)] private int eliteSouls = 10;
        [SerializeField, Min(0)] private int bossSouls;

        private readonly NetworkVariable<int> syncedSouls = new();
        private readonly Dictionary<Enemy, (Health Health, Action OnDied)> watched = new();
        private int souls;
        private int earnedThisRun;
        private bool runCleared;
        private bool listening;

        public int Souls => IsAuthority ? souls : syncedSouls.Value;

        private bool IsAuthority => !IsSpawned || IsServer;

        public override void OnNetworkSpawn()
        {
            StopListening();
            if (IsServer) BeginListening();
        }

        public override void OnNetworkDespawn() => EndListening();

        private void Start()
        {
            if (!IsSpawned) BeginListening();
        }

        public override void OnDestroy()
        {
            EndListening();
            base.OnDestroy();
        }

        private void BeginListening()
        {
            if (listening) return;
            listening = true;
            souls = ProgressBank.Get(OwnerClientId).Souls;
            earnedThisRun = 0;
            runCleared = false;
            PublishSouls();

            Enemy.PresenceChanged += OnEnemyPresenceChanged;
            GameEvents.StageCleared += OnStageCleared;
            foreach (var enemy in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                Watch(enemy);
        }

        private void EndListening()
        {
            if (!listening) return;
            StopListening();
            ProgressBank.Get(OwnerClientId).Souls = SafeSouls;
        }

        // 지금 판이 실패로 끝나도 남는 혼 (클리어했으면 전부)
        internal int SafeSouls => runCleared ? souls : Mathf.Max(0, souls - (earnedThisRun - earnedThisRun / 2));

        internal void ReloadFromBank()
        {
            if (!listening) return;
            souls = ProgressBank.Get(OwnerClientId).Souls;
            earnedThisRun = 0;
            PublishSouls();
        }

        private void StopListening()
        {
            if (!listening) return;
            listening = false;
            Enemy.PresenceChanged -= OnEnemyPresenceChanged;
            GameEvents.StageCleared -= OnStageCleared;
            foreach (var entry in watched.Values) entry.Health.Died -= entry.OnDied;
            watched.Clear();
        }

        private void OnEnemyPresenceChanged(Enemy enemy, bool present)
        {
            if (present) Watch(enemy);
            else Unwatch(enemy);
        }

        private void Watch(Enemy enemy)
        {
            if (!enemy || watched.ContainsKey(enemy) || !enemy.TryGetComponent(out Health health)) return;
            Action onDied = () => AddSouls(GetReward(enemy.Rank));
            health.Died += onDied;
            watched.Add(enemy, (health, onDied));
        }

        private void Unwatch(Enemy enemy)
        {
            if (!enemy || !watched.Remove(enemy, out var entry)) return;
            entry.Health.Died -= entry.OnDied;
        }

        private int GetReward(EnemyRank rank) => rank switch
        {
            EnemyRank.Elite => eliteSouls,
            EnemyRank.Boss => bossSouls,
            _ => normalSouls,
        };

        private void AddSouls(int amount)
        {
            if (amount <= 0) return;
            souls += amount;
            earnedThisRun += amount;
            PublishSouls();
        }

        internal bool TrySpend(int amount)
        {
            if (!IsAuthority || amount < 0 || souls < amount) return false;
            souls -= amount;
            earnedThisRun = Mathf.Max(0, earnedThisRun - amount);
            PublishSouls();
            return true;
        }

#if UNITY_EDITOR
        // 플레이어 에디터 창 전용: 테스트용 혼. 판 수입이 아니라 실패해도 깎이지 않는다.
        // 저장 파일과 이어진 판이면 저장 파일에 섞이므로 막는다 (PlayerSkillTree.DebugAllowed와 같은 조건)
        public void DebugAddSouls(int amount)
        {
            if (!IsAuthority || ProgressBank.Get(OwnerClientId).Loaded) return;
            int before = souls;
            souls = Mathf.Max(0, souls + amount);
            // 줄일 때는 이번 판 수입도 같이 줄인다 (TrySpend와 같음). 안 그러면 0으로 만든 뒤 넣은 테스트 혼이 실패 때 깎인다
            if (souls < before) earnedThisRun = Mathf.Max(0, earnedThisRun - (before - souls));
            PublishSouls();
        }
#endif

        private void OnStageCleared() => runCleared = true;

        private void PublishSouls()
        {
            if (IsSpawned && IsServer) syncedSouls.Value = souls;
        }

        private void OnGUI()
        {
            if (IsSpawned && !IsOwner) return;
            GUI.Label(new Rect(Screen.width * 0.5f - 60f, 8f, 120f, 24f), $"혼 {Souls}");
        }
    }
}
