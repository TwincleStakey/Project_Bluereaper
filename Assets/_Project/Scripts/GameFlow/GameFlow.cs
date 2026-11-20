using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BlueReaper.Core;
using BlueReaper.Network;
using BlueReaper.Player;
using BlueReaper.Stage;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlueReaper.GameFlow
{
    [RequireComponent(typeof(GameMenu))]
    public sealed class GameFlow : MonoBehaviour
    {
        private const string HubScene = "Hub";
        private const string StageScene = "Stage";
        private const string StateMessage = "BlueReaper.GameFlow.State";
        private const string RequestMessage = "BlueReaper.GameFlow.Request";

        private enum Phase { Lobby, LoadingHub, Hub, LoadingStage, Stage, Returning }
        private enum Request { Sync, Ready }

        [SerializeField] private SessionService session;
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private StageData[] stages;

        private readonly HashSet<ulong> ready = new();
        private readonly Dictionary<ulong, int> remoteCharacters = new();
        private GameMenu menu;
        private Phase phase;
        private int characterCount, character;
        private int selectedStage = -1, fieldIndex = -1;
        private int remoteCount, remoteCapacity, remoteReady, readyRevision;
        private bool remoteLocalReady, leaving, departRequested, sceneComplete, sceneTimedOut;
        private string joinCode = "", expectedScene;
        private StageRunResult? result;
        private Health localPlayer;
        private StageEntry stageEntry;
        private (StageFieldPhase Phase, int Current, int Goal) remoteProgress;
        private NetworkSceneManager sceneManager;
        private CustomMessagingManager messages;
        private Coroutine run;

        private int Capacity => session.IsSolo ? 1 : session.TeamSize;
        private string CharacterName => $"Warrior {character}";
        private bool CanDepart => phase == Phase.Hub && networkManager.IsServer && selectedStage >= 0
            && networkManager.ConnectedClientsIds.Count == Capacity
            && (session.IsSolo || ready.Count == Capacity);

        private void Awake()
        {
            menu = GetComponent<GameMenu>();
            networkManager.ConnectionApprovalCallback = Approve;
            networkManager.OnServerStarted += OnServerStarted;
            networkManager.OnClientConnectedCallback += OnConnected;
            networkManager.OnClientDisconnectCallback += OnDisconnected;
            session.SessionStarted += OnSessionStarted;
            session.SessionFailed += ShowLobby;
            session.SessionEnded += OnSessionEnded;
        }

        private void Start() => ShowTitle();

        private void OnDestroy()
        {
            NetworkWorld.Enabled = false;
            if (sceneManager != null) sceneManager.OnSceneEvent -= OnSceneEvent;
            session.SessionStarted -= OnSessionStarted;
            session.SessionFailed -= ShowLobby;
            session.SessionEnded -= OnSessionEnded;
            if (!networkManager) return;
            networkManager.ConnectionApprovalCallback -= Approve;
            networkManager.OnServerStarted -= OnServerStarted;
            networkManager.OnClientConnectedCallback -= OnConnected;
            networkManager.OnClientDisconnectCallback -= OnDisconnected;
        }

        private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            bool host = request.ClientNetworkId == NetworkManager.ServerClientId;
            response.Approved = host || (!session.IsSolo && phase == Phase.Hub && !departRequested
                && networkManager.ConnectedClientsIds.Count < Capacity);
            response.CreatePlayerObject = false;
            if (!response.Approved) response.Reason = "The team is full or has already departed.";
        }

        private void OnServerStarted()
        {
            networkManager.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Additive);
        }

        private void ShowTitle()
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

        private void ShowSettings()
        {
            menu.Show("Settings");
            menu.Volume();
            menu.Button(Screen.fullScreen ? "Switch to Windowed" : "Switch to Fullscreen", () =>
            {
                Screen.fullScreen = !Screen.fullScreen;
                StartCoroutine(RefreshSettings());
            });
            menu.Button("Back", ShowTitle);
        }

        private IEnumerator RefreshSettings()
        {
            yield return null;
            ShowSettings();
        }

        private void ShowLobby(string message = null)
        {
            menu.Show("Character Lobby", message);
            menu.Button("Create Warrior", () => { character = ++characterCount; ShowLobby(); });
            menu.Label(character == 0 ? "No Character Selected" : $"Selected: {CharacterName}");
            if (characterCount > 1)
                menu.Button("Next Character", () => { character = character % characterCount + 1; ShowLobby(); });
            bool canStart = character > 0 && !leaving;
            menu.Button("Start Solo", canStart ? session.StartSolo : null);
            if (canStart)
            {
                menu.Button("Create Team (2)", () => Connect(() => session.CreateTeam(2)));
                menu.Button("Create Team (3)", () => Connect(() => session.CreateTeam(3)));
                menu.Input("Join code", joinCode, value => joinCode = value, SessionService.CodeLength);
                menu.Button("Join Team", () => Connect(() => session.JoinTeam(joinCode)));
            }
            menu.Button("Back to Title", leaving ? null : ShowTitle);
        }

        private void Connect(System.Action action)
        {
            menu.Show("Connecting");
            menu.Button("Cancel", () => { session.Leave(); ShowLobby(); });
            action();
        }

        private void OnSessionStarted()
        {
            messages = networkManager.CustomMessagingManager;
            sceneManager = networkManager.SceneManager;
            messages.RegisterNamedMessageHandler(StateMessage, OnState);
            messages.RegisterNamedMessageHandler(RequestMessage, OnRequest);
            messages.RegisterNamedMessageHandler(StageDialogueService.Message, OnDialogue);
            sceneManager.OnSceneEvent += OnSceneEvent;
            NetworkWorld.Enabled = !session.IsSolo;
            if (networkManager.IsServer) run = StartCoroutine(RunHost());
            else if (networkManager.IsConnectedClient) SendRequest(Request.Sync);
        }

        private void OnConnected(ulong clientId)
        {
            if (messages == null) return;
            if (!networkManager.IsServer)
            {
                if (clientId == networkManager.LocalClientId) SendRequest(Request.Sync);
                return;
            }
            if (phase != Phase.Hub || clientId == NetworkManager.ServerClientId) return;

            var hub = FindFirstObjectByType<HubEntry>();
            if (hub && !networkManager.ConnectedClients[clientId].PlayerObject)
                SpawnPlayer(clientId, hub.PlayerPrefab, hub.SpawnPoint, networkManager.ConnectedClientsIds.Count - 1);
            ResetReady();
            PublishState();
        }

        private void OnDisconnected(ulong clientId)
        {
            if (!networkManager.IsServer || clientId == NetworkManager.ServerClientId || messages == null) return;
            remoteCharacters.Remove(clientId);
            ResetReady();
            if (phase == Phase.Stage && stageEntry) stageEntry.Controller.AbortRun();
            if (phase == Phase.Hub) departRequested = false;
            PublishState();
        }

        private void OnSessionEnded()
        {
            if (run != null) StopCoroutine(run);
            run = null;
            if (sceneManager != null) sceneManager.OnSceneEvent -= OnSceneEvent;
            sceneManager = null;
            messages?.UnregisterNamedMessageHandler(StageDialogueService.Message);
            messages = null;
            remoteCharacters.Clear();
            NetworkWorld.Enabled = false;
            StartCoroutine(ReturnToLobby());
        }

        private IEnumerator ReturnToLobby()
        {
            phase = Phase.Lobby;
            var menuScene = gameObject.scene;
            if (menuScene.isLoaded) SceneManager.SetActiveScene(menuScene);
            foreach (string name in new[] { HubScene, StageScene })
            {
                Scene scene = SceneManager.GetSceneByName(name);
                if (scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            }
            localPlayer = null;
            stageEntry = null;
            selectedStage = fieldIndex = -1;
            result = null;
            ResetReady();
            departRequested = false;
            bool requested = leaving;
            leaving = false;
            ShowLobby(requested ? null : "Session ended.");
        }

        private IEnumerator RunHost()
        {
            while (session.InSession && !leaving)
            {
                phase = Phase.LoadingHub;
                PublishState();
                yield return ChangeScene(HubScene, true);
                if (!session.InSession || !sceneComplete || sceneTimedOut) break;
                var hub = FindFirstObjectByType<HubEntry>();
                if (!hub || !hub.PlayerPrefab || !hub.SpawnPoint) break;
                SpawnParty(hub.PlayerPrefab, hub.SpawnPoint);
                ResetReady();
                departRequested = false;
                phase = Phase.Hub;
                PublishState();
                yield return new WaitUntil(() => departRequested || !session.InSession || leaving);
                if (!session.InSession || leaving) break;
                if (!CanDepart) continue;

                phase = Phase.LoadingStage;
                PublishState();
                DespawnParty();
                yield return ChangeScene(HubScene, false);
                if (!session.InSession || !sceneComplete || sceneTimedOut) break;
                yield return RunStage();
                if (!sceneComplete || sceneTimedOut) break;
            }

            if (session.InSession && !leaving) session.Leave();
        }

        private IEnumerator RunStage()
        {
            result = null;
            fieldIndex = -1;
            yield return ChangeScene(StageScene, true);
            if (!session.InSession || !sceneComplete || sceneTimedOut) yield break;

            stageEntry = FindFirstObjectByType<StageEntry>();
            if (stageEntry && stageEntry.Controller && stageEntry.PlayerPrefab
                && networkManager.ConnectedClientsIds.Count == Capacity)
            {
                var party = SpawnParty(stageEntry.PlayerPrefab, stageEntry.transform);
                stageEntry.Controller.RunFinished += RecordResult;
                stageEntry.Controller.FieldChanged += RecordField;
                stageEntry.Controller.DialogueRequested += PublishDialogue;
                stageEntry.Controller.FieldProgressChanged += PublishState;
                if (stageEntry.Controller.TryStartRun(stages[selectedStage], party))
                {
                    phase = Phase.Stage;
                    PublishState();
                    yield return new WaitUntil(() => result.HasValue || !session.InSession || leaving);
                }
                if (stageEntry)
                {
                    stageEntry.Controller.RunFinished -= RecordResult;
                    stageEntry.Controller.FieldChanged -= RecordField;
                    stageEntry.Controller.DialogueRequested -= PublishDialogue;
                    stageEntry.Controller.FieldProgressChanged -= PublishState;
                    stageEntry.Controller.enabled = false;
                }
            }
            if (!session.InSession) yield break;
            result ??= StageRunResult.Aborted;
            phase = Phase.Returning;
            PublishState();
            DespawnParty();
            yield return ChangeScene(StageScene, false);
            fieldIndex = -1;
            stageEntry = null;
        }

        private void RecordResult(StageRunResult outcome)
        {
            result = outcome;
            if (stageEntry) stageEntry.EndFieldHud();
        }

        private void RecordField(int index)
        {
            fieldIndex = index;
            stageEntry.BeginFieldHud();
            PublishState();
        }

        private void PublishDialogue(StageDialogueLine line)
        {
            if (phase != Phase.Stage || !stageEntry) return;
            StageDialogueService.Publish(networkManager, line, fieldIndex, stageEntry.ShowDialogue);
        }

        private void OnDialogue(ulong sender, FastBufferReader reader)
        {
            if (networkManager.IsServer || sender != NetworkManager.ServerClientId || phase != Phase.Stage || !stageEntry) return;
            if (StageDialogueService.TryRead(reader, fieldIndex, out int speaker, out int portraitVariant, out string dialogue))
                stageEntry.ShowDialogue(speaker, portraitVariant, dialogue);
        }

        private IEnumerator ChangeScene(string name, bool load)
        {
            sceneComplete = sceneTimedOut = false;
            expectedScene = name;
            if (!load) SceneManager.SetActiveScene(gameObject.scene);
            if (sceneManager == null) yield break;
            var progress = load
                ? sceneManager.LoadScene(name, LoadSceneMode.Additive)
                : sceneManager.UnloadScene(SceneManager.GetSceneByName(name));
            if (progress != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"Could not {(load ? "load" : "unload")} {name}: {progress}", this);
                yield break;
            }
            yield return new WaitUntil(() => sceneComplete || !session.InSession);
            expectedScene = null;
            if (load && sceneComplete)
            {
                Scene scene = SceneManager.GetSceneByName(name);
                if (scene.isLoaded) SceneManager.SetActiveScene(scene);
            }
        }

        private void OnSceneEvent(SceneEvent sceneEvent)
        {
            if (sceneEvent.SceneEventType == SceneEventType.LoadComplete
                && sceneEvent.ClientId == networkManager.LocalClientId
                && (sceneEvent.SceneName == HubScene || sceneEvent.SceneName == StageScene))
            {
                Scene scene = SceneManager.GetSceneByName(sceneEvent.SceneName);
                if (scene.isLoaded) SceneManager.SetActiveScene(scene);
            }
            if (!networkManager.IsServer || sceneEvent.SceneName != expectedScene) return;
            if (sceneEvent.SceneEventType is SceneEventType.LoadEventCompleted or SceneEventType.UnloadEventCompleted)
            {
                sceneTimedOut = sceneEvent.ClientsThatTimedOut?.Count > 0;
                sceneComplete = true;
            }
        }

        private List<Health> SpawnParty(Health prefab, Transform spawn)
        {
            var party = new List<Health>();
            foreach (ulong client in networkManager.ConnectedClientsIds.ToArray())
                party.Add(SpawnPlayer(client, prefab, spawn, party.Count));
            return party;
        }

        private Health SpawnPlayer(ulong client, Health prefab, Transform spawn, int slot)
        {
            Health player = Instantiate(prefab, spawn.position + spawn.right * (slot * 2f), spawn.rotation);
            if (client == networkManager.LocalClientId) player.name = CharacterName;
            if (player.TryGetComponent(out PlayerLife life))
                life.SetPartyIdentity(slot + 1, client == networkManager.LocalClientId ? character : remoteCharacters.GetValueOrDefault(client));
            player.GetComponent<NetworkObject>().SpawnAsPlayerObject(client, destroyWithScene: true);
            return player;
        }

        private void DespawnParty()
        {
            foreach (var client in networkManager.ConnectedClientsList)
                if (client.PlayerObject && client.PlayerObject.IsSpawned) client.PlayerObject.Despawn();
            localPlayer = null;
        }

        private void Update() => BindLocalPlayer();

        private void BindLocalPlayer()
        {
            if (!session.InSession || phase is not (Phase.Hub or Phase.Stage)) return;
            var playerObject = networkManager.LocalClient?.PlayerObject;
            if (!playerObject) return;

            if (phase == Phase.Hub)
            {
                if (localPlayer && localPlayer.gameObject == playerObject.gameObject) return;
                var hub = FindFirstObjectByType<HubEntry>();
                if (!hub) return;
                if (SceneManager.GetActiveScene() != hub.gameObject.scene)
                    SceneManager.SetActiveScene(hub.gameObject.scene);
                localPlayer = playerObject.GetComponent<Health>();
                if (!localPlayer) return;
                localPlayer.name = CharacterName;
                hub.Bind(localPlayer);
                return;
            }

            if (!stageEntry) stageEntry = FindFirstObjectByType<StageEntry>();
            if (!stageEntry) return;
            RefreshRemoteField();
            if (localPlayer && localPlayer.gameObject == playerObject.gameObject) return;
            localPlayer = playerObject.GetComponent<Health>();
            if (!localPlayer) return;
            localPlayer.name = CharacterName;
            stageEntry.Bind(localPlayer);
        }

        private void RefreshRemoteField()
        {
            if (networkManager.IsServer || phase != Phase.Stage || selectedStage < 0
                || selectedStage >= stages.Length || fieldIndex < 0) return;
            if (!stageEntry) stageEntry = FindFirstObjectByType<StageEntry>();
            if (stageEntry) stageEntry.ShowRemoteField(stages[selectedStage], fieldIndex);
        }

        private void ShowCurrentState()
        {
            if (phase == Phase.Lobby) return;
            if (phase == Phase.Stage)
            {
                menu.Hide(keepInput: true);
                if (stageEntry) stageEntry.ShowProgress(networkManager.IsServer ? stageEntry.Controller.FieldProgress : remoteProgress,
                    networkManager.IsServer);
                return;
            }
            if (phase != Phase.Hub)
            {
                menu.Show(phase == Phase.LoadingHub ? "Loading Base"
                    : phase == Phase.LoadingStage ? "Loading Stage" : "Returning to Base");
                return;
            }
            string message = result switch
            {
                StageRunResult.Cleared => "Stage cleared.",
                StageRunResult.PartyDefeated => "Defeated. Ready to redeploy.",
                StageRunResult.Aborted => "Stage interrupted. Ready to redeploy.",
                _ => null
            };
            menu.Show("Base", message, inWorld: true);
            menu.Label(CharacterName);
            if (!session.IsSolo)
            {
                int count = networkManager.IsServer ? networkManager.ConnectedClientsIds.Count : remoteCount;
                int capacity = networkManager.IsServer ? Capacity : remoteCapacity;
                int readyCount = networkManager.IsServer ? ready.Count : remoteReady;
                menu.Label($"Team {count}/{capacity}   Ready {readyCount}/{capacity}");
                if (!string.IsNullOrWhiteSpace(session.JoinCode)) menu.Label($"Code: {session.JoinCode}");
            }
            for (int i = 0; i < stages.Length; i++)
            {
                if (!stages[i] || (!networkManager.IsServer && i != selectedStage)) continue;
                int index = i;
                string name = stages[i].name.Replace('_', ' ');
                menu.Button(i == selectedStage ? $"Selected: {name}" : $"Select {name}",
                    networkManager.IsServer ? () => SelectStage(index) : null);
            }
            if (!session.IsSolo)
            {
                bool localReady = networkManager.IsServer ? ready.Contains(NetworkManager.ServerClientId) : remoteLocalReady;
                menu.Button(localReady ? "Cancel Ready" : "Ready", selectedStage >= 0 ? ToggleReady : null);
            }
            if (networkManager.IsServer)
                menu.Button("Depart", CanDepart ? () => departRequested = true : null);
            menu.Button("Back to Lobby", () => { leaving = true; session.Leave(); });
        }

        private void SelectStage(int index)
        {
            if (selectedStage != index)
            {
                selectedStage = index;
                ResetReady();
            }
            PublishState();
        }

        private void ResetReady()
        {
            ready.Clear();
            readyRevision++;
        }

        private void ToggleReady()
        {
            if (networkManager.IsServer) ApplyReady(NetworkManager.ServerClientId);
            else SendRequest(Request.Ready);
        }

        private void ApplyReady(ulong clientId)
        {
            if (session.IsSolo || phase != Phase.Hub || departRequested || selectedStage < 0
                || !networkManager.ConnectedClients.TryGetValue(clientId, out var client) || !client.PlayerObject) return;
            if (!ready.Add(clientId)) ready.Remove(clientId);
            PublishState();
        }

        private void PublishState()
        {
            if (!networkManager.IsServer || messages == null) return;
            foreach (ulong client in networkManager.ConnectedClientsIds)
                if (client != NetworkManager.ServerClientId) SendState(client);
            BindLocalPlayer();
            ShowCurrentState();
        }

        private void SendState(ulong clientId)
        {
            using var writer = new FastBufferWriter(48, Allocator.Temp);
            writer.WriteValueSafe((int)phase);
            writer.WriteValueSafe(selectedStage);
            writer.WriteValueSafe(fieldIndex);
            writer.WriteValueSafe(networkManager.ConnectedClientsIds.Count);
            writer.WriteValueSafe(Capacity);
            writer.WriteValueSafe(ready.Count);
            writer.WriteValueSafe(readyRevision);
            writer.WriteValueSafe(ready.Contains(clientId));
            writer.WriteValueSafe(result.HasValue ? (int)result.Value : -1);
            var progress = stageEntry ? stageEntry.Controller.FieldProgress : default;
            writer.WriteValueSafe((int)progress.Phase);
            writer.WriteValueSafe(progress.Current);
            writer.WriteValueSafe(progress.Goal);
            messages.SendNamedMessage(StateMessage, clientId, writer);
        }

        private void OnState(ulong sender, FastBufferReader reader)
        {
            if (networkManager.IsServer || sender != NetworkManager.ServerClientId || !reader.TryBeginRead(45)) return;
            reader.ReadValueSafe(out int phaseValue);
            reader.ReadValueSafe(out selectedStage);
            reader.ReadValueSafe(out fieldIndex);
            reader.ReadValueSafe(out remoteCount);
            reader.ReadValueSafe(out remoteCapacity);
            reader.ReadValueSafe(out remoteReady);
            reader.ReadValueSafe(out readyRevision);
            reader.ReadValueSafe(out remoteLocalReady);
            reader.ReadValueSafe(out int resultValue);
            reader.ReadValueSafe(out int fieldPhase);
            reader.ReadValueSafe(out int points);
            reader.ReadValueSafe(out int goal);
            remoteProgress = ((StageFieldPhase)fieldPhase, points, goal);
            phase = (Phase)phaseValue;
            result = resultValue < 0 ? null : (StageRunResult)resultValue;
            if (phase != Phase.Stage && stageEntry) stageEntry.EndFieldHud();
            RefreshRemoteField();
            BindLocalPlayer();
            ShowCurrentState();
        }

        private void SendRequest(Request request)
        {
            using var writer = new FastBufferWriter(9, Allocator.Temp);
            writer.WriteValueSafe((byte)request);
            writer.WriteValueSafe(readyRevision);
            writer.WriteValueSafe(character);
            messages.SendNamedMessage(RequestMessage, NetworkManager.ServerClientId, writer);
        }

        private void OnRequest(ulong sender, FastBufferReader reader)
        {
            if (!networkManager.IsServer || !networkManager.ConnectedClients.ContainsKey(sender)
                || !reader.TryBeginRead(9)) return;
            reader.ReadValueSafe(out byte request);
            reader.ReadValueSafe(out int revision);
            reader.ReadValueSafe(out int selectedCharacter);
            if (phase == Phase.Hub && !departRequested && selectedCharacter > 0
                && (Request)request is Request.Ready or Request.Sync)
                remoteCharacters[sender] = selectedCharacter;

            if ((Request)request == Request.Ready && revision == readyRevision) ApplyReady(sender);
            else if ((Request)request == Request.Sync) SendState(sender);
        }
    }
}
