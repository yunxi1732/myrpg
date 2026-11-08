using System;
using System.Collections;
using System.Collections.Generic;
using Rpg.Networking;
using UnityEngine;

/// <summary>Shared gameplay time. Single player retains Unity scaled time and its existing pause behavior.</summary>
public static class GameSimulationClock
{
    private static readonly HashSet<object> sessions = new HashSet<object>();
    private static readonly HashSet<object> authorities = new HashSet<object>();
    private static GameSimulationDriver driver;
    private static double epoch;
    private static float epochGameTime;
    private static SimulationMode2D previousPhysicsMode;
    public static bool IsNetwork => sessions.Count > 0;
    public static bool IsAuthority => authorities.Count > 0;
    public static ulong AuthorityTick { get; private set; }
    public static float DeltaTime => IsNetwork ?
        (UnityEngine.Time.inFixedTimeStep ? FixedDeltaTime : UnityEngine.Time.unscaledDeltaTime) : UnityEngine.Time.deltaTime;
    public static float FixedDeltaTime => IsNetwork ? UnityEngine.Time.fixedUnscaledDeltaTime : UnityEngine.Time.fixedDeltaTime;
    public static float Time => IsNetwork ? epochGameTime + (float)(MonotonicClock.Now - epoch) : UnityEngine.Time.time;

    public static void Acquire(object owner, bool authority)
    {
        if (!Application.isPlaying || sessions.Contains(owner)) return;
        if (sessions.Count == 0)
        {
            epoch = MonotonicClock.Now; epochGameTime = UnityEngine.Time.time;
            previousPhysicsMode = Physics2D.simulationMode;
        }
        sessions.Add(owner);
        if (authority && authorities.Add(owner)) AuthorityTick = 0;
        if (driver == null)
        {
            var root = new GameObject("NetworkSimulationClock");
            UnityEngine.Object.DontDestroyOnLoad(root);
            driver = root.AddComponent<GameSimulationDriver>();
        }
        EnforceNetworkTime();
    }

    public static void Release(object owner)
    {
        authorities.Remove(owner);
        if (!sessions.Remove(owner) || IsNetwork) return;
        Physics2D.simulationMode = previousPhysicsMode;
        if (driver != null) UnityEngine.Object.Destroy(driver.gameObject);
        driver = null;
        if (GameManager.instance != null) GameManager.instance.RefreshPauseState();
        else UnityEngine.Time.timeScale = 1;
    }

    internal static void EnforceNetworkTime()
    {
        if (!IsNetwork) return;
        UnityEngine.Time.timeScale = 1;
        // One automatic physics step after FixedUpdate. There is no manual Physics2D.Simulate path.
        if (Physics2D.simulationMode != SimulationMode2D.FixedUpdate)
            Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
    }
    internal static void FixedStep(GameSimulationDriver source)
    {
        if (!ReferenceEquals(driver, source)) return;
        EnforceNetworkTime();
        if (IsAuthority) AuthorityTick++;
    }

    public static object Wait(float seconds) => IsNetwork ? (object)new SimulationWait(seconds) : new WaitForSeconds(seconds);
    private sealed class SimulationWait : CustomYieldInstruction
    {
        private float remaining;
        public SimulationWait(float seconds) { remaining = seconds; }
        public override bool keepWaiting { get { remaining -= DeltaTime; return remaining > 0; } }
    }

    public static void Schedule(MonoBehaviour owner, Action callback, string legacyMethodName, float delay)
    {
        if (!IsNetwork) { owner.Invoke(legacyMethodName, delay); return; }
        owner.StartCoroutine(Delayed(owner, callback, delay));
    }
    private static IEnumerator Delayed(MonoBehaviour owner, Action callback, float delay)
    {
        yield return Wait(delay);
        if (owner != null) callback();
    }

    public static void DestroyAfter(GameObject target, float delay)
    {
        if (!IsNetwork) { UnityEngine.Object.Destroy(target, delay); return; }
        var lifetime = target.GetComponent<SimulationLifetime>() ?? target.AddComponent<SimulationLifetime>();
        lifetime.Schedule(delay);
    }
}
