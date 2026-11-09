using UnityEngine;

namespace BlueReaper.Core
{
    /// <summary>이 오브젝트가 어떤 스탯표(UnitData)를 쓰는지. 체력·이동·공격 컴포넌트가 여기서 스탯을 읽는다.</summary>
    public class Unit : MonoBehaviour
    {
        [SerializeField] UnitData data;

        public UnitData Data => data;
    }
}
