using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlueReaper.Boss;
using BlueReaper.BulletPatterns;
using BlueReaper.Core;
using BlueReaper.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Pool = BlueReaper.Core.ObjectPool;

public class BossPatternExecutionTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly List<UnityEngine.Object> data = new();
    Scene scene;
    NavMeshData navMesh;
    NavMeshDataInstance navMeshInstance;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        scene = SceneManager.CreateScene("Boss pattern checks");
        SceneManager.SetActiveScene(scene);
        typeof(Pool).GetMethod("ResetOnPlay", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (var ai in UnityEngine.Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) ai.Release();
        if (navMeshInstance.valid) navMeshInstance.Remove();
        if (navMesh) UnityEngine.Object.Destroy(navMesh);
        yield return SceneManager.UnloadSceneAsync(scene);
        foreach (var value in data) if (value) UnityEngine.Object.Destroy(value);
        data.Clear();
        typeof(Pool).GetMethod("ResetOnPlay", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
    }

    static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);
    static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, Private).GetValue(owner);
    static void Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Private).Invoke(owner, args);
    EnemyPattern Wrap(object attack)
    {
        if (attack is EnemyPattern existing) return existing;
        var pattern = ScriptableObject.CreateInstance<EnemyPattern>();
        data.Add(pattern);
        pattern.ChasingDistance = 0f;
        pattern.MeleeAttack = attack as MeleeAttackPattern;
        pattern.BulletAttack = attack as BulletPattern;
        return pattern;
    }

    IEnumerator Execute(EnemyAI ai, object attack)
    {
        var pattern = Wrap(attack);
        return (IEnumerator)typeof(EnemyAI).GetMethod("ExecutePattern", Private).Invoke(ai, new object[] { pattern });
    }
    static HashSet<ProjectileHitBox> Bullets(EnemyAI ai) => Get<HashSet<ProjectileHitBox>>(ai, "projectiles");
    static string State(EnemyAI ai) => Get<object>(ai, "currentPatternState").ToString();

    GameObject Actor(string name, Vector3 position, bool boss, RuntimeAnimatorController controller = null)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        go.transform.position = position;
        var unit = go.AddComponent<Unit>();
        var stats = ScriptableObject.CreateInstance<UnitData>();
        stats.Attack = 10; stats.MaxHealth = 100; stats.MoveSpeed = 2;
        data.Add(stats); Set(unit, "data", stats);
        go.AddComponent<Health>();
        for (int i = 0; i < 2; i++)
        {
            var part = new GameObject("part " + i);
            part.transform.SetParent(go.transform, false);
            part.transform.localPosition = boss ? Vector3.forward : Vector3.zero;
            part.AddComponent<BoxCollider>().size = Vector3.one * 0.5f;
            if (boss) part.AddComponent<MeleeHitBox>();
        }
        if (boss)
        {
            go.AddComponent<Enemy>();
            go.AddComponent<NavMeshAgent>().enabled = false;
            if (controller) go.AddComponent<Animator>().runtimeAnimatorController = controller;
            go.AddComponent<EnemyAI>();
        }
        go.SetActive(true);
        return go;
    }

    BulletPattern Pattern(int repeats = 1, int count = 1, float interval = 0.1f, float travel = 1f)
    {
        var prefab = new GameObject("bullet template");
        prefab.SetActive(false);
        prefab.AddComponent<SphereCollider>().radius = 0.1f;
        prefab.AddComponent<ProjectileHitBox>();
        var pattern = ScriptableObject.CreateInstance<BulletPattern>();
        data.Add(pattern);
        pattern.BulletPrefab = prefab; pattern.Layout = BulletLayout.Manual;
        pattern.Resize(count); pattern.RepeatCount = repeats; pattern.RepeatInterval = interval;
        pattern.TravelDuration = travel;
        for (int i = 0; i < count; i++) pattern.Paths[i] = new BulletPath { Start = Vector3.right * i, End = Vector3.forward * 5 + Vector3.right * i };
        return pattern;
    }

    MeleeAttackPattern Melee(bool repeat = false)
    {
        var pattern = ScriptableObject.CreateInstance<MeleeAttackPattern>();
        data.Add(pattern); pattern.HitStartSeconds = 0.08f; pattern.HitDurationSeconds = 0.2f;
        pattern.RepeatHits = repeat; pattern.HitRepeatIntervalSeconds = 0.1f;
        return pattern;
    }

    [UnityTest]
    public IEnumerator SingleVolleyCompletesImmediatelyWhileBulletMovesAndExpires()
    {
        var boss = Actor("boss", new Vector3(5, 0, 5), true);
        var ai = boss.GetComponent<EnemyAI>();
        var pattern = Pattern(travel: 0.4f);
        Assert.IsFalse(Execute(ai, pattern).MoveNext());
        Assert.AreEqual("Done", State(ai)); Assert.AreEqual(1, Bullets(ai).Count);
        var bullet = Bullets(ai).Single(); var start = bullet.transform.position;
        yield return new WaitForSeconds(0.12f);
        Assert.Greater(bullet.transform.position.z, start.z + 0.1f);
        yield return new WaitForSeconds(0.4f);
        Assert.IsFalse(bullet.gameObject.activeSelf); Assert.AreEqual(0, Bullets(ai).Count);
    }

    [UnityTest]
    public IEnumerator RepeatedVolleysKeepFirstOriginAndFacingAfterTargetLoss()
    {
        var boss = Actor("boss", new Vector3(5, 0, 5), true);
        boss.transform.rotation = Quaternion.Euler(0, 90, 0);
        var ai = boss.GetComponent<EnemyAI>();
        var target = Actor("target", new Vector3(50, 0, 50), false);
        var targets = new List<Transform> { target.transform };
        boss.GetComponent<Enemy>().Init(targets);
        ai.StartCoroutine(Execute(ai, Pattern(3, 2, 0.08f)));
        Assert.AreEqual("Executing", State(ai)); Assert.AreEqual(2, Bullets(ai).Count);
        targets.Clear(); target.SetActive(false);
        boss.transform.SetPositionAndRotation(new Vector3(100, 0, 100), Quaternion.identity);
        yield return new WaitForSeconds(0.22f);
        Assert.AreEqual("Done", State(ai)); Assert.AreEqual(6, Bullets(ai).Count);
        foreach (var bullet in Bullets(ai))
        {
            Assert.Less(bullet.transform.position.x, 20);
            Assert.Less(bullet.transform.position.z, 10);
            Assert.Greater(bullet.transform.forward.x, 0.99f);
        }
    }

    [UnityTest]
    public IEnumerator ZeroIntervalSpawnsEveryScheduledBulletInOneCall()
    {
        var ai = Actor("boss", Vector3.zero, true).GetComponent<EnemyAI>();
        Assert.IsFalse(Execute(ai, Pattern(3, 2, 0)).MoveNext());
        Assert.AreEqual(6, Bullets(ai).Count); Assert.AreEqual("Done", State(ai));
        yield return null;
    }

    [UnityTest]
    public IEnumerator DeathCancelsPendingVolleysAndReturnsExistingBulletsImmediately()
    {
        var boss = Actor("boss", Vector3.zero, true);
        var ai = boss.GetComponent<EnemyAI>();
        ai.StartCoroutine(Execute(ai, Pattern(4, 2, 0.1f)));
        var first = Bullets(ai).ToArray(); Assert.AreEqual(2, first.Length);
        boss.GetComponent<Health>().TakeDamage(1000);
        Assert.AreEqual(0, Bullets(ai).Count);
        foreach (var bullet in first) Assert.IsFalse(bullet.gameObject.activeSelf);
        yield return new WaitForSeconds(0.4f);
        Assert.AreEqual(0, Bullets(ai).Count);
        Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType<ProjectileHitBox>(FindObjectsSortMode.None).Length);
    }

    [UnityTest]
    public IEnumerator BossDeathDoesNotReturnAnotherBossesBullets()
    {
        var a = Actor("boss A", Vector3.zero, true);
        var b = Actor("boss B", Vector3.right * 30, true);
        var aiA = a.GetComponent<EnemyAI>(); var aiB = b.GetComponent<EnemyAI>();
        Execute(aiA, Pattern()).MoveNext(); Execute(aiB, Pattern()).MoveNext();
        a.GetComponent<Health>().TakeDamage(1000);
        Assert.AreEqual(0, Bullets(aiA).Count); Assert.AreEqual(1, Bullets(aiB).Count);
        Assert.IsTrue(Bullets(aiB).Single().gameObject.activeSelf);
        yield return null;
    }

    [UnityTest]
    public IEnumerator ProjectileHitsParentReceiverOnceAndResetsWhenReused()
    {
        var owner = Actor("boss", Vector3.zero, true).GetComponent<Unit>();
        var target = Actor("target", Vector3.right * 20, false);
        var health = target.GetComponent<Health>(); var collider = target.GetComponentInChildren<Collider>();
        var pattern = Pattern();
        var bullet = Pool.Spawn(pattern.BulletPrefab, Vector3.zero, Quaternion.identity).GetComponent<ProjectileHitBox>();
        Call(bullet, "TryHit", collider); Assert.AreEqual(100, health.Current);
        bullet.Init(owner, 2f);
        Call(bullet, "TryHit", collider); Call(bullet, "TryHit", collider); bullet.ReturnToPool();
        Assert.AreEqual(80, health.Current);
        var reused = Pool.Spawn(pattern.BulletPrefab, Vector3.zero, Quaternion.identity).GetComponent<ProjectileHitBox>();
        Assert.AreSame(bullet, reused);
        Call(reused, "TryHit", collider); Assert.AreEqual(80, health.Current);
        reused.Init(owner, 0.5f); Call(reused, "TryHit", collider);
        Assert.AreEqual(75, health.Current);
        yield return null;
    }

    [UnityTest]
    public IEnumerator ProjectilePhysicsContactAppliesOnlyOneHitAcrossTwoColliders()
    {
        var boss = Actor("boss", Vector3.zero, true); var ai = boss.GetComponent<EnemyAI>();
        var target = Actor("target", Vector3.forward, false);
        Execute(ai, Pattern(travel: 0.5f)).MoveNext();
        yield return new WaitForSeconds(0.3f);
        Assert.AreEqual(90, target.GetComponent<Health>().Current);
        Assert.AreEqual(0, Bullets(ai).Count);
    }

    [UnityTest]
    public IEnumerator MeleeWindowSharesOneHitBudgetAcrossAllHitboxes()
    {
        var boss = Actor("boss", Vector3.zero, true); var ai = boss.GetComponent<EnemyAI>();
        var target = Actor("target", Vector3.forward, false); var health = target.GetComponent<Health>();
        var pattern = Melee(); pattern.DamageMultiplier = 2;
        ai.StartCoroutine(Execute(ai, pattern));
        Assert.IsTrue(boss.GetComponentsInChildren<MeleeHitBox>().All(h => !h.GetComponent<Collider>().enabled));
        yield return new WaitForSeconds(0.04f); Assert.AreEqual(100, health.Current);
        yield return new WaitForSeconds(0.13f); Assert.AreEqual(80, health.Current);
        yield return new WaitForSeconds(0.17f); Assert.AreEqual(80, health.Current);
        Assert.AreEqual("Done", State(ai));
        Assert.IsTrue(boss.GetComponentsInChildren<MeleeHitBox>().All(h => !h.GetComponent<Collider>().enabled));
    }

    [UnityTest]
    public IEnumerator MeleeStayRepeatsPerTargetAndReleaseDisarmsEverything()
    {
        var boss = Actor("boss", Vector3.zero, true); var ai = boss.GetComponent<EnemyAI>();
        var target = Actor("target", Vector3.forward, false); var health = target.GetComponent<Health>();
        var pattern = Melee(true); pattern.HitStartSeconds = 0; pattern.HitDurationSeconds = 0.5f;
        ai.StartCoroutine(Execute(ai, pattern));
        yield return new WaitForSeconds(0.27f);
        Assert.That(health.Current, Is.InRange(70f, 80f));
        ai.Release(); float remaining = health.Current;
        yield return new WaitForSeconds(0.15f); Assert.AreEqual(remaining, health.Current);
        Assert.IsTrue(boss.GetComponentsInChildren<MeleeHitBox>().All(h => !h.GetComponent<Collider>().enabled));
    }

    [UnityTest]
    public IEnumerator RetainedBulletOutlivesNextPatternAndDeathStillReturnsIt()
    {
        var boss = Actor("boss", Vector3.zero, true); var ai = boss.GetComponent<EnemyAI>();
        var pattern = Pattern(travel: 0.08f); pattern.DespawnAtEnd = false;
        Execute(ai, pattern).MoveNext(); var bullet = Bullets(ai).Single();
        ai.StartCoroutine(Execute(ai, Melee()));
        yield return new WaitForSeconds(0.35f);
        Assert.IsTrue(bullet.gameObject.activeSelf); Assert.That(bullet.transform.position.z, Is.EqualTo(5).Within(0.01));
        boss.GetComponent<Health>().TakeDamage(1000);
        Assert.IsFalse(bullet.gameObject.activeSelf);
    }

    [UnityTest]
    public IEnumerator AnimationStateCanTransitionFromAttackToMoveAndIdle()
    {
        var controller = Resources.Load<RuntimeAnimatorController>("Boss_1");
        Assert.IsNotNull(controller);
        var boss = Actor("boss", Vector3.zero, true, controller); var ai = boss.GetComponent<EnemyAI>();
        var pattern = Wrap(Pattern()); pattern.MotionClip = Resources.Load<AnimationClip>("Boss_1_Melee1");
        Execute(ai, pattern).MoveNext(); yield return null;
        var animator = boss.GetComponent<Animator>();
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Boss_1_Melee1"));
        Call(ai, "PlayLocomotion", true); yield return null;
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Move"));
        Set(ai, "motionUntil", 0f); Call(ai, "PlayLocomotion", false); yield return null;
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"));
    }

    [UnityTest]
    public IEnumerator AuthoredBossPrefabSpawnsPooledPatternAndDeathReclaimsIt()
    {
        var settings = NavMesh.GetSettingsByIndex(0);
        var source = new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(30, 0.2f, 30), transform = Matrix4x4.TRS(Vector3.down * 0.1f, Quaternion.identity, Vector3.one) };
        navMesh = NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> { source }, new Bounds(Vector3.zero, new Vector3(32, 4, 32)), Vector3.zero, Quaternion.identity);
        navMeshInstance = NavMesh.AddNavMeshData(navMesh);
        var prefab = Resources.Load<GameObject>("Boss_1");
        Assert.IsNotNull(prefab);
        var boss = UnityEngine.Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        var ai = boss.GetComponent<EnemyAI>();
        var target = Actor("target", Vector3.right * 2.5f, false);
        boss.GetComponent<Enemy>().Init(new List<Transform> { target.transform });
        float until = Time.time + 8;
        while (Bullets(ai).Count == 0 && Time.time < until) yield return null;
        Assert.Greater(Bullets(ai).Count, 0, "Authored prefab did not reach its pooled bullet pattern");
        var bullets = Bullets(ai).ToArray();
        boss.GetComponent<Health>().TakeDamage(10000);
        Assert.AreEqual(0, Bullets(ai).Count);
        foreach (var bullet in bullets) Assert.IsFalse(bullet.gameObject.activeSelf);
        Assert.IsTrue(boss.GetComponentsInChildren<MeleeHitBox>().All(h => !h.GetComponent<Collider>().enabled));
    }

    [UnityTest]
    public IEnumerator AutonomousAiReachesTargetAndAlternatesPatternsOnNavMesh()
    {
        var settings = NavMesh.GetSettingsByIndex(0);
        var source = new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(20, 0.2f, 20), transform = Matrix4x4.TRS(Vector3.down * 0.1f, Quaternion.identity, Vector3.one) };
        navMesh = NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> { source }, new Bounds(Vector3.zero, new Vector3(22, 4, 22)), Vector3.zero, Quaternion.identity);
        Assert.IsNotNull(navMesh); navMeshInstance = NavMesh.AddNavMeshData(navMesh);
        var boss = Actor("boss", Vector3.zero, true); var ai = boss.GetComponent<EnemyAI>();
        boss.GetComponent<NavMeshAgent>().enabled = true;
        var target = Actor("target", Vector3.right * 2.5f, false);
        boss.GetComponent<Enemy>().Init(new List<Transform> { target.transform });
        var melee = Melee(); melee.HitStartSeconds = 0; melee.HitDurationSeconds = 0.05f;
        Set(ai, "patterns", new[] { Wrap(melee), Wrap(Pattern(2, 1, 0.05f)) });
        float until = Time.time + 2;
        while (Bullets(ai).Count < 2 && Time.time < until) yield return null;
        Assert.GreaterOrEqual(Bullets(ai).Count, 2, "AI failed to arrive and execute its bullet pattern");
        Assert.GreaterOrEqual(Get<int>(ai, "currentPatternIndex"), 0);
        boss.SetActive(false); Assert.AreEqual(0, Bullets(ai).Count);
        boss.GetComponent<Enemy>().Init(new List<Transform>());
        boss.SetActive(true); yield return null;
        Assert.IsNotNull(Get<Coroutine>(ai, "coAIUpdate"), "Pool reactivation did not restart AI");
    }
    [UnityTest]
    public IEnumerator MeleeAndDelayedBulletRunConcurrentlyAndWindowCleansUpNaturally()
    {
        var ai = Actor("boss", Vector3.zero, true).GetComponent<EnemyAI>();
        var melee = Melee(); melee.HitStartSeconds = 0.02f; melee.HitDurationSeconds = 0.35f;
        var bullet = Pattern(2, 1, 0.08f); bullet.StartTime = 0.12f;
        var pattern = Wrap(melee); pattern.BulletAttack = bullet;
        ai.StartCoroutine(Execute(ai, pattern));
        yield return new WaitForSeconds(0.07f);
        Assert.AreEqual(0, Bullets(ai).Count);
        Assert.IsNotNull(Get<Coroutine>(ai, "coMeleeHitWindow"));
        Assert.IsTrue(ai.GetComponentsInChildren<MeleeHitBox>().All(h => h.GetComponent<Collider>().enabled));
        yield return new WaitForSeconds(0.09f);
        Assert.AreEqual(1, Bullets(ai).Count);
        Assert.IsTrue(ai.GetComponentsInChildren<MeleeHitBox>().All(h => h.GetComponent<Collider>().enabled));
        yield return new WaitForSeconds(0.09f);
        Assert.AreEqual("Done", State(ai)); Assert.AreEqual(2, Bullets(ai).Count);
        Assert.IsNotNull(Get<Coroutine>(ai, "coMeleeHitWindow"));
        yield return new WaitForSeconds(0.2f);
        Assert.IsNull(Get<Coroutine>(ai, "coMeleeHitWindow"));
        Assert.IsNull(Get<MeleeAttackPattern>(ai, "activeMelee"));
        Assert.AreEqual(0, Get<Dictionary<IDamageable, float>>(ai, "nextMeleeHits").Count);
        Assert.IsTrue(ai.GetComponentsInChildren<MeleeHitBox>().All(h => !h.GetComponent<Collider>().enabled));
    }

    [UnityTest]
    public IEnumerator NewPatternWithoutMeleeCancelsPreviousWindowImmediately()
    {
        var ai = Actor("boss", Vector3.zero, true).GetComponent<EnemyAI>();
        var melee = Melee(); melee.HitStartSeconds = 0; melee.HitDurationSeconds = 1;
        var old = Wrap(melee); old.BulletAttack = Pattern();
        Assert.IsFalse(Execute(ai, old).MoveNext());
        Assert.IsNotNull(Get<Coroutine>(ai, "coMeleeHitWindow"));
        Assert.IsTrue(ai.GetComponentsInChildren<MeleeHitBox>().All(h => h.GetComponent<Collider>().enabled));
        Assert.IsFalse(Execute(ai, Wrap(Pattern())).MoveNext());
        Assert.IsNull(Get<Coroutine>(ai, "coMeleeHitWindow"));
        Assert.IsTrue(ai.GetComponentsInChildren<MeleeHitBox>().All(h => !h.GetComponent<Collider>().enabled));
        yield return new WaitForSeconds(0.1f);
        Assert.IsNull(Get<MeleeAttackPattern>(ai, "activeMelee"));
    }

    [UnityTest]
    public IEnumerator ReplacingWindowDoesNotLetOldCompletionClearTheNewWindow()
    {
        var ai = Actor("boss", Vector3.zero, true).GetComponent<EnemyAI>();
        var first = Melee(); first.HitStartSeconds = 0; first.HitDurationSeconds = 0.08f;
        var old = Wrap(first); old.BulletAttack = Pattern(); Execute(ai, old).MoveNext();
        var next = Melee(); next.HitStartSeconds = 0; next.HitDurationSeconds = 0.4f;
        var pattern = Wrap(next); pattern.BulletAttack = Pattern(); Execute(ai, pattern).MoveNext();
        yield return new WaitForSeconds(0.15f);
        Assert.AreSame(next, Get<MeleeAttackPattern>(ai, "activeMelee"));
        Assert.IsNotNull(Get<Coroutine>(ai, "coMeleeHitWindow"));
        Assert.IsTrue(ai.GetComponentsInChildren<MeleeHitBox>().All(h => h.GetComponent<Collider>().enabled));
    }

    [UnityTest]
    public IEnumerator DeathDuringBulletDelayCancelsMeleeAndPreventsAnyEmission()
    {
        var boss = Actor("boss", Vector3.zero, true); var ai = boss.GetComponent<EnemyAI>();
        var melee = Melee(); melee.HitStartSeconds = 0; melee.HitDurationSeconds = 1;
        var bullet = Pattern(3, 1, 0.05f); bullet.StartTime = 0.2f;
        var pattern = Wrap(melee); pattern.BulletAttack = bullet;
        ai.StartCoroutine(Execute(ai, pattern)); yield return new WaitForSeconds(0.04f);
        boss.GetComponent<Health>().TakeDamage(1000);
        Assert.IsNull(Get<Coroutine>(ai, "coMeleeHitWindow"));
        Assert.IsTrue(ai.GetComponentsInChildren<MeleeHitBox>().All(h => !h.GetComponent<Collider>().enabled));
        yield return new WaitForSeconds(0.35f); Assert.AreEqual(0, Bullets(ai).Count);
    }

    [UnityTest]
    public IEnumerator DisableCancelsPendingMeleeAndReenableDoesNotResumeIt()
    {
        var boss = Actor("boss", Vector3.zero, true); var ai = boss.GetComponent<EnemyAI>();
        var melee = Melee(); melee.HitStartSeconds = 0.15f; melee.HitDurationSeconds = 0.3f;
        ai.StartCoroutine(Execute(ai, melee)); yield return new WaitForSeconds(0.03f);
        boss.SetActive(false);
        Assert.IsNull(Get<Coroutine>(ai, "coMeleeHitWindow"));
        Assert.IsNull(Get<MeleeAttackPattern>(ai, "activeMelee"));
        Assert.IsTrue(boss.GetComponentsInChildren<MeleeHitBox>(true).All(h => !h.GetComponent<Collider>().enabled));
        boss.SetActive(true); yield return new WaitForSeconds(0.25f);
        Assert.IsNull(Get<Coroutine>(ai, "coMeleeHitWindow"));
        Assert.IsTrue(boss.GetComponentsInChildren<MeleeHitBox>().All(h => !h.GetComponent<Collider>().enabled));
    }

    [UnityTest]
    public IEnumerator ZeroLengthMeleeDoesNotLeaveAStaleCoroutineHandle()
    {
        var ai = Actor("boss", Vector3.zero, true).GetComponent<EnemyAI>();
        var melee = Melee(); melee.HitDurationSeconds = 0;
        Assert.IsFalse(Execute(ai, melee).MoveNext());
        Assert.IsNull(Get<Coroutine>(ai, "coMeleeHitWindow"));
        Assert.IsTrue(ai.GetComponentsInChildren<MeleeHitBox>().All(h => !h.GetComponent<Collider>().enabled));
        yield return null;
    }

    [UnityTest]
    public IEnumerator BulletDelayIsNotAppliedTwiceToProjectileLifetime()
    {
        Assert.IsNull(typeof(EnemyPattern).GetField("StartTime"));
        var ai = Actor("boss", Vector3.zero, true).GetComponent<EnemyAI>();
        var bullet = Pattern(travel: 0.1f); bullet.StartTime = 0.15f;
        ai.StartCoroutine(Execute(ai, bullet));
        yield return new WaitForSeconds(0.07f); Assert.AreEqual(0, Bullets(ai).Count);
        float until = Time.time + 0.3f;
        while (Bullets(ai).Count == 0 && Time.time < until) yield return null;
        Assert.AreEqual(1, Bullets(ai).Count);
        var instance = Bullets(ai).Single(); Assert.AreEqual("Done", State(ai));
        yield return new WaitForSeconds(0.14f);
        Assert.IsFalse(instance.gameObject.activeSelf); Assert.AreEqual(0, Bullets(ai).Count);
    }

}
