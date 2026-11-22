using UnityEngine;

/// <summary>
/// Minimal panel lifecycle. The owner calls OnInit before first use, then OnShow/OnClose.
/// Overrides should call base to preserve visibility changes.
/// </summary>
public class BasePanel : MonoBehaviour, IBasePanel
{
    /// <summary>Prepare a panel in the closed state; not called automatically by Awake.</summary>
    public virtual void OnInit()
    {
        gameObject.SetActive(false);
    }

    /// <summary>Show the panel. Arguments are reserved for panel-specific data.</summary>
    public virtual void OnShow(params object[] args)
    {
        gameObject.SetActive(true);
    }

    /// <summary>Hide the panel without destroying it.</summary>
    public virtual void OnClose()
    {
        gameObject.SetActive(false);
    }
}
