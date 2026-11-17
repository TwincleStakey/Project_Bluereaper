using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace BlueReaper.Network
{
    /// <summary>
    /// UI 기획 [외근 형태 선택] 팝업의 입구. 단독 외근 / 외근조 만들기 / 외근조 합류.
    /// UI는 이 클래스의 함수만 부르고, 결과는 이벤트로 받는다.
    /// </summary>
    public class SessionService : MonoBehaviour
    {
        public const int MaxPlayers = 3;
        public const int CodeLength = 6;

        public static SessionService Instance { get; private set; }

        /// <summary>단독 외근이면 true. UI가 채팅·코드 라벨·파티 패널을 숨길 때 쓴다.</summary>
        public bool IsSolo { get; private set; }
        public bool InSession => NetworkManager.Singleton && NetworkManager.Singleton.IsListening;
        /// <summary>방장 화면의 [외근 코드 복사] 라벨에 띄울 코드. 솔로면 null.</summary>
        public string JoinCode => session?.Code;
        /// <summary>연결하거나 나가는 중. UI가 버튼을 잠글 때 쓴다.</summary>
        public bool Busy => connecting || leaving;

        public event Action SessionStarted;
        public event Action<string> SessionFailed;
        public event Action SessionEnded;

        ISession session;
        /// <summary>Leave()마다 1씩 오른다. 응답을 기다리던 요청이 돌아왔을 때 값이 바뀌어 있으면 이미 나간 것.</summary>
        int attempt;
        bool connecting, leaving;

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start() => NetworkManager.Singleton.OnClientStopped += OnNetworkStopped;

        void OnDestroy()
        {
            attempt++; // 씬이 바뀌는 중에 도착한 세션은 받자마자 나가게 한다
            if (Instance == this) Instance = null;
            if (NetworkManager.Singleton) NetworkManager.Singleton.OnClientStopped -= OnNetworkStopped;
        }

        public void StartSolo()
        {
            if (Busy || InSession) return;
            IsSolo = true;
            if (NetworkManager.Singleton.StartHost()) SessionStarted?.Invoke();
            else Fail("단독 외근을 시작하지 못했어요.");
        }

        public async void CreateTeam()
        {
            if (Busy || InSession) return;
            connecting = true;
            int myAttempt = attempt;
            try
            {
                await SignInAsync();
                var options = new SessionOptions { MaxPlayers = MaxPlayers }.WithRelayNetwork();
                var created = await MultiplayerService.Instance.CreateSessionAsync(options);
                if (myAttempt != attempt) { await LeaveLateAsync(created); return; }
                session = created;
                IsSolo = false;
                SessionStarted?.Invoke();
            }
            catch (Exception e) { Debug.LogException(e); Fail(ToMessage(e)); }
            finally { connecting = false; }
        }

        public async void JoinTeam(string code)
        {
            if (Busy || InSession) return;
            code = code?.Trim().ToUpperInvariant();
            if (code == null || code.Length != CodeLength) { Fail($"외근 코드는 {CodeLength}자리예요."); return; }
            connecting = true;
            int myAttempt = attempt;
            try
            {
                await SignInAsync();
                var joined = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                if (myAttempt != attempt) { await LeaveLateAsync(joined); return; }
                session = joined;
                IsSolo = false;
                SessionStarted?.Invoke();
            }
            catch (Exception e) { Debug.LogException(e); Fail(ToMessage(e)); }
            finally { connecting = false; }
        }

        public async void Leave()
        {
            attempt++; // 응답을 기다리는 생성·합류 요청이 있으면 무효로 만든다
            if (leaving) return;
            // 다 나갈 때까지 Busy로 잠가서, 그 사이 새로 연결한 세션을 아래 Shutdown이 끊지 않게 한다
            leaving = true;
            try
            {
                if (session != null)
                {
                    var old = session;
                    session = null;
                    try { await old.LeaveAsync(); }
                    catch (Exception e) { Debug.LogWarning(e); }
                }
                if (NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
            }
            finally { leaving = false; }
        }

        void OnNetworkStopped(bool wasHost)
        {
            session = null;
            IsSolo = false;
            SessionEnded?.Invoke();
        }

        /// <summary>나가기를 누른 뒤에 도착한 세션은 받자마자 나간다.</summary>
        static async Task LeaveLateAsync(ISession late)
        {
            try { await late.LeaveAsync(); }
            catch (Exception e) { Debug.LogWarning(e); }
            if (NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
        }

        void Fail(string message)
        {
            IsSolo = false;
            SessionFailed?.Invoke(message);
        }

        static async Task SignInAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                var options = new InitializationOptions();
#if UNITY_EDITOR
                // Multiplayer Play Mode의 플레이어 창들이 서로 다른 사람으로 로그인되게 한다
                options.SetProfile("Editor" + UnityEngine.Random.Range(0, 100000));
#endif
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        static string ToMessage(Exception e) => e is SessionException s
            ? s.Error switch
            {
                SessionError.SessionNotFound => "코드에 맞는 외근조가 없어요. 코드를 다시 확인해 주세요.",
                SessionError.NotAuthorized => "Unity 서비스 로그인에 실패했어요.",
                _ => $"외근조 연결에 실패했어요. ({s.Error})"
            }
            : "외근조 연결에 실패했어요. 인터넷 연결을 확인해 주세요.";
    }
}
