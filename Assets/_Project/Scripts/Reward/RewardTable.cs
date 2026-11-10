using System;
using UnityEngine;

[CreateAssetMenu(fileName = "RewardTable", menuName = "BlueReaper/RewardTable")]
public class RewardTable : ScriptableObject
{
    public uint[] RewardId;
    public uint[] RewardCount;
    public uint[] RewardPermil; //0~1000, 1000 = 100%

    internal (uint id, uint count) GetReward()
    {
        if (RewardId == null || RewardCount == null || RewardPermil == null
            || RewardId.Length == 0 || RewardId.Length != RewardCount.Length
            || RewardId.Length != RewardPermil.Length)
            throw new InvalidOperationException("Reward arrays must be non-empty and have matching lengths.");

        ulong total = 0;
        foreach (uint permil in RewardPermil) total += permil;
        if (total != 1000)
            throw new InvalidOperationException("RewardPermil must total exactly 1000.");

        uint roll = (uint)UnityEngine.Random.Range(0, 1000);
        uint end = 0;
        for (int i = 0; i < RewardPermil.Length; i++)
        {
            end += RewardPermil[i];
            if (roll < end) return (RewardId[i], RewardCount[i]);
        }

        throw new InvalidOperationException("No reward interval matched the roll.");
    }
}
