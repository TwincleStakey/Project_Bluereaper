using System;
using System.IO;
using BlueReaper.Core;
using Unity.Netcode;
using UnityEngine;

namespace BlueReaper.Player
{
    [RequireComponent(typeof(PlayerSouls), typeof(PlayerSkillTree), typeof(PlayerLife))]
    public sealed class PlayerProgress : NetworkBehaviour
    {
        [Serializable]
        private sealed class SaveData
        {
            public int souls;
            public string[] nodes = Array.Empty<string>();
        }

        private static bool sentThisSession;
        private static NetworkManager watchedManager;

        private PlayerSouls souls;
        private PlayerSkillTree skills;
        private PlayerLife life;

        private void Awake()
        {
            souls = GetComponent<PlayerSouls>();
            skills = GetComponent<PlayerSkillTree>();
            life = GetComponent<PlayerLife>();
        }

        public override void OnNetworkSpawn()
        {
            WatchSession();

            if (IsServer)
            {
                ProgressBank.Entry entry = ProgressBank.Get(OwnerClientId);
                if (entry.Loaded) SaveRpc(entry.Souls, entry.Nodes);
                skills.NodeTaken += OnNodeTaken;
                GameEvents.StageCleared += OnStageCleared;
            }

            // 팀원은 거점 첫 스폰 때 캐릭터 번호가 아직 0이다 (GameFlow가 번호를 받기 전에 스폰).
            // 번호가 정해진 다음 스폰에서 불러온다. 그전엔 Loaded가 false라 저장도 안 한다.
            if (IsOwner && !sentThisSession && life.SaveSlot > 0)
            {
                sentThisSession = true;
                SaveData data = Read(life.SaveSlot);
                LoadRpc(data.souls, skills.ToIndices(data.nodes));
            }
        }

        public override void OnNetworkDespawn()
        {
            skills.NodeTaken -= OnNodeTaken;
            GameEvents.StageCleared -= OnStageCleared;
        }

        // 노드를 찍으면 바로 저장. 판 중간이면 실패해도 남는 혼만 저장한다
        private void OnNodeTaken() => SaveNow(souls.SafeSouls);

        // 디렉션 6-4: 스테이지를 클리어할 때마다 자동 저장
        private void OnStageCleared() => SaveNow(souls.Souls);

        private void SaveNow(int soulsToKeep)
        {
            // 아직 저장 파일을 못 받았으면 빈 값으로 덮어쓰지 않는다
            if (!ProgressBank.Get(OwnerClientId).Loaded) return;
            SaveRpc(soulsToKeep, skills.TakenNodes);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, DeferLocal = true)]
        private void LoadRpc(int savedSouls, int[] savedNodes)
        {
            ProgressBank.Entry entry = ProgressBank.Get(OwnerClientId);
            if (entry.Loaded) return;
            entry.Loaded = true;
            entry.Souls = Mathf.Max(0, savedSouls);
            entry.Nodes = savedNodes ?? Array.Empty<int>();
            souls.ReloadFromBank();
            skills.ReloadFromBank();
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server, DeferLocal = true)]
        private void SaveRpc(int currentSouls, int[] currentNodes) =>
            Write(life.SaveSlot, new SaveData { souls = currentSouls, nodes = skills.ToNames(currentNodes) });

        private void WatchSession()
        {
            if (watchedManager == NetworkManager) return;
            if (watchedManager) watchedManager.OnClientStopped -= OnSessionStopped;
            watchedManager = NetworkManager;
            watchedManager.OnClientStopped += OnSessionStopped;
        }

        private static void OnSessionStopped(bool wasHost)
        {
            sentThisSession = false;
            ProgressBank.Clear();
        }

        private static string PathFor(int slot) =>
            Path.Combine(Application.persistentDataPath, "Saves", $"Warrior_{slot}.json");

        private static SaveData Read(int slot)
        {
            string path = PathFor(slot);
            if (slot <= 0 || !File.Exists(path)) return new SaveData();
            try
            {
                return JsonUtility.FromJson<SaveData>(File.ReadAllText(path)) ?? new SaveData();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"저장 파일을 읽지 못해 새로 시작합니다: {path}\n{exception.Message}");
                return new SaveData();
            }
        }

        private static void Write(int slot, SaveData data)
        {
            if (slot <= 0) return;
            string path = PathFor(slot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(data, true));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            sentThisSession = false;
            watchedManager = null;
        }
    }
}
