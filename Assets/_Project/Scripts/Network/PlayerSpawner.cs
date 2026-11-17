using Unity.Netcode;
using UnityEngine;

namespace BlueReaper.Network
{
    /// <summary>
    /// 팀원이 접속하면 호스트가 그 사람의 Player를 만들어 준다 (주인 = 그 팀원).
    /// 임시: 호스트는 씬에 놓인 Player를 그대로 쓴다. StageController가 접속한 플레이어를
    /// 실행 중에 등록할 수 있게 되면(성규님과 협의) 호스트도 여기서 만들도록 바꾼다.
    /// </summary>
    public class PlayerSpawner : MonoBehaviour
    {
        [SerializeField] NetworkObject playerPrefab;
        [Tooltip("호스트가 쓰는 씬의 Player. 팀원은 이 옆에 생긴다")]
        [SerializeField] Transform hostPlayer;
        [Tooltip("팀원끼리 옆으로 떨어뜨릴 간격(m)")]
        [SerializeField] float spacing = 2f;

        void Start() => NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

        void OnDestroy()
        {
            if (NetworkManager.Singleton) NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }

        void OnClientConnected(ulong clientId)
        {
            var manager = NetworkManager.Singleton;
            // 호스트 자신은 씬의 Player를 쓴다. 팀원 쪽 PC에서도 이 콜백이 오지만 만드는 건 호스트만
            if (!manager.IsServer || clientId == NetworkManager.ServerClientId) return;

            int slot = manager.ConnectedClientsIds.Count - 1; // 1 = 2P, 2 = 3P
            Vector3 position = hostPlayer.position + hostPlayer.right * spacing * slot;
            var player = Instantiate(playerPrefab, position, hostPlayer.rotation);
            player.SpawnAsPlayerObject(clientId, destroyWithScene: true);
        }
    }
}
