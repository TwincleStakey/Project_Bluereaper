using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;

public static class RewardTableChecks
{
    public static void RunBatch()
    {
        var table = ScriptableObject.CreateInstance<RewardTable>();
        var previous = Random.state;
        var cases = new List<string>();
        var states = new Dictionary<int, Random.State>();
        try
        {
            table.RewardId = new uint[] { 11, 22, 33 };
            table.RewardCount = new uint[] { 1, 2, 3 };
            table.RewardPermil = new uint[] { 500, 300, 200 };
            for (int seed = 0; seed < 30000 && states.Count < 1000; seed++)
            {
                Random.InitState(seed);
                var state = Random.state;
                int roll = Random.Range(0, 1000);
                states[roll] = state;
                Random.state = state;
                var actual = table.GetReward();
                var expected = roll < 500 ? (11u, 1u) : roll < 800 ? (22u, 2u) : (33u, 3u);
                if (actual != expected) throw new Exception("Wrong reward at roll " + roll);
            }
            if (states.Count != 1000) throw new Exception("Incomplete roll coverage");
            cases.Add("all 1000 roll values select exactly one expected reward and count");

            table.RewardId = new uint[] { 1, 2, 3, 4, 5, 6 };
            table.RewardCount = new uint[] { 1, 2, 3, 4, 5, 6 };
            table.RewardPermil = new uint[] { 0, 200, 0, 300, 500, 0 };
            foreach (int roll in new[] { 0, 199, 200, 499, 500, 999 })
            {
                Random.state = states[roll];
                uint expected = roll < 200 ? 2u : roll < 500 ? 4u : 5u;
                if (table.GetReward() != (expected, expected)) throw new Exception("Boundary or zero-width interval failed");
            }
            cases.Add("interval boundaries and leading, middle, trailing zero widths");

            table.RewardId = new uint[] { 7 }; table.RewardCount = new uint[] { 12 };
            table.RewardPermil = new uint[] { 1000 };
            foreach (var state in states.Values)
            {
                Random.state = state;
                if (table.GetReward() != (7u, 12u)) throw new Exception("100 percent reward failed");
            }
            cases.Add("1000 permil always returns the single configured reward");

            void Reject(string name, uint[] ids, uint[] counts, uint[] widths)
            {
                table.RewardId = ids; table.RewardCount = counts; table.RewardPermil = widths;
                try { table.GetReward(); }
                catch (InvalidOperationException) { cases.Add(name); return; }
                throw new Exception("Invalid table accepted: " + name);
            }
            Reject("null array rejected", new uint[] { 1 }, new uint[] { 1 }, null);
            Reject("empty arrays rejected", Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<uint>());
            Reject("mismatched arrays rejected", new uint[] { 1, 2 }, new uint[] { 1 }, new uint[] { 500, 500 });
            Reject("999 total rejected", new uint[] { 1 }, new uint[] { 1 }, new uint[] { 999 });
            Reject("1001 total rejected", new uint[] { 1 }, new uint[] { 1 }, new uint[] { 1001 });
            Reject("large unsigned weights cannot overflow the total", new uint[] { 1, 2 }, new uint[] { 1, 1 }, new uint[] { uint.MaxValue, 1001 });
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/RewardTableChecks.txt", "PASS: " + cases.Count + " checks; 1000/1000 roll values\n" + string.Join("\n", cases));
            Debug.Log("REWARD_TABLE_CHECKS: " + cases.Count + " passed; 1000/1000 roll values");
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally { Random.state = previous; UnityEngine.Object.DestroyImmediate(table); }
    }
}
