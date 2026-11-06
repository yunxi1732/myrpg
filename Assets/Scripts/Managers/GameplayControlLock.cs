using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// Shared gameplay-input lock. Owners acquire once and release on close/disable/destroy.
/// Does not change timeScale or disable UI input; gameplay entry points query IsLocked.
/// </summary>
public static class GameplayControlLock
{
    private sealed class OwnerComparer : IEqualityComparer<object>
    {
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private static readonly HashSet<object> holders = new HashSet<object>(new OwnerComparer());
    private static int suppressedActionFrame = -1;

    public static bool IsLocked => holders.Count > 0;
    public static int HolderCount => holders.Count;
    /// <summary>Action gates include the frame in which a UI owner released its lock.</summary>
    public static bool IsActionInputBlocked => IsLocked || Time.frameCount == suppressedActionFrame;

    public static void SuppressActionInputForCurrentFrame() => suppressedActionFrame = Time.frameCount;

    /// <summary>True only for a new owner. Null and duplicate requests are no-ops.</summary>
    public static bool Acquire(object owner)
    {
        return !ReferenceEquals(owner, null) && holders.Add(owner);
    }

    /// <summary>Release only this owner's lock; null/unknown/repeated releases return false.</summary>
    public static bool Release(object owner)
    {
        return !ReferenceEquals(owner, null) && holders.Remove(owner);
    }

    // Also runs when Enter Play Mode has Domain Reload disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay()
    {
        holders.Clear();
        suppressedActionFrame = -1;
    }
}
