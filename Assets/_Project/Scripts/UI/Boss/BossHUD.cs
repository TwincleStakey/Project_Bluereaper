using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.UI
{
    //TODO : ingamehud에서 bosshud를 참조, 해당 클래스는 boss타입의 몬스터가 스폰될 때를 기준으로 show/hide를 결정
    //show/hide는 모든 하위 hud를 show/hide만 수행
    //필요하면 개별 요소마다 show/hide를 수행 할 수 있어야 함
    public class BossHUD : MonoBehaviour
    {
        [SerializeField]
        private HPBarUI hpBarUI = null;
        [SerializeField]
        private PostureBarUI postureBarUI = null;

        public void Init(Enemy enemy)
        {
            if(enemy.Rank != EnemyRank.Boss)
            {
                Debug.LogWarning($"BossHUD.Init() called with non-boss enemy: {enemy.name}");
                return;
            }

            if (hpBarUI)
            {
                var health = enemy.GetComponent<Health>();
                if (health)
                {
                    hpBarUI.Init(health);
                }
            }

            if(postureBarUI)
            {
                var posture = enemy.GetComponent<Posture>();
                if(posture)
                {
                    postureBarUI.Init(posture);
                }
            }
        }

        public void Show()
        {
            if (hpBarUI) hpBarUI.gameObject.SetActive(true);
            if (postureBarUI) postureBarUI.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (hpBarUI) hpBarUI.gameObject.SetActive(false);
            if (postureBarUI) postureBarUI.gameObject.SetActive(false);
        }
    }
}
