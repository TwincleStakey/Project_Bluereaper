using System.Collections;
using BlueReaper.Network;
using BlueReaper.Stage;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlueReaper.GameFlow
{
    [RequireComponent(typeof(SoloMenu))]
    public sealed class SoloGameFlow : MonoBehaviour
    {
        const string StagePath = "Assets/_Project/Scenes/SoloStage.unity";

        [SerializeField] SessionService session;
        [SerializeField] NetworkManager networkManager;
        [SerializeField] StageData[] stages;

        SoloMenu menu;
        int characterCount;
        int character;
        StageData selectedStage;
        bool leaving;
        bool inStage;
        SoloStageResult? result;

        string CharacterName => $"Warrior {character}";

        void Awake()
        {
            menu = GetComponent<SoloMenu>();
            networkManager.ConnectionApprovalCallback = ApproveLocalHost;
            session.SessionStarted += ShowHub;
            session.SessionFailed += ShowLobby;
            session.SessionEnded += OnSessionEnded;
        }

        void Start() => ShowTitle();

        void OnDestroy()
        {
            session.SessionStarted -= ShowHub;
            session.SessionFailed -= ShowLobby;
            session.SessionEnded -= OnSessionEnded;
            if (networkManager) networkManager.ConnectionApprovalCallback -= ApproveLocalHost;
        }

        static void ApproveLocalHost(NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = request.ClientNetworkId == NetworkManager.ServerClientId;
        }

        void ShowTitle()
        {
            menu.Show("BlueReaper");
            menu.Button("New Game", () =>
            {
                characterCount = character = 0;
                ShowLobby();
            });
            menu.Button("Continue", null);
            menu.Button("Settings", ShowSettings);
            menu.Button("Quit", Application.Quit);
        }

        void ShowSettings()
        {
            menu.Show("Settings");
            menu.Volume();
            menu.Button(Screen.fullScreen ? "Switch to Windowed" : "Switch to Fullscreen", () =>
            {
                Screen.fullScreen = !Screen.fullScreen;
                // Unity applies this change at the end of the frame.
                StartCoroutine(RefreshSettings());
            });
            menu.Button("Back", ShowTitle);
        }

        IEnumerator RefreshSettings()
        {
            yield return null;
            ShowSettings();
        }

        void ShowLobby(string message = null)
        {
            menu.Show("Character Lobby", message);
            menu.Button("Create Warrior", () =>
            {
                character = ++characterCount;
                ShowLobby();
            });
            menu.Label(character == 0 ? "No Character Selected" : $"Selected: {CharacterName}");
            if (characterCount > 1) menu.Button("Next Character", SelectNextCharacter);
            menu.Button("Start Solo", character > 0 && !leaving ? session.StartSolo : null);
            menu.Button("Back to Title", leaving ? null : ShowTitle);
        }

        void SelectNextCharacter()
        {
            character = character % characterCount + 1;
            ShowLobby();
        }

        void ShowHub() => ShowHub(null);

        void ShowHub(string message)
        {
            menu.Show("Base", message);
            menu.Label(CharacterName);
            foreach (StageData stage in stages)
            {
                if (!stage) continue;
                string name = stage.name.Replace('_', ' ');
                menu.Button(stage == selectedStage ? $"Selected: {name}" : $"Select {name}", () =>
                {
                    selectedStage = stage;
                    ShowHub(message);
                });
            }
            menu.Button("Depart", selectedStage ? Depart : null);
            menu.Button("Back to Lobby", LeaveForLobby);
        }

        void LeaveForLobby()
        {
            selectedStage = null;
            leaving = true;
            ShowLobby();
            session.Leave();
        }

        void OnSessionEnded()
        {
            bool expected = leaving;
            leaving = false;
            selectedStage = null;
            if (!inStage) ShowLobby(expected ? null : "Solo session ended.");
        }

        void Depart()
        {
            if (inStage || !session.InSession) return;
            if (!Application.CanStreamedLevelBeLoaded(StagePath))
            {
                ShowHub("Stage scene is unavailable.");
                return;
            }
            StartCoroutine(RunStage());
        }

        IEnumerator RunStage()
        {
            inStage = true;
            result = null;
            Scene menuScene = gameObject.scene;
            menu.Show("Loading Stage");
            yield return SceneManager.LoadSceneAsync(StagePath, LoadSceneMode.Additive);

            Scene scene = SceneManager.GetSceneByPath(StagePath);
            SoloStage entry = null;
            StageController controller = null;
            if (scene.isLoaded && session.InSession)
            {
                SceneManager.SetActiveScene(scene);
                entry = FindFirstObjectByType<SoloStage>();
                if (entry && entry.gameObject.scene == scene && entry.Controller)
                {
                    controller = entry.Controller;
                    controller.SoloRunFinished += RecordResult;
                    if (entry.TryBegin(selectedStage, CharacterName))
                    {
                        menu.Hide();
                        yield return new WaitUntil(() => result.HasValue || !session.InSession);
                    }
                }
            }

            menu.Show("Returning to Base");
            if (controller && entry)
            {
                controller.SoloRunFinished -= RecordResult;
                entry.StopRun();
            }
            SceneManager.SetActiveScene(menuScene);
            if (scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);

            inStage = false;
            if (!session.InSession)
            {
                ShowLobby("Solo session ended.");
                yield break;
            }
            ShowHub(result switch
            {
                SoloStageResult.Cleared => "Stage cleared.",
                SoloStageResult.PlayerDefeated => "Defeated. Ready to redeploy.",
                _ => "Stage could not be started."
            });
        }

        void RecordResult(SoloStageResult outcome) => result = outcome;
    }
}
