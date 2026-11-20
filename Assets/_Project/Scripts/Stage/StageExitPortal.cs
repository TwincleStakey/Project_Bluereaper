using System.Collections.Generic;
using BlueReaper.Core;
using UnityEngine;

namespace BlueReaper.Stage
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class StageExitPortal : MonoBehaviour
    {
        private readonly HashSet<Health> occupants = new();
        private readonly HashSet<Health> initialOccupants = new();
        private BoxCollider area;
        private StageController controller;

        internal void Arm(StageController stage)
        {
            area = GetComponent<BoxCollider>();
            Physics.SyncTransforms();
            StageReadyPad.FindPlayers(area, stage.AlivePlayers, initialOccupants);
            controller = stage;
        }

        private void FixedUpdate()
        {
            if (!controller) return;
            StageReadyPad.FindPlayers(area, controller.AlivePlayers, occupants);
            initialOccupants.IntersectWith(occupants);
            foreach (var player in occupants)
            {
                if (initialOccupants.Contains(player)) continue;
                controller.EnterExitPortal(this);
                controller = null;
                return;
            }
        }
    }
}
