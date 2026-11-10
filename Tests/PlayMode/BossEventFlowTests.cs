using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlueReaper.Boss;
using BlueReaper.Core;
using BlueReaper.Stage;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Pool = BlueReaper.Core.ObjectPool;

public class BossEventFlowTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly List<UnityEngine.Object> data = new();
    readonly List<string> rewardLogs = new();
    Scene scene;
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);
    static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private).GetValue(owner);
    static void ResetEvents() => typeof(GameEvents).GetMethod("ResetOnPlay", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
    void CaptureLog(string message, string stack, LogType type)
    {
        if (message.StartsWith("Enemy ") && message.Contains("Reward:")) rewardLogs.Add(message);
    }

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        ResetEvents(); rewardLogs.Clear();
        scene = SceneManager.CreateScene("Boss event flow checks");
        SceneManager.SetActiveScene(scene);
        Application.logMessageReceived += CaptureLog;
        typeof(Pool).GetMethod("ResetOnPlay", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        yield return SceneManager.UnloadSceneAsync(scene);
        Application.logMessageReceived -= CaptureLog;
        ResetEvents();
        typeof(Pool).GetMethod("ResetOnPlay", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        foreach (var value in data) if (value) UnityEngine.Object.Destroy(value);
        data.Clear();
    }

    GameObject Actor(string name, bool activate = true, EnemyRank rank = EnemyRank.Boss, uint rewardId = 101)
    {
        var go = new GameObject(name); go.SetActive(false);
        var unit = go.AddComponent<Unit>();
        var stats = ScriptableObject.CreateInstance<UnitData>(); data.Add(stats);
        stats.Attack = 10; stats.MaxHealth = 100; stats.MoveSpeed = 1;
        Set(unit, "data", stats);
        go.AddComponent<Health>();
        var enemy = go.AddComponent<Enemy>(); Set(enemy, "rank", rank);
        var rewards = ScriptableObject.CreateInstance<RewardTable>(); data.Add(rewards);
        rewards.RewardId = new[] { rewardId }; rewards.RewardCount = new uint[] { 1 }; rewards.RewardPermil = new uint[] { 1000 };
        Set(enemy, "rewardTable", rewards);
        go.AddComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
        go.AddComponent<EnemyAI>();
        if (activate) go.SetActive(true);
        return go;
    }

    (StageController stage, BossHUD hud, HPBarUI bar, Image fill) Stage(string name)
    {
        var root = new GameObject(name); root.SetActive(false);
        var hudObject = new GameObject("HUD"); hudObject.transform.SetParent(root.transform);
        var hud = hudObject.AddComponent<BossHUD>();
        var barObject = new GameObject("HP bar", typeof(RectTransform)); barObject.transform.SetParent(hudObject.transform);
        var image = barObject.AddComponent<Image>(); var bar = barObject.AddComponent<HPBarUI>();
        Set(bar, "fillImage", image); Set(hud, "hpBarUI", bar);
        var stage = root.AddComponent<StageController>(); Set(stage, "bossHUD", hud);
        root.SetActive(true);
        return (stage, hud, bar, image);
    }

    [UnityTest]
    public IEnumerator ColdAndPooledActivationNeverRaiseDeathFromInitialZeroHp()
    {
        int localDeaths = 0, bossDeaths = 0;
        GameEvents.BossDefeated += _ => bossDeaths++;
        var actor = Actor("cold boss", false); var health = actor.GetComponent<Health>();
        health.Died += () => localDeaths++;
        Assert.AreEqual(0, health.Current); Assert.AreEqual(0, localDeaths);
        actor.SetActive(true); yield return null;
        Assert.AreEqual(100, health.Current); Assert.IsFalse(health.IsDead);
        Assert.AreEqual(0, localDeaths); Assert.AreEqual(0, bossDeaths); Assert.AreEqual(0, rewardLogs.Count);
        actor.SetActive(false); actor.SetActive(true); yield return null;
        Assert.AreEqual(100, health.Current); Assert.AreEqual(0, localDeaths); Assert.AreEqual(0, bossDeaths);
    }

    [UnityTest]
    public IEnumerator SpawnEventSeesInitializedHealthAndBindsHud()
    {
        var view = Stage("stage"); var prefab = Actor("prefab", false);
        int spawned = 0, defeated = 0; Health eventHealth = null;
        GameEvents.BossSpawned += h => { spawned++; eventHealth = h; Assert.AreEqual(h.MaxHealth, h.Current); Assert.IsFalse(h.IsDead); };
        GameEvents.BossDefeated += _ => defeated++;
        Assert.IsFalse(view.bar.gameObject.activeSelf);
        var boss = view.stage.SpawnEnemy(prefab.GetComponent<Enemy>(), Vector3.zero, Quaternion.identity);
        Assert.AreEqual(1, spawned); Assert.AreSame(boss.GetComponent<Health>(), eventHealth);
        Assert.AreSame(view.stage.AlivePlayers, boss.Players);
        Assert.AreSame(eventHealth, Get<Health>(view.bar, "targetHealth"));
        Assert.IsTrue(view.bar.gameObject.activeSelf); Assert.AreEqual(1, view.fill.fillAmount);
        yield return null;
        Assert.AreEqual(0, defeated); Assert.AreEqual(0, rewardLogs.Count);
    }

    [UnityTest]
    public IEnumerator LethalDamageRaisesOneDeathAndOneRewardThenHidesHud()
    {
        var view = Stage("stage"); var prefab = Actor("prefab", false);
        var boss = view.stage.SpawnEnemy(prefab.GetComponent<Enemy>(), Vector3.zero, Quaternion.identity);
        var health = boss.GetComponent<Health>(); int died = 0, defeated = 0;
        health.Died += () => { died++; Assert.AreEqual(0, health.Current); };
        GameEvents.BossDefeated += h => { defeated++; Assert.AreSame(health, h); Assert.IsTrue(h.IsDead); };
        health.TakeDamage(99); Assert.AreEqual(1, health.Current); Assert.AreEqual(0, died); Assert.AreEqual(0, rewardLogs.Count);
        health.TakeDamage(1);
        Assert.AreEqual(1, died); Assert.AreEqual(1, defeated); Assert.AreEqual(1, rewardLogs.Count);
        Assert.IsFalse(view.bar.gameObject.activeSelf);
        health.TakeDamage(100); GameEvents.RaiseBossDefeated(health);
        Assert.AreEqual(1, died); Assert.AreEqual(1, rewardLogs.Count);
        yield return null;
    }

    [UnityTest]
    public IEnumerator NonlethalInvincibleAndDisabledBossNeverReceiveAReward()
    {
        int defeated = 0; GameEvents.BossDefeated += _ => defeated++;
        var boss = Actor("boss"); var health = boss.GetComponent<Health>();
        health.Invincible = true; health.TakeDamage(1000);
        Assert.AreEqual(100, health.Current); Assert.AreEqual(0, defeated);
        health.Invincible = false; health.TakeDamage(20);
        GameEvents.RaiseBossDefeated(health);
        Assert.AreEqual(0, rewardLogs.Count);
        boss.SetActive(false); yield return null;
        Assert.AreEqual(0, rewardLogs.Count);
        // The only global call above was intentional; normal damage/disable did not publish a defeat.
        Assert.AreEqual(1, defeated);
    }

    [UnityTest]
    public IEnumerator PoolReuseResetsHpAndAllowsExactlyOneRewardForEachLife()
    {
        var view = Stage("stage"); var prefab = Actor("prefab", false);
        int spawned = 0, defeated = 0;
        GameEvents.BossSpawned += _ => spawned++; GameEvents.BossDefeated += _ => defeated++;
        var first = view.stage.SpawnEnemy(prefab.GetComponent<Enemy>(), Vector3.zero, Quaternion.identity);
        first.GetComponent<Health>().TakeDamage(1000); Pool.Despawn(first.gameObject);
        Assert.AreEqual(1, rewardLogs.Count); Assert.AreEqual(1, defeated);
        var second = view.stage.SpawnEnemy(prefab.GetComponent<Enemy>(), Vector3.zero, Quaternion.identity);
        Assert.AreSame(first, second); Assert.AreEqual(100, second.GetComponent<Health>().Current);
        Assert.AreEqual(2, spawned); Assert.AreEqual(1, defeated); Assert.IsTrue(view.bar.gameObject.activeSelf);
        second.GetComponent<Health>().TakeDamage(1000); Pool.Despawn(second.gameObject);
        Assert.AreEqual(2, rewardLogs.Count); Assert.AreEqual(2, defeated);
        yield return null;
    }

    [UnityTest]
    public IEnumerator OnlyTheDefeatedBossRollsRewardsAndAnotherBossHudStaysVisible()
    {
        var view = Stage("stage"); var prefabA = Actor("A", false, rewardId: 101); var prefabB = Actor("B", false, rewardId: 202);
        var a = view.stage.SpawnEnemy(prefabA.GetComponent<Enemy>(), Vector3.zero, Quaternion.identity);
        var b = view.stage.SpawnEnemy(prefabB.GetComponent<Enemy>(), Vector3.right * 20, Quaternion.identity);
        a.GetComponent<Health>().TakeDamage(1000);
        Assert.AreEqual(1, rewardLogs.Count); StringAssert.Contains("item ID 101", rewardLogs[0]);
        Assert.IsFalse(Get<bool>(b.GetComponent<EnemyAI>(), "rewardHandled"));
        Assert.IsTrue(view.bar.gameObject.activeSelf); Assert.AreSame(b.GetComponent<Health>(), Get<Health>(view.bar, "targetHealth"));
        b.GetComponent<Health>().TakeDamage(1000);
        Assert.AreEqual(2, rewardLogs.Count); StringAssert.Contains("item ID 202", rewardLogs[1]);
        Assert.IsFalse(view.bar.gameObject.activeSelf);
        yield return null;
    }

    [UnityTest]
    public IEnumerator HudRebindAndHideShowMaintainOneHealthSubscription()
    {
        var view = Stage("stage"); var prefab = Actor("prefab", false);
        var a = view.stage.SpawnEnemy(prefab.GetComponent<Enemy>(), Vector3.zero, Quaternion.identity).GetComponent<Health>();
        a.TakeDamage(20); Assert.That(view.fill.fillAmount, Is.EqualTo(0.8f).Within(0.001));
        var b = view.stage.SpawnEnemy(prefab.GetComponent<Enemy>(), Vector3.right * 20, Quaternion.identity).GetComponent<Health>();
        a.TakeDamage(10); Assert.AreEqual(1, view.fill.fillAmount);
        view.hud.Hide(); b.TakeDamage(25); Assert.AreEqual(1, view.fill.fillAmount);
        view.hud.Show(); Assert.That(view.fill.fillAmount, Is.EqualTo(0.75f).Within(0.001));
        view.hud.Init(b.GetComponent<Enemy>()); view.hud.Init(b.GetComponent<Enemy>());
        var handlers = (Action<float>)typeof(Health).GetField("Damaged", Private).GetValue(b);
        Assert.AreEqual(1, handlers.GetInvocationList().Count(d => ReferenceEquals(d.Target, view.bar)));
        b.TakeDamage(25); Assert.That(view.fill.fillAmount, Is.EqualTo(0.5f).Within(0.001));
        yield return null;
    }

    [UnityTest]
    public IEnumerator StageOnlyBindsItsOwnSpawnedBoss()
    {
        var a = Stage("stage A"); var b = Stage("stage B"); var prefab = Actor("prefab", false);
        var bossA = a.stage.SpawnEnemy(prefab.GetComponent<Enemy>(), Vector3.zero, Quaternion.identity);
        Assert.IsTrue(a.bar.gameObject.activeSelf); Assert.IsFalse(b.bar.gameObject.activeSelf);
        var bossB = b.stage.SpawnEnemy(prefab.GetComponent<Enemy>(), Vector3.right * 20, Quaternion.identity);
        Assert.AreSame(bossA.GetComponent<Health>(), Get<Health>(a.bar, "targetHealth"));
        Assert.AreSame(bossB.GetComponent<Health>(), Get<Health>(b.bar, "targetHealth"));
        bossA.GetComponent<Health>().TakeDamage(1000);
        Assert.IsFalse(a.bar.gameObject.activeSelf); Assert.IsTrue(b.bar.gameObject.activeSelf);
        yield return null;
    }

    [UnityTest]
    public IEnumerator ReentrantDamagePublishesOnlyOneDeathForTheLethalTransition()
    {
        var actor = Actor("boss"); var health = actor.GetComponent<Health>(); int deaths = 0; bool nested = false;
        health.Died += () => deaths++;
        health.Damaged += _ => { if (!nested) { nested = true; health.TakeDamage(1000); } };
        health.TakeDamage(1);
        Assert.AreEqual(0, health.Current); Assert.AreEqual(1, deaths); Assert.AreEqual(1, rewardLogs.Count);
        yield return null;
    }

    [UnityTest]
    public IEnumerator NormalEnemyDoesNotPublishBossEventsOrShowBossHud()
    {
        var view = Stage("stage"); var prefab = Actor("normal", false, EnemyRank.Normal);
        int spawned = 0, defeated = 0;
        GameEvents.BossSpawned += _ => spawned++; GameEvents.BossDefeated += _ => defeated++;
        var enemy = view.stage.SpawnEnemy(prefab.GetComponent<Enemy>(), Vector3.zero, Quaternion.identity);
        enemy.GetComponent<Health>().TakeDamage(1000);
        Assert.AreEqual(0, spawned); Assert.AreEqual(0, defeated); Assert.AreEqual(0, rewardLogs.Count);
        Assert.IsFalse(view.bar.gameObject.activeSelf);
        yield return null;
    }
}
