using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BlueReaper.Player
{
    [RequireComponent(typeof(PlayerSouls))]
    public sealed class PlayerSkillTree : NetworkBehaviour
    {
        [SerializeField] private SkillNodeData[] nodes = Array.Empty<SkillNodeData>();

        private readonly List<int> taken = new();
        private NetworkList<int> syncedTaken;
        private PlayerSouls souls;
        private bool windowOpen;

        private bool IsAuthority => !IsSpawned || IsServer;
        private bool IsMine => !IsSpawned || IsOwner;

        private void Awake()
        {
            souls = GetComponent<PlayerSouls>();
            syncedTaken = new NetworkList<int>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) ReloadFromBank();
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) ProgressBank.Get(OwnerClientId).Nodes = taken.ToArray();
        }

        internal void ReloadFromBank()
        {
            if (!IsSpawned || !IsServer) return;
            taken.Clear();
            foreach (int index in ProgressBank.Get(OwnerClientId).Nodes)
                if (index >= 0 && index < nodes.Length && nodes[index] && !taken.Contains(index)) taken.Add(index);
            syncedTaken.Clear();
            foreach (int index in taken) syncedTaken.Add(index);
        }

        internal int[] ToIndices(string[] names)
        {
            var indices = new List<int>();
            foreach (string nodeName in names ?? Array.Empty<string>())
            {
                int index = Array.FindIndex(nodes, node => node && node.name == nodeName);
                if (index >= 0) indices.Add(index);
            }
            return indices.ToArray();
        }

        internal string[] ToNames(int[] indices)
        {
            var names = new List<string>();
            foreach (int index in indices ?? Array.Empty<int>())
                if (index >= 0 && index < nodes.Length && nodes[index]) names.Add(nodes[index].name);
            return names.ToArray();
        }

        public float GetMultiplier(SkillStat stat)
        {
            float percent = 0f;
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i] && nodes[i].Stat == stat && IsTaken(i)) percent += nodes[i].Percent;
            return 1f + percent / 100f;
        }

        private bool IsTaken(int index) => IsAuthority ? taken.Contains(index) : syncedTaken.Contains(index);

        private bool IsUnlocked(int index)
        {
            SkillNodeData parent = nodes[index].Parent;
            return !parent || IsTaken(Array.IndexOf(nodes, parent));
        }

        internal void RequestTake(int index)
        {
            if (IsAuthority) TryTake(index);
            else TakeRpc(index);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void TakeRpc(int index) => TryTake(index);

        private void TryTake(int index)
        {
            if (index < 0 || index >= nodes.Length || !nodes[index]) return;
            if (IsTaken(index) || !IsUnlocked(index)) return;
            if (!souls.TrySpend(nodes[index].Cost)) return;
            taken.Add(index);
            if (IsSpawned) syncedTaken.Add(index);
            NodeTaken?.Invoke();
        }

#if UNITY_EDITOR
        // 플레이어 에디터 창 전용: 혼·선행 노드 없이 넣고 뺀다.
        // 저장 파일과 이어진 판(로비에서 캐릭터로 들어옴)이면 다음 저장 때 같이 저장되므로 막는다. 판정은 호스트만 한다
        public bool DebugAllowed => IsAuthority && !ProgressBank.Get(OwnerClientId).Loaded;

        public bool DebugIsTaken(int index) => IsTaken(index);

        public void DebugSetTaken(int index, bool on)
        {
            if (!DebugAllowed || index < 0 || index >= nodes.Length || !nodes[index]) return;
            if (on == taken.Contains(index)) return;
            if (on) taken.Add(index);
            else taken.Remove(index);
            if (!IsSpawned) return;
            if (on) syncedTaken.Add(index);
            else syncedTaken.Remove(index);
        }
#endif

        internal event Action NodeTaken;

        internal int[] TakenNodes => taken.ToArray();

        private void Update()
        {
            if (IsMine && Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
                windowOpen = !windowOpen;
        }

        private void OnGUI()
        {
            if (!windowOpen || !IsMine) return;

            var area = new Rect(Screen.width - 300f, 60f, 280f, 40f + nodes.Length * 34f);
            GUI.Box(area, $"스킬트리 (K)   혼 {souls.Souls}");
            for (int i = 0; i < nodes.Length; i++)
            {
                SkillNodeData node = nodes[i];
                if (!node) continue;
                bool isTaken = IsTaken(i);
                bool canTake = !isTaken && IsUnlocked(i) && souls.Souls >= node.Cost;
                string state = isTaken ? "찍음" : IsUnlocked(i) ? $"혼 {node.Cost}" : "잠김";

                GUI.enabled = canTake;
                GUI.color = isTaken ? KindColor(node.Kind) : Color.Lerp(KindColor(node.Kind), Color.gray, 0.6f);
                if (GUI.Button(new Rect(area.x + 10f, area.y + 30f + i * 34f, area.width - 20f, 28f),
                        $"{node.DisplayName}  ({state})"))
                    RequestTake(i);
            }
            GUI.enabled = true;
            GUI.color = Color.white;
        }

        private static Color KindColor(SkillNodeKind kind) => kind switch
        {
            SkillNodeKind.Transform => new Color(1f, 0.35f, 0.3f),
            SkillNodeKind.Core => new Color(1f, 0.82f, 0.25f),
            _ => new Color(0.4f, 0.7f, 1f),
        };
    }
}
