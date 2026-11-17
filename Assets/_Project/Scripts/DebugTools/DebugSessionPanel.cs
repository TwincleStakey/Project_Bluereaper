using BlueReaper.Network;
using UnityEngine;

namespace BlueReaper.DebugTools
{
    /// <summary>
    /// 샌드박스 전용 임시 버튼. 진짜 [외근 형태 선택] UI가 나오기 전까지 SessionService를 시험한다.
    /// Kibaek 씬에만 둔다.
    /// </summary>
    public class DebugSessionPanel : MonoBehaviour
    {
        string code = "";
        string message = "";

        void Start()
        {
            var service = SessionService.Instance;
            service.SessionStarted += () => message = service.IsSolo ? "단독 외근 시작" : $"외근조 연결됨  코드: {service.JoinCode}";
            service.SessionFailed += m => message = m;
            service.SessionEnded += () => message = "연결 종료";
        }

        void OnGUI()
        {
            var service = SessionService.Instance;
            if (service == null) return;

            GUILayout.BeginArea(new Rect(10, 10, 260, 200), GUI.skin.box);
            GUI.enabled = !service.Busy;
            if (!service.InSession)
            {
                if (GUILayout.Button("단독 외근")) service.StartSolo();
                if (GUILayout.Button("외근조 만들기")) service.CreateTeam();
                GUILayout.BeginHorizontal();
                code = GUILayout.TextField(code, SessionService.CodeLength, GUILayout.Width(120));
                if (GUILayout.Button("외근조 합류")) service.JoinTeam(code);
                GUILayout.EndHorizontal();
            }
            else if (GUILayout.Button("나가기")) service.Leave();
            GUI.enabled = true;

            if (service.Busy) GUILayout.Label("연결 중...");
            GUILayout.Label(message);
            GUILayout.EndArea();
        }
    }
}
