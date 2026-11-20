using System;
using System.Text;
using UnityEngine;

namespace BlueReaper.Stage
{
    public enum StageDialogueAudience { Everyone, Individual }

    [Serializable]
    public sealed class StageDialogueLine
    {
        public StageDialogueAudience Audience;
        [Range(0, 3)] public int SpeakerSlot;
        [Range(1, 3)] public int RecipientSlot = 1;
        [Min(0)] public int PortraitVariant;
        [TextArea] public string Text;

        internal bool IsValid => !string.IsNullOrWhiteSpace(Text) && Encoding.UTF8.GetByteCount(Text) <= 500
            && SpeakerSlot is >= 0 and <= 3 && PortraitVariant >= 0
            && (Audience == StageDialogueAudience.Everyone
                || Audience == StageDialogueAudience.Individual && RecipientSlot is >= 1 and <= 3);
    }
}
