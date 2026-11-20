using System;
using BlueReaper.Core;

namespace BlueReaper.Stage
{
    public enum HuntPhase { None, Afternoon, Evening, Night }

    public enum WaveSpawnType { Scattered, Ring, Clockwise, Pincer, Arrowhead }

    [Serializable]
    public sealed class SpawnData
    {
        [Serializable]
        public struct EnemyCount
        {
            public Enemy Prefab;
            public int Count;
        }

        public WaveSpawnType Type;
        public float StartTime;
        public float StepInterval = 3f;
        public EnemyCount[] Enemies;
    }
}
