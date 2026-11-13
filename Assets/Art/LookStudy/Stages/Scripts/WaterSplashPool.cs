// Pooled water splash: plays an authored splash prefab (from a downloaded VFX pack) where a foot or body hits water.
// One shared pool per prefab; a splash reuses the oldest instance, so nothing is allocated per step.
using System.Collections.Generic;
using UnityEngine;

public class WaterSplashPool : MonoBehaviour
{
    [Tooltip("Splash prefab with one or more ParticleSystems.")]
    public GameObject prefab;
    [Range(1, 64)] public int size = 16;

    static readonly Dictionary<GameObject, WaterSplashPool> pools = new Dictionary<GameObject, WaterSplashPool>();
    readonly List<Transform> items = new List<Transform>();
    readonly List<ParticleSystem[]> systems = new List<ParticleSystem[]>();
    Vector3 baseScale = Vector3.one;
    int next;

    public static WaterSplashPool For(GameObject prefab, int size = 16)
    {
        if (!prefab) return null;
        if (pools.TryGetValue(prefab, out var p) && p) return p;
        var go = new GameObject("WaterSplashPool_" + prefab.name);
        p = go.AddComponent<WaterSplashPool>();
        p.prefab = prefab; p.size = size;
        pools[prefab] = p;
        return p;
    }

    void OnDestroy() { if (prefab && pools.TryGetValue(prefab, out var p) && p == this) pools.Remove(prefab); }

    void Fill()
    {
        baseScale = prefab.transform.localScale;
        for (int i = items.Count; i < size; i++)
        {
            var go = Instantiate(prefab, transform);
            go.name = prefab.name + "_" + i;
            var all = go.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in all)
            {
                var m = ps.main; m.playOnAwake = false; m.loop = false; m.stopAction = ParticleSystemStopAction.None;
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            items.Add(go.transform); systems.Add(all);
        }
    }

    /// <summary>Play a splash at a point on the water surface. strength 0..1 scales the size.</summary>
    public void Play(Vector3 at, float strength, float yaw = 0f)
    {
        if (items.Count < size) Fill();
        if (items.Count == 0) return;
        int k = next; next = (next + 1) % items.Count;
        var t = items[k];
        t.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        t.localScale = baseScale * Mathf.Lerp(0.7f, 1.15f, Mathf.Clamp01(strength));
        foreach (var ps in systems[k]) { ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Play(false); }
    }
}
