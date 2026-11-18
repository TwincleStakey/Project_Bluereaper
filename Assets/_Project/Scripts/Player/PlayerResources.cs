using UnityEngine;

namespace BlueReaper.Player
{
    /// <summary>
    /// 무사의 자원: 마나(RMB 등에 소모, 평타 적중으로 회복)와 궁극기 게이지(적중으로 충전, R로 소모).
    /// 수치는 무사 스킬 기획서 v1.0 기본값. 궁 게이지는 라운드마다 0으로 초기화.
    /// </summary>
    public class PlayerResources : MonoBehaviour
    {
        [SerializeField] float maxMana = 100f;
        [Tooltip("초당 자동 회복량")]
        [SerializeField] float manaRegen = 1.5f;

        public float Mana { get; private set; }
        public float MaxMana => maxMana;
        /// <summary>궁극기 게이지 0~100 (%)</summary>
        public float Ult { get; private set; }
        public bool IsUltReady => Ult >= 100f;

        void OnEnable()
        {
            Mana = maxMana;
            Ult = 0f;
        }

        void Update()
        {
            AddMana(manaRegen * Time.deltaTime);
        }

        public void AddMana(float amount) => Mana = Mathf.Min(maxMana, Mana + amount);

        /// <summary>마나가 충분하면 쓰고 true, 모자라면 안 쓰고 false</summary>
        public bool TrySpendMana(float amount)
        {
            if (Mana < amount) return false;
            Mana -= amount;
            return true;
        }

        public void AddUlt(float percent) => Ult = Mathf.Min(100f, Ult + percent);

        public bool TryUseUlt()
        {
            if (!IsUltReady) return false;
            Ult = 0f;
            return true;
        }

        /// <summary>라운드 시작 시 호출</summary>
        public void ResetUlt() => Ult = 0f;
    }
}
