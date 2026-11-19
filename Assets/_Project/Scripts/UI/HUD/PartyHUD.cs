using System.Collections.Generic;
using BlueReaper.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlueReaper.UI
{
    internal sealed class PartyHUD
    {
        private readonly List<PlayerLife> members = new(2);
        private readonly (RectTransform Rect, Image Accent, Image Portrait, TMP_Text Text, bool Generated)[] slots =
            new (RectTransform, Image, Image, TMP_Text, bool)[2];
        private readonly System.Func<int, int, Sprite> findPortrait;
        private PlayerLife local;

        internal PartyHUD(Transform parent, System.Func<int, int, Sprite> findPortrait, StageHudLayout layout)
        {
            this.findPortrait = findPortrait;
            var root = layout?.PartyRoot;
            if (!HudWidgets.IsSceneView(root)) root = null;
            var authored = layout?.PartySlots;
            for (int i = 0; i < slots.Length; i++)
            {
                var binding = authored != null && i < authored.Length ? authored[i] : default;
                if (binding.IsReady)
                {
                    slots[i] = (binding.Root, binding.Accent, binding.Portrait, binding.Status, false);
                    binding.Root.gameObject.SetActive(false);
                    continue;
                }
                if (HudWidgets.IsSceneView(binding.Root)) binding.Root.gameObject.SetActive(false);

                var rect = HudWidgets.Rect(root ? root : parent, "Party Member", Vector2.one, new Vector2(190, 50));
                rect.anchoredPosition = new Vector2(-112, -42 - i * 56);
                HudWidgets.Image(rect, new Color(.07f, .09f, .12f, .8f));
                var accent = HudWidgets.Rect(rect, "Party Color", new Vector2(0, .5f), new Vector2(4, 50));
                var portrait = HudWidgets.Rect(rect, "Portrait", new Vector2(0, .5f), new Vector2(36, 36));
                portrait.anchoredPosition = new Vector2(26, 0);
                var label = HudWidgets.Text(rect, "Status", 19, TextAlignmentOptions.Center);
                slots[i] = (rect, HudWidgets.Image(accent, Color.white), HudWidgets.Image(portrait, Color.white), label, true);
                rect.gameObject.SetActive(false);
            }
        }

        internal void Bind(PlayerLife player)
        {
            local = player;
            members.Clear();
            foreach (var member in Object.FindObjectsByType<PlayerLife>(FindObjectsSortMode.None))
                if (member.IsSpawned) SetMember(member, true);
        }

        internal void SetMember(PlayerLife member, bool spawned)
        {
            if (!local || member == local || member.PartySlot <= 0) return;
            if (!spawned) members.Remove(member);
            else if (!members.Contains(member))
            {
                members.Add(member);
                members.Sort((first, second) => first.PartySlot.CompareTo(second.PartySlot));
            }
        }

        internal void Tick()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var view = slots[i];
                var member = i < members.Count ? members[i] : null;
                view.Rect.gameObject.SetActive(member);
                if (!member) continue;
                Color color = SlotColor(member.PartySlot);
                view.Accent.color = member.State == LifeState.Alive ? color : Color.gray;
                var portrait = findPortrait(member.CharacterId, 0);
                view.Portrait.sprite = portrait;
                view.Portrait.enabled = portrait;
                view.Portrait.color = member.State == LifeState.Alive ? Color.white : Color.gray;
                if (view.Generated) view.Text.rectTransform.offsetMin = new Vector2(portrait ? 45 : 0, 0);
                var name = member.CharacterId > 0 ? $"Warrior {member.CharacterId}" : $"{member.PartySlot}P";
                view.Text.text = member.State switch
                {
                    LifeState.Downed => $"{name}  ·  {Mathf.CeilToInt(member.BleedOutLeft)}s",
                    LifeState.Dead => $"{name}  ·  Dead",
                    _ => name
                };
                view.Text.color = member.State == LifeState.Alive ? Color.white : Color.gray;
            }
        }

        internal static Color SlotColor(int slot) => slot switch
        {
            1 => new Color(.95f, .69f, .35f),
            2 => new Color(.38f, .84f, .84f),
            3 => new Color(.76f, .60f, .92f),
            _ => Color.white
        };
    }
}
