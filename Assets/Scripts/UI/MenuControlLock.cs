using UnityEngine;

/// <summary>One menu owns one input/pause request. Configured by UISystem, never used for HUD.</summary>
public sealed class MenuControlLock : MonoBehaviour
{
    private bool configured;
    private GameManager pauseManager;

    public void Configure()
    {
        configured = true;
        if (isActiveAndEnabled) PrepareOpen();
    }

    // Acquire before activating the target or deactivating the previous menu.
    public bool PrepareOpen()
    {
        if (!configured || !enabled || (transform.parent != null && !transform.parent.gameObject.activeInHierarchy))
            return false;
        if (GameplayControlLock.Acquire(this) && !GameSimulationClock.IsNetwork && Application.isPlaying && PlayerManager.instance != null)
            PlayerManager.instance.player?.CancelSwordAimForControlLock();
        if (!GameSimulationClock.IsNetwork && Application.isPlaying && pauseManager == null && GameManager.instance != null)
        {
            pauseManager = GameManager.instance;
            pauseManager.SetPauseOwner(this, true);
        }
        return true;
    }

    public void Release()
    {
        if (GameplayControlLock.Release(this)) GameplayControlLock.SuppressActionInputForCurrentFrame();
        if (pauseManager != null) pauseManager.SetPauseOwner(this, false);
        pauseManager = null;
    }

    public void Unconfigure()
    {
        configured = false;
        Release();
    }

    private void OnEnable() { if (configured) PrepareOpen(); }
    private void OnDisable() => Release();
    private void OnDestroy() => Release();
}
