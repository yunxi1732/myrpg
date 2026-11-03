using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ArchitectureVerification
{
    private sealed class Module : IUtility, IModel, ISystem, IDisposable
    {
        private readonly Action onInit;
        public int Count;
        public int DisposeCount;
        public Action OnDispose = () => { };
        public void Dispose() { DisposeCount++; OnDispose(); }
        public Module(Action onInit) { this.onInit = onInit; }
        public void Init() { Count++; onInit(); }
    }

    [MenuItem("Tools/RPG/Verify Architecture")]
    public static void Verify()
    {
        var order = new List<string>();
        var architecture = new Architecture();
        var utility = new Module(() => order.Add("Utility"));
        var model = new Module(() => order.Add("Model"));
        var system = new Module(() =>
        {
            Require(model.Count == 1, "Model must be ready before System");
            Require(ReferenceEquals(architecture.GetModel<IModel>(), model), "System can resolve Model");
            order.Add("System");
        });
        architecture.RegistSystem<ISystem>(system);
        architecture.RegistModel<IModel>(model);
        architecture.RegistUtility<IUtility>(utility);
        Require(order.Count == 0, "Registration must not initialize modules");
        ExpectInvalid(() => architecture.RegistModel<IModel>(model));
        architecture.InitAllModules();
        architecture.InitAllModules();
        Require(string.Join(",", order) == "Utility,Model,System", "Layer initialization order");
        Require(utility.Count == 1 && model.Count == 1 && system.Count == 1, "Initialize each module once");
        ExpectInvalid(() => architecture.RegistModel<Module>(new Module(() => { })));

        var aliases = new Architecture();
        var shared = new Module(() => { });
        aliases.RegistModel<IModel>(shared);
        aliases.RegistModel<Module>(shared);
        aliases.InitAllModules();
        aliases.InitAllModules();
        Require(shared.Count == 1, "Multiple registrations of same object initialize once");

        var failed = new Architecture();
        var broken = new Module(() => { throw new InvalidOperationException("Expected test failure"); });
        var skipped = new Module(() => { });
        failed.RegistModel<IModel>(broken);
        failed.RegistSystem<ISystem>(skipped);
        ExpectInvalid(failed.InitAllModules);
        ExpectInvalid(failed.InitAllModules);
        Require(broken.Count == 1 && skipped.Count == 0, "Failure stops initialization without retry");
        var released = new List<string>();
        utility.OnDispose = () => released.Add("Utility");
        model.OnDispose = () => released.Add("Model");
        system.OnDispose = () => released.Add("System");
        architecture.Dispose();
        architecture.Dispose();
        Require(string.Join(",", released) == "System,Model,Utility", "Reverse cleanup order");
        Require(system.DisposeCount == 1 && model.DisposeCount == 1 && utility.DisposeCount == 1, "Cleanup once");
        Require(architecture.GetModel<IModel>() == null, "Old registry cleared");
        ExpectInvalid(() => architecture.RegistModel<IModel>(model));
        aliases.Dispose();
        Require(shared.DisposeCount == 1, "Alias cleanup once");
        failed.Dispose();
        Require(broken.DisposeCount == 1 && skipped.DisposeCount == 0, "Partial initialization cleanup");
        var fresh = new Architecture();
        Require(fresh.GetModel<IModel>() == null, "New architecture has no old instances");
        var freshModel = new Module(() => { });
        fresh.RegistModel<IModel>(freshModel);
        fresh.InitAllModules();
        Require(!ReferenceEquals(fresh.GetModel<IModel>(), model), "Fresh instance after restart");
        fresh.Dispose();

        var cleanupFailure = new Architecture();
        var badCleanup = new Module(() => { });
        var goodCleanup = new Module(() => { });
        badCleanup.OnDispose = () => { throw new Exception("Expected cleanup failure"); };
        cleanupFailure.RegistModel<IModel>(goodCleanup);
        cleanupFailure.RegistSystem<ISystem>(badCleanup);
        cleanupFailure.InitAllModules();
        bool reported = false;
        try { cleanupFailure.Dispose(); }
        catch (AggregateException) { reported = true; }
        cleanupFailure.Dispose();
        Require(reported && goodCleanup.DisposeCount == 1 && cleanupFailure.GetSystem<ISystem>() == null,
            "Cleanup continues and clears registry after failure");
        var hostObject = new GameObject("ArchitectureHost verification");
        var host = hostObject.AddComponent<ArchitectureHost>();
        var owned = host.Current;
        var ownedModel = new Module(() => { });
        owned.RegistModel<IModel>(ownedModel);
        owned.InitAllModules();
        UnityEngine.Object.DestroyImmediate(hostObject);
        Require(ownedModel.DisposeCount == 1 && owned.GetModel<IModel>() == null, "Host destruction cleans architecture");
        Debug.Log("[Architecture] PASS: reverse cleanup, repeat cleanup, fresh instances, host lifecycle; order, once-only initialization, aliases, registration guards and failure behavior");
    }

    private static void ExpectInvalid(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("[Architecture] FAIL: expected InvalidOperationException");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new Exception("[Architecture] FAIL: " + message);
    }
}

