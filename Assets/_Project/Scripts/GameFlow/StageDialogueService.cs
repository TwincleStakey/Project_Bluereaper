using System;
using BlueReaper.Player;
using BlueReaper.Stage;
using Unity.Collections;
using Unity.Netcode;

namespace BlueReaper.GameFlow
{
    internal static class StageDialogueService
    {
        internal const string Message = "BlueReaper.Stage.Dialogue";

        internal static void Publish(NetworkManager manager, StageDialogueLine line, int fieldIndex, Action<int, int, string> showLocal)
        {
            if (!manager.IsServer || line == null || !line.IsValid || fieldIndex < 0) return;

            using var writer = new FastBufferWriter(528, Allocator.Temp);
            writer.WriteValueSafe(fieldIndex);
            writer.WriteValueSafe(line.SpeakerSlot);
            writer.WriteValueSafe(line.PortraitVariant);
            writer.WriteValueSafe(new FixedString512Bytes(line.Text));

            foreach (var client in manager.ConnectedClientsList)
            {
                if (!client.PlayerObject || !client.PlayerObject.TryGetComponent(out PlayerLife member)) continue;
                if (line.Audience == StageDialogueAudience.Individual && member.PartySlot != line.RecipientSlot) continue;
                if (client.ClientId == manager.LocalClientId) showLocal(line.SpeakerSlot, line.PortraitVariant, line.Text);
                else manager.CustomMessagingManager.SendNamedMessage(Message, client.ClientId, writer);
            }
        }

        internal static bool TryRead(FastBufferReader reader, int expectedField, out int speakerSlot, out int portraitVariant, out string text)
        {
            speakerSlot = portraitVariant = 0;
            text = null;
            if (!reader.TryBeginRead(12)) return false;
            reader.ReadValueSafe(out int fieldIndex);
            reader.ReadValueSafe(out speakerSlot);
            reader.ReadValueSafe(out portraitVariant);
            if (fieldIndex != expectedField || speakerSlot is < 0 or > 3 || portraitVariant < 0) return false;
            reader.ReadValueSafe(out FixedString512Bytes body);
            text = body.ToString();
            return !string.IsNullOrWhiteSpace(text);
        }
    }
}
