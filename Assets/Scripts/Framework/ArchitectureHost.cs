using System;
using UnityEngine;

/// <summary>Optional scene owner. Register and initialize modules explicitly through Current.</summary>
public sealed class ArchitectureHost : MonoBehaviour
{
    private Architecture current;
    private bool closed;
    public Architecture Current
    {
        get
        {
            if (closed) throw new ObjectDisposedException(nameof(ArchitectureHost));
            return current ?? (current = new Architecture());
        }
    }

    private void Update() => current?.Update();

    private void OnApplicationQuit() => Close();
    private void OnDestroy() => Close();

    private void Close()
    {
        if (closed) return;
        closed = true;
        Architecture previous = current;
        current = null;
        try { previous?.Dispose(); }
        catch (Exception error) { Debug.LogException(error, this); }
    }
}
