using System;
using UnityEditor;
using UnityEngine;

public static class IOCContainerVerification
{
    private interface IService { }
    private sealed class Service : IService { }

    [MenuItem("Tools/RPG/Verify IOCContainer")]
    public static void Verify()
    {
        var container = new IOCContainer();
        Require(container.Get<IService>() == null, "Unregistered type returns null");

        var first = new Service();
        container.Register<IService>(first);
        Require(ReferenceEquals(container.Get<IService>(), first), "Registration preserves identity");
        Require(ReferenceEquals(container.Get<IService>(), container.Get<IService>()), "Repeated get returns same instance");
        Require(container.Get<Service>() == null, "Registration uses exact declared type");

        container.Register<IService>(first);
        Require(ReferenceEquals(container.Get<IService>(), first), "Registering same instance is allowed");
        var replacement = new Service();
        container.Register<IService>(replacement);
        Require(ReferenceEquals(container.Get<IService>(), replacement), "Duplicate registration replaces instance");
        Require(new IOCContainer().Get<IService>() == null, "Containers have independent state");
        container.Register<IService>(null);
        Require(container.Get<IService>() == null, "Null registration returns null");
        Debug.Log("[IOCContainer] PASS: 8 checks");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("[IOCContainer] FAIL: " + message);
    }
}
