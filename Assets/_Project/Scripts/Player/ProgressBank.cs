using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlueReaper.Player
{
    internal static class ProgressBank
    {
        internal sealed class Entry
        {
            internal int Souls;
            internal int[] Nodes = Array.Empty<int>();
            internal bool Loaded;
        }

        private static readonly Dictionary<ulong, Entry> entries = new();

        internal static Entry Get(ulong clientId)
        {
            if (!entries.TryGetValue(clientId, out Entry entry)) entries[clientId] = entry = new Entry();
            return entry;
        }

        internal static void Clear() => entries.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => entries.Clear();
    }
}
