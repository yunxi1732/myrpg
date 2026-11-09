using System;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

public static class NetworkSimulationTimeVerification
{
    private static readonly object owner = new object();
    private static readonly object otherOwner = new object();
    private static Stopwatch timer;
    private static GameObject bodyObject, lifetimeObject;
    private static float originalScale, pausedGameTime, simulationStart;
    private static int stage, callbacks;

    [MenuItem("Tools/RPG/Verify Network Simulation Time (Play Mode)")]
    public static void Verify()
    {
        if (!Application.isPlaying || GameManager.instance == null || GameSimulationClock.IsNetwork || timer != null)
            throw new InvalidOperationException("Run once in single-player MainScene Play mode, with no active network room.");
        originalScale = Time.timeScale;
        try
        {
            stage = callbacks = 0;
            GameManager.instance.SetPauseOwner(owner, true);
            pausedGameTime = GameSimulationClock.Time;
            Require(Time.timeScale == 0, "Single player retains pause");
            timer = Stopwatch.StartNew();
            EditorApplication.update += Check;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        }
        catch { Cleanup(); throw; }
    }

    private static void Check()
    {
        try
        {
            Require(timer.Elapsed.TotalSeconds < 10, "Verification timed out");
            if (stage == 0)
            {
                if (timer.Elapsed.TotalSeconds < 0.3) return;
                Require(Mathf.Abs(GameSimulationClock.Time - pausedGameTime) < 0.01f, "Single-player simulation time freezes");
                GameSimulationClock.Acquire(owner, true);
                GameplayControlLock.Acquire(owner);
                GameManager.instance.SetPauseOwner(owner, true);
                Require(Time.timeScale == 1 && GameplayControlLock.IsLocked, "Network menu locks input, not world time");
                GameManager.instance.SetPauseOwner(otherOwner, true);
                GameManager.instance.SetPauseOwner(owner, false);
                Require(GameplayControlLock.IsLocked && Time.timeScale == 1, "Closing one UI cannot release another UI's lock");
                GameManager.instance.SetPauseOwner(owner, true);
                GameManager.instance.SetPauseOwner(otherOwner, false);
                Require(GameplayControlLock.IsLocked, "Owners release only their own lock");
                bodyObject = new GameObject("SimulationTimePhysicsProbe");
                var body = bodyObject.AddComponent<Rigidbody2D>();
                body.gravityScale = 0;
                body.velocity = Vector2.right;
                lifetimeObject = new GameObject("SimulationLifetimeProbe");
                GameSimulationClock.DestroyAfter(lifetimeObject, 0.2f);
                var driver = UnityEngine.Object.FindObjectOfType<GameSimulationDriver>();
                GameSimulationClock.Schedule(driver, () => callbacks++, "UnusedNetworkOnlyCallback", 0.2f);
                simulationStart = GameSimulationClock.Time;
                timer.Restart();
                stage = 1;
            }
            else
            {
                if (timer.Elapsed.TotalSeconds < 1) return;
                Require(Time.timeScale == 1 && GameSimulationClock.AuthorityTick > 0 &&
                    GameSimulationClock.Time - simulationStart >= 0.9f, "Non-scaled authority time/ticks advance");
                Require(callbacks == 1 && lifetimeObject == null, "Delayed action and lifetime use simulation time");
                float elapsed = (float)timer.Elapsed.TotalSeconds;
                Require(bodyObject.transform.position.x > 0.1f && bodyObject.transform.position.x < elapsed + 0.2f,
                    "Automatic 2D physics continues without duplicate stepping");
                Cleanup();
                UnityEngine.Debug.Log("[NetworkSimulationTime] PASS: single pause retained; network input lock, clock/tick, delayed action/lifetime and automatic physics continue. Gameplay timing adapters still need normal combat regression.");
            }
        }
        catch (Exception error) { Cleanup(); UnityEngine.Debug.LogException(error); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[NetworkSimulationTime] FAIL: " + message);
    }

    private static void OnPlayModeChanged(PlayModeStateChange value)
    {
        if (value == PlayModeStateChange.ExitingPlayMode) Cleanup();
    }

    private static void Cleanup()
    {
        EditorApplication.update -= Check;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
        if (GameManager.instance != null) GameManager.instance.SetPauseOwner(owner, false);
        if (GameManager.instance != null) GameManager.instance.SetPauseOwner(otherOwner, false);
        GameplayControlLock.Release(otherOwner);
        GameplayControlLock.Release(owner);
        GameSimulationClock.Release(owner);
        if (!GameSimulationClock.IsNetwork) Time.timeScale = originalScale;
        if (bodyObject != null) UnityEngine.Object.Destroy(bodyObject);
        if (lifetimeObject != null) UnityEngine.Object.Destroy(lifetimeObject);
        bodyObject = lifetimeObject = null;
        timer = null;
    }
}
