using UnityEngine;

public interface ITemporaryEffectPlayback
{
    void StopPlayback();
}

/// <summary>Pool identity is independent of GameObject activation. Only the owning pool accepts returns.</summary>
[DisallowMultipleComponent]
public sealed class PooledTemporaryEffect : MonoBehaviour
{
    public TemporaryEffectPool.PrefabPool OwnerPool { get; private set; }
    public bool IsInUse { get; private set; }

    public bool ReturnToPool()
    {
        return OwnerPool != null && OwnerPool.ReturnToOwner(this);
    }

    private MonoBehaviour[] playbackComponents;
    internal void StopPlayback()
    {
        if (playbackComponents == null) return;
        foreach (var component in playbackComponents)
            if (component != null && component is ITemporaryEffectPlayback playback) playback.StopPlayback();
    }

    internal bool IsReturning { get; set; }
    internal void DetachPool() { OwnerPool = null; IsInUse = false; IsReturning = false; }
    internal void MarkIdle() { IsInUse = false; }

    internal void Initialize(TemporaryEffectPool.PrefabPool owner) { OwnerPool = owner; playbackComponents = GetComponents<MonoBehaviour>(); }
    internal void MarkInUse() { IsInUse = true; }
}
