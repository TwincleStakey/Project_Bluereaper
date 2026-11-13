#if UNITY_EDITOR
using System.IO;
using System.Text;
using BlueReaper.Core;
using BlueReaper.Network;
using BlueReaper.Player;
using UnityEngine;

namespace BlueReaper.DebugTools
{
    /// <summary>
    /// 테스트 전용 (에디터에서만). Multiplayer Play Mode의 Player 2 같은 가상 플레이어 창이
    /// 화면을 조작하지 않아도 외근조에 합류하고, 자기 화면에 보이는 플레이어 상태를 1초마다 로그에 남긴다.
    /// 메인 에디터가 프로젝트 폴더의 Temp/mppm_join.txt에 외근 코드를 쓰면 그 코드로 합류한다.
    /// 로그 위치: Library/VP/&lt;가상 플레이어 ID&gt;/Logs/Editor.log
    /// </summary>
    public class DebugAutoJoin : MonoBehaviour
    {
        string joinFile;
        string lastCode;
        float nextReport;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateInVirtualPlayer()
        {
            // 가상 플레이어는 메인 프로젝트의 Library/VP/<ID>/ 안에서 실행된다. 메인 에디터에서는 아무것도 안 함
            string data = Application.dataPath.Replace('\\', '/');
            int vp = data.IndexOf("/Library/VP/");
            if (vp < 0) return;

            var go = new GameObject(nameof(DebugAutoJoin));
            DontDestroyOnLoad(go);
            go.AddComponent<DebugAutoJoin>().joinFile = data.Substring(0, vp) + "/Temp/mppm_join.txt";
        }

        void Update()
        {
            var service = SessionService.Instance;
            if (!service) return;

            if (!service.InSession && !service.Busy && File.Exists(joinFile))
            {
                string code = File.ReadAllText(joinFile).Trim();
                if (code != lastCode)
                {
                    lastCode = code;
                    Debug.Log($"[AutoJoin] 합류 시도 {code}");
                    service.JoinTeam(code);
                }
            }

            if (service.InSession && Time.time >= nextReport)
            {
                nextReport = Time.time + 1f;
                Report();
            }
        }

        static void Report()
        {
            var line = new StringBuilder("[AutoJoin] ");
            foreach (var life in FindObjectsByType<PlayerLife>(FindObjectsSortMode.InstanceID))
            {
                var health = life.GetComponent<Health>();
                line.Append($"{life.name}(주인 {life.OwnerClientId}) HP {health.Current:0} {life.State} 남은 {life.BleedOutLeft:0.0}초 ")
                    .Append($"구조 {life.RescueProgress:0.00} 위치 {life.transform.position} | ");
            }
            Debug.Log(line);
        }
    }
}
#endif
