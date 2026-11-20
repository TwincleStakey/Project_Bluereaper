using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlueReaper.UI
{
    [Serializable]
    internal sealed class StageHudLayout
    {
        public Canvas Canvas;
        public RectTransform DialogueRoot;
        public DialogueRow[] DialogueRows;
        public RectTransform PartyRoot;
        public PartySlot[] PartySlots;
        public RectTransform EnemyHealthRoot;
        public EnemyHealthBarView EnemyHealthPrefab;

        [Serializable]
        internal struct DialogueRow
        {
            public RectTransform Root;
            public CanvasGroup Fade;
            public TMP_Text Speaker;
            public TMP_Text Line;
            public Image Portrait;
            internal bool IsReady => HudWidgets.IsSceneView(Root) && Fade && Speaker && Line && Portrait
                && Fade.transform.IsChildOf(Root) && Speaker.transform.IsChildOf(Root)
                && Line.transform.IsChildOf(Root) && Portrait.transform.IsChildOf(Root);
        }

        [Serializable]
        internal struct PartySlot
        {
            public RectTransform Root;
            public Image Accent;
            public Image Portrait;
            public TMP_Text Status;
            internal bool IsReady => HudWidgets.IsSceneView(Root) && Accent && Portrait && Status
                && Accent.transform.IsChildOf(Root) && Portrait.transform.IsChildOf(Root) && Status.transform.IsChildOf(Root);
        }
    }
}
