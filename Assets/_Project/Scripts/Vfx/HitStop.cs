using UnityEngine;

namespace BlueReaper.Vfx
{
    /// <summary>
    /// 히트스톱: 맞는 순간 게임 시간을 아주 잠깐 거의 멈춰 "손맛"을 준다.
    /// 싱글 테스트용으로 Time.timeScale을 쓴다. 온라인(협동)이 되면 화면마다 멈춤이 어긋나므로
    /// 그때는 "맞은 캐릭터의 자세만 멈추기" 방식으로 바꿔야 한다.
    /// </summary>
    public class HitStop : MonoBehaviour
    {
        [Tooltip("멈춰 있는 실제 시간(초)")]
        [SerializeField] float duration = 0.06f;
        [Tooltip("멈춘 동안의 게임 속도 (0 = 완전 정지, 0.05 = 거의 정지)")]
        [SerializeField, Range(0f, 1f)] float slowScale = 0.05f;

        float timeLeft;

        public void Play()
        {
            timeLeft = duration;
            Time.timeScale = slowScale;
        }

        void Update()
        {
            if (timeLeft <= 0f) return;
            // 게임 시간은 멈춰 있으니 실제 시간으로 잰다
            timeLeft -= Time.unscaledDeltaTime;
            if (timeLeft <= 0f) Time.timeScale = 1f;
        }

        void OnDisable()
        {
            // 멈춘 채로 꺼지면 게임이 영원히 느린 채로 남지 않게
            if (timeLeft > 0f) Time.timeScale = 1f;
            timeLeft = 0f;
        }
    }
}
