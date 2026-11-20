#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text;
using BlueReaper.Core;
using BlueReaper.Network;
using BlueReaper.Player;
using BlueReaper.Stage;
using UnityEngine;
using UnityEngine.UI;

namespace BlueReaper.DebugTools
{
    /// <summary>
    /// 테스트 전용 (에디터에서만). Multiplayer Play Mode의 Player 2 같은 가상 플레이어 창이
    /// 화면을 조작하지 않아도 외근조에 합류하고, 자기 화면에 보이는 플레이어 상태를 1초마다 로그에 남긴다.
    /// 메인 에디터가 프로젝트 폴더의 Temp/mppm_join.txt에 외근 코드를 쓰면,
    /// 로비에서 Warrior 2를 만들어 그 코드로 합류하고, 거점에서는 Ready를 누르고,
    /// 혼이 모이면 이동 속도 노드를 찍는다 (혼·스킬트리·흡혈·저장 멀티 확인용).
    /// 로그 위치: Library/VP/&lt;가상 플레이어 ID&gt;/Logs/Editor.log
    /// </summary>
    public class DebugAutoJoin : MonoBehaviour
    {
        const string SpeedNode = "Warrior_Speed_1";

        string joinFile;
        string lastCode;
        float nextReport;
        bool joining;

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

            if (!service.InSession && !service.Busy && !joining && File.Exists(joinFile))
            {
                string code = File.ReadAllText(joinFile).Trim();
                if (code != lastCode)
                {
                    lastCode = code;
                    StartCoroutine(JoinAsWarrior2(code));
                }
            }

            if (service.InSession && Time.time >= nextReport)
            {
                nextReport = Time.time + 1f;
                // 버튼 대신 누르기는 자동 테스트(메인 에디터가 합류 파일을 씀) 때만. 사람이 Player 2를 조작할 땐 로그만 남긴다
                if (File.Exists(joinFile))
                {
                    if (Click("Ready")) Debug.Log("[AutoJoin] Ready 누름");
                    TakeSpeedNode();
                }
                Report();
            }
        }

        // 로비 버튼을 사람처럼 눌러서 Warrior 2로 합류한다 (캐릭터 번호가 있어야 저장 파일 번호가 생긴다)
        IEnumerator JoinAsWarrior2(string code)
        {
            joining = true;
            Debug.Log($"[AutoJoin] 합류 시도 {code}");
            float giveUp = Time.time + 15f;
            while (!FindButton("Create Warrior") && Time.time < giveUp)
            {
                Click("New Game");
                yield return null;
            }
            while (!HasText("Selected: Warrior 2") && Time.time < giveUp)
            {
                Click("Create Warrior");
                yield return null;
            }

            var field = FindAnyObjectByType<InputField>();
            if (field) field.text = code;
            yield return null;
            Debug.Log(Click("Join Team") ? "[AutoJoin] Join Team 누름" : "[AutoJoin] Join Team 버튼을 못 찾음");
            joining = false;
        }

        static void TakeSpeedNode()
        {
            foreach (var tree in FindObjectsByType<PlayerSkillTree>(FindObjectsSortMode.None))
            {
                if (!tree.IsOwner) continue;
                int[] index = tree.ToIndices(new[] { SpeedNode });
                if (index.Length == 0 || tree.GetMultiplier(SkillStat.MoveSpeed) > 1f) return;
                if (tree.GetComponent<PlayerSouls>().Souls < 5) return;
                Debug.Log("[AutoJoin] 이동 속도 노드 찍기 요청");
                tree.RequestTake(index[0]);
            }
        }

        // 메인 에디터(호스트)에서 MCP 명령으로 부른다: 플레이어를 준비 발판에 한 명씩 올린다
        public static string PlacePartyOnReadyPads()
        {
            var pads = FindObjectsByType<StageReadyPad>(FindObjectsSortMode.InstanceID);
            var players = FindObjectsByType<PlayerController>(FindObjectsSortMode.InstanceID);
            var line = new StringBuilder($"발판 {pads.Length}개, 플레이어 {players.Length}명: ");
            for (int i = 0; i < players.Length && i < pads.Length; i++)
            {
                var box = pads[i].GetComponent<BoxCollider>();
                Vector3 position = box ? box.bounds.center : pads[i].transform.position;
                position.y = pads[i].transform.position.y;
                players[i].PlaceFromHost(position, pads[i].transform.rotation);
                line.Append($"{players[i].name}(주인 {players[i].OwnerClientId}) → {pads[i].name} ");
            }
            return line.ToString();
        }

        static Button FindButton(string label)
        {
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                var text = button.GetComponentInChildren<Text>(true);
                if (text && text.text == label && button.isActiveAndEnabled && button.interactable) return button;
            }
            return null;
        }

        static bool Click(string label)
        {
            var button = FindButton(label);
            if (!button) return false;
            button.onClick.Invoke();
            return true;
        }

        static bool HasText(string value)
        {
            foreach (var text in FindObjectsByType<Text>(FindObjectsSortMode.None))
                if (text.text == value) return true;
            return false;
        }

        static void Report()
        {
            var line = new StringBuilder("[AutoJoin] ");
            foreach (var life in FindObjectsByType<PlayerLife>(FindObjectsSortMode.InstanceID))
            {
                var health = life.GetComponent<Health>();
                var souls = life.GetComponent<PlayerSouls>();
                var tree = life.GetComponent<PlayerSkillTree>();
                line.Append($"{life.name}(주인 {life.OwnerClientId}) HP {health.Current:0} {life.State} ")
                    .Append($"혼 {(souls ? souls.Souls : -1)} 속도배율 {(tree ? tree.GetMultiplier(SkillStat.MoveSpeed) : 0f):0.00} | ");
            }
            Debug.Log(line);
        }
    }
}
#endif
