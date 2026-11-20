using UnityEngine;
using UnityEngine.UI;

namespace BlueReaper.UI
{
    public sealed class EnemyHealthBarView : MonoBehaviour
    {
        [SerializeField] private Image fill;
        internal bool IsReady => fill && transform is RectTransform;
        internal Image Fill => fill;
    }
}
