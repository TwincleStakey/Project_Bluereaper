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
        const string HubPath = "Assets/_Project/Scenes/SoloHub.unity";
        const string StagePath = "Assets/_Project/Scenes/SoloStage.unity";

        [SerializeField] SessionService session;
        [SerializeField] NetworkManager networkManager;
        [SerializeField] StageData[] stages;

        SoloMenu menu;
        int characterCount;
        int character;
        StageData selectedStage;
        bool leaving;
        bool inSessionFlow;
        bool departRequested;
        SoloStageResult? result;

        string CharacterName => $"Warrior {character}";

        void Awake()
        {
            menu = GetComponent<SoloMenu>();
            networkManager.ConnectionApprovalCallback = ApproveLocalHost;
            session.SessionStarted += OnSessionStarted;
            session.SessionFailed += ShowLobby;
            session.SessionEnded += OnSessionEnded;
        }

        void Start() => ShowTitle();

        void OnDestroy()
        {
            session.SessionStarted -= OnSessionStarted;
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

        void OnSessionStarted()
        {
            if (inSessionFlow) return;
            inSessionFlow = true;
            StartCoroutine(RunSession());
        }

        void ShowHub(string message)
        {
            menu.Show("Base", message, inWorld: true);
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
            if (!inSessionFlow) ShowLobby("Solo session ended.");
        }

        void Depart()
        {
            if (!session.InSession || leaving || departRequested) return;
            if (!Application.CanStreamedLevelBeLoaded(StagePath))
            {
                ShowHub("Stage scene is unavailable.");
                return;
            }
            departRequested = true;
        }

        IEnumerator RunSession()
        {
            string message = null;
            Scene menuScene = gameObject.scene;
            while (session.InSession && !leaving)
            {
                if (!Application.CanStreamedLevelBeLoaded(HubPath))
                {
                    message = "Base scene is unavailable.";
                    break;
                }
                menu.Show("Loading Base");
                yield return SceneManager.LoadSceneAsync(HubPath, LoadSceneMode.Additive);
                Scene hubScene = SceneManager.GetSceneByPath(HubPath);
                bool entered = false;
                if (hubScene.isLoaded && session.InSession && !leaving)
                {
                    SceneManager.SetActiveScene(hubScene);
                    var hub = FindFirstObjectByType<SoloHub>();
                    entered = hub && hub.gameObject.scene == hubScene && hub.TryEnter(CharacterName);
                    if (entered)
                    {
                        departRequested = false;
                        ShowHub(message);
                        yield return new WaitUntil(() => departRequested || leaving || !session.InSession);
                    }
                }

                SceneManager.SetActiveScene(menuScene);
                if (hubScene.isLoaded) yield return SceneManager.UnloadSceneAsync(hubScene);
                if (leaving || !session.InSession) break;
                if (!entered)
                {
                    message = "Base could not be started.";
                    break;
                }

                yield return RunStage();
                message = result switch
                {
                    SoloStageResult.Cleared => "Stage cleared.",
                    SoloStageResult.PlayerDefeated => "Defeated. Ready to redeploy.",
                    _ => "Stage could not be started."
                };
            }

            bool requestedExit = leaving;
            if (session.InSession)
            {
                if (!requestedExit) session.Leave();
                yield return new WaitUntil(() => !session.InSession);
            }
            inSessionFlow = false;
            leaving = false;
            departRequested = false;
            selectedStage = null;
            ShowLobby(requestedExit ? null : message ?? "Solo session ended.");
        }

        IEnumerator RunStage()
        {
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

        }

        void RecordResult(SoloStageResult outcome) => result = outcome;
    }
}
