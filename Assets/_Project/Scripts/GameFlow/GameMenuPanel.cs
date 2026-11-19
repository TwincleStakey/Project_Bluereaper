using UnityEngine;

namespace BlueReaper.GameFlow
{
    public sealed class GameMenuPanel : MonoBehaviour
    {
        [SerializeField] private RectTransform content;
        internal RectTransform Content => content;
    }
}
