using BlueReaper.Stage;
using Unity.Netcode;
using UnityEngine;

namespace BlueReaper.Network
{
    /// <summary>
    /// 스테이지 진행을 멀티로 맞춰 주는 연결부. StageController가 스스로 붙인다 (씬 수정 필요 없음).
    /// - 스테이지 진행(필드·웨이브·소환·클리어)은 호스트만 한다.
    /// - 호스트가 필드를 바꾸면 팀원 PC에 "몇 번째 필드"인지 메시지로 알려 주고, 팀원 PC는 같은 필드를 깔기만 한다.
    /// - 적은 호스트가 네트워크 오브젝트로 만들어서 팀원 PC에 자동으로 생긴다 (EnemyNetwork 참고).
    /// </summary>
    [RequireComponent(typeof(StageController))]
    public class StageNetwork : MonoBehaviour
    {
        const string FieldMessage = "BlueReaper.StageField";
        const string FieldRequestMessage = "BlueReaper.StageFieldRequest";

        StageController stage;
        int fieldIndex = -1;

        /// <summary>씬에 NetworkManager가 있는지. 없으면(성규님 샌드박스 등) 예전처럼 혼자 진행한다.</summary>
        public static bool HasNetwork => NetworkManager.Singleton;
        /// <summary>방이 열렸고 이 PC가 호스트(진행 담당)인지.</summary>
        public static bool IsHost => HasNetwork && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer;
        /// <summary>방에 합류한 팀원 PC인지. 팀원 PC는 진행하지 않고 보여 주기만 한다.</summary>
        public static bool IsRemoteClient => HasNetwork && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer;

        /// <summary>호스트에서 풀에서 꺼낸 적을 팀원 PC에도 생기게 한다. 네트워크가 아니면 아무것도 안 함.</summary>
        public static void Spawn(GameObject go)
        {
            if (IsHost && go.TryGetComponent(out NetworkObject networkObject) && !networkObject.IsSpawned)
                networkObject.Spawn(destroyWithScene: true);
        }

        /// <summary>풀로 돌려보내기 전에 부른다. 호스트는 오브젝트를 그대로 두고(재사용), 팀원 PC에서는 사라진다.</summary>
        public static void Despawn(GameObject go)
        {
            if (IsHost && go.TryGetComponent(out NetworkObject networkObject) && networkObject.IsSpawned)
                networkObject.Despawn(destroy: false);
        }

        void Awake() => stage = GetComponent<StageController>();

        void Start()
        {
            var manager = NetworkManager.Singleton;
            manager.OnClientStarted += OnClientStarted;
            manager.OnClientStopped += OnClientStopped;
            manager.OnClientConnectedCallback += OnClientConnected;
            // 이미 방에 들어온 뒤에 이 스테이지가 열렸으면 시작 이벤트를 놓쳤으므로 지금 바로 준비한다
            if (manager.IsListening) OnClientStarted();
        }

        void OnDestroy()
        {
            var manager = NetworkManager.Singleton;
            if (!manager) return;
            manager.OnClientStarted -= OnClientStarted;
            manager.OnClientStopped -= OnClientStopped;
            manager.OnClientConnectedCallback -= OnClientConnected;
            OnClientStopped(false);
        }

        /// <summary>호스트: 새 필드를 깔았을 때(-1이면 치웠을 때) 부른다. 팀원 모두에게 알린다.</summary>
        public void SetField(int index)
        {
            if (!IsHost) return;
            fieldIndex = index;
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                if (clientId != NetworkManager.ServerClientId) SendField(clientId);
        }

        void OnClientStarted()
        {
            var manager = NetworkManager.Singleton;
            if (manager.IsServer)
            {
                // 호스트: 팀원이 "지금 몇 번 필드예요?" 하고 물으면 답한다
                manager.CustomMessagingManager.RegisterNamedMessageHandler(FieldRequestMessage, OnFieldRequest);
                return;
            }

            manager.CustomMessagingManager.RegisterNamedMessageHandler(FieldMessage, OnFieldMessage);
            // 늦게 준비된 경우를 위해 지금 필드를 한 번 물어본다 (호스트 접속 콜백만으로는 놓칠 수 있음)
            using var writer = new FastBufferWriter(0, Unity.Collections.Allocator.Temp);
            manager.CustomMessagingManager.SendNamedMessage(FieldRequestMessage, NetworkManager.ServerClientId, writer);
        }

        void OnClientStopped(bool wasHost)
        {
            var messaging = NetworkManager.Singleton ? NetworkManager.Singleton.CustomMessagingManager : null;
            messaging?.UnregisterNamedMessageHandler(FieldMessage);
            messaging?.UnregisterNamedMessageHandler(FieldRequestMessage);
        }

        void OnFieldRequest(ulong senderId, FastBufferReader reader)
        {
            if (fieldIndex >= 0) SendField(senderId);
        }

        /// <summary>늦게 들어온 팀원에게도 지금 필드를 알려 준다.</summary>
        void OnClientConnected(ulong clientId)
        {
            if (IsHost && clientId != NetworkManager.ServerClientId && fieldIndex >= 0) SendField(clientId);
        }

        void SendField(ulong clientId)
        {
            using var writer = new FastBufferWriter(sizeof(int), Unity.Collections.Allocator.Temp);
            writer.WriteValueSafe(fieldIndex);
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(FieldMessage, clientId, writer);
        }

        void OnFieldMessage(ulong senderId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int index);
            stage.ShowFieldForClient(index);
        }
    }
}
