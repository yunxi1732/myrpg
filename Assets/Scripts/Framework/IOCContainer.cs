using System;
using System.Collections.Generic;

/// <summary>Stores instances by their explicitly registered type.</summary>
public class IOCContainer
{
    public void Clear() => instances.Clear();
    private readonly Dictionary<Type, object> instances = new Dictionary<Type, object>();

    /// <summary>Replaces any previous registration for T. Null is allowed.</summary>
    public void Register<T>(T instance) where T : class
    {
        instances[typeof(T)] = instance;
    }

    /// <summary>Returns the registered instance, or null when T is not registered.</summary>
    public T Get<T>() where T : class
    {
        return instances.TryGetValue(typeof(T), out object instance) ? (T)instance : null;
    }
}
