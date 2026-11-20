using System.Collections.Generic;
using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.Stage
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class StageReadyPad : MonoBehaviour
    {
        private BoxCollider area;
        private readonly HashSet<Health> occupants = new();

        private void Awake() => area = GetComponent<BoxCollider>();

        internal Health GetSingleOccupant(IReadOnlyList<Transform> alivePlayers)
        {
            FindPlayers(area, alivePlayers, occupants);
            if (occupants.Count != 1) return null;
            foreach (Health occupant in occupants) return occupant;
            return null;
        }

        internal static void FindPlayers(BoxCollider box, IReadOnlyList<Transform> alivePlayers, HashSet<Health> result)
        {
            result.Clear();
            var anchor = box.transform;
            Vector3 scale = anchor.lossyScale;
            Vector3 half = Vector3.Scale(box.size * 0.5f,
                new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            foreach (var contact in Physics.OverlapBox(anchor.TransformPoint(box.center), half, anchor.rotation,
                LayerMask.GetMask("Player", "Hurtbox"), QueryTriggerInteraction.Collide))
            {
                var health = contact.GetComponentInParent<Health>();
                if (!health || health.IsDead) continue;
                foreach (var player in alivePlayers)
                    if (player == health.transform) result.Add(health);
            }
        }
    }
}
