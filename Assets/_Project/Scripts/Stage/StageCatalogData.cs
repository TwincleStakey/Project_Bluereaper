using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlueReaper.Stage
{
    [CreateAssetMenu(fileName = "StageCatalog", menuName = "BlueReaper/Stage Catalog")]
    public sealed class StageCatalogData : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public StageData Stage;
            public bool OpenByDefault;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public IReadOnlyList<Entry> Entries => entries ?? Array.Empty<Entry>();

        public bool TryResolve(int stageId, out StageData stage)
        {
            bool found = TryFindEntry(stageId, out Entry entry);
            stage = found ? entry.Stage : null;
            return found;
        }

        public bool IsOpen(int stageId) => TryFindEntry(stageId, out Entry entry) && entry.OpenByDefault;

        public string GetValidationError()
        {
            if (Entries.Count == 0) return "Stage catalog must contain at least one stage.";

            var ids = new HashSet<int>();
            foreach (Entry entry in Entries)
            {
                if (!entry.Stage) return "Stage catalog contains an empty stage reference.";
                int id = entry.Stage.StageId;
                if (id <= 0) return $"{entry.Stage.name} needs a positive stage ID.";
                if (!ids.Add(id)) return $"Stage catalog contains duplicate stage ID {id}.";
                if (!entry.Stage.HasFields) return $"{entry.Stage.name} needs a complete field list.";
            }
            return null;
        }

        private bool TryFindEntry(int stageId, out Entry result)
        {
            result = default;
            if (stageId <= 0) return false;

            foreach (Entry entry in Entries)
            {
                if (!entry.Stage || entry.Stage.StageId != stageId) continue;
                if (result.Stage) return false;
                result = entry;
            }
            return result.Stage && result.Stage.HasFields;
        }
    }
}
