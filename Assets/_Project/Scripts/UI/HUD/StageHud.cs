using System.Collections.Generic;
using BlueReaper.Core;
using BlueReaper.Player;
using UnityEngine;

namespace BlueReaper.UI
{
    internal sealed class StageHud : MonoBehaviour
    {
        private readonly Dictionary<int, PlayerLife> roster = new();
        private PlayerLife local;
        private CharacterHudPortrait[] portraits;
        private PartyHUD party;
        private DialogueHUD dialogue;
        private EnemyHudPresenter enemies;
        private bool fieldEnded;

        internal void Init(Canvas canvas, StageHudLayout layout, CharacterHudPortrait[] portraits, TMPro.TMP_FontAsset dialogueFont)
        {
            this.portraits = portraits;
            party = new PartyHUD(canvas.transform, FindPortrait, layout);
            dialogue = new DialogueHUD(canvas.transform, dialogueFont, layout);
            enemies = new EnemyHudPresenter(canvas, layout);
            Enemy.PresenceChanged += enemies.SetEnemy;
            PlayerLife.PartyPresenceChanged += SetMember;
            foreach (var member in Object.FindObjectsByType<PlayerLife>(FindObjectsSortMode.None))
                if (member.IsSpawned) SetMember(member, true);
        }

        internal void Bind(Health player, HPBarUI hpBar)
        {
            if (local) local.EnemyHit -= OnEnemyHit;
            local = player ? player.GetComponent<PlayerLife>() : null;
            if (local)
            {
                local.EnemyHit += OnEnemyHit;
                if (hpBar)
                {
                    if (!hpBar.TryGetComponent(out PlayerHpView view))
                    {
                        view = hpBar.gameObject.AddComponent<PlayerHpView>();
                        view.BuildFallback(hpBar);
                    }
                    view.Bind(local);
                }
            }
            party.Bind(local);
        }

        internal void ShowDialogue(int speaker, int variant, string text)
        {
            if (fieldEnded) return;
            roster.TryGetValue(speaker, out var member);
            var portrait = member ? FindPortrait(member.CharacterId, variant) : null;
            dialogue.Show(speaker, portrait, text);
        }

        private void SetMember(PlayerLife member, bool spawned)
        {
            if (member.PartySlot <= 0) return;
            if (spawned) roster[member.PartySlot] = member;
            else if (roster.TryGetValue(member.PartySlot, out var previous) && previous == member)
                roster.Remove(member.PartySlot);
            party.SetMember(member, spawned);
        }

        private Sprite FindPortrait(int character, int variant)
        {
            if (portraits == null || character <= 0) return null;
            foreach (var entry in portraits)
                if (entry.CharacterId == character) return entry.Resolve(variant);
            return null;
        }

        internal void EndField()
        {
            fieldEnded = true;
            dialogue.Clear();
            enemies.Clear();
        }

        internal void BeginField()
        {
            EndField();
            fieldEnded = false;
        }

        private void OnEnemyHit(Enemy enemy)
        {
            if (!fieldEnded) enemies.MarkHit(enemy);
        }

        private void LateUpdate()
        {
            if (party == null) return;
            party.Tick();
            dialogue.Tick();
            enemies.Tick(Camera.main, !fieldEnded && local);
        }

        private void OnDestroy()
        {
            if (local) local.EnemyHit -= OnEnemyHit;
            if (enemies != null) Enemy.PresenceChanged -= enemies.SetEnemy;
            if (party != null) PlayerLife.PartyPresenceChanged -= SetMember;
        }
    }
}
