using System;
using System.Collections.Generic;

/// <summary>Instance-owned event listeners, keyed by event type.</summary>
public sealed class GameEventSystem : IDisposable
{
    private readonly Dictionary<Type, Action<object>> listeners = new Dictionary<Type, Action<object>>();
    private bool disposed;

    // As in mmo-demo, repeated registration adds another callback; unregister removes one occurrence.
    public void Regist<T>(Action<object> onEvent)
    {
        if (disposed) throw new ObjectDisposedException(nameof(GameEventSystem));
        if (onEvent == null) throw new ArgumentNullException(nameof(onEvent));
        listeners.TryGetValue(typeof(T), out Action<object> current);
        listeners[typeof(T)] = current + onEvent;
    }

    public void UnRegist<T>(Action<object> onEvent)
    {
        if (!listeners.TryGetValue(typeof(T), out Action<object> current)) return;
        current -= onEvent;
        if (current == null) listeners.Remove(typeof(T));
        else listeners[typeof(T)] = current;
    }

    public void Send<T>(object data)
    {
        if (disposed || !listeners.TryGetValue(typeof(T), out Action<object> current)) return;
        // Snapshot semantics: registration changes affect the next send; disposal stops this send too.
        foreach (Action<object> callback in current.GetInvocationList())
        {
            if (disposed) break;
            callback(data);
        }
    }

    public void Dispose()
    {
        disposed = true;
        listeners.Clear();
    }
}
