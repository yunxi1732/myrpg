using System;
using System.Collections.Generic;
using UnityEngine;

public enum UIPanelId
{
    Character,
    SkillTree,
    Craft,
    Options,
    InGame,
    Room
}

/// <summary>Scene-owned panel registry. Registration does not change visibility.</summary>
public sealed class UISystem : IDisposable
{
    public bool IsDisposed { get; private set; }
    private static readonly UIPanelId[] menuGroup =
    {
        UIPanelId.Character, UIPanelId.Craft, UIPanelId.SkillTree,
        UIPanelId.Options, UIPanelId.InGame
    };

    // Existing panels can share BasePanel, so use explicit IDs rather than component types.
    private readonly Dictionary<UIPanelId, BasePanel> panels = new Dictionary<UIPanelId, BasePanel>();

    public BasePanel RegisterPanel(UIPanelId id, GameObject scenePanel)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(UISystem));
        if (scenePanel == null)
            throw new ArgumentNullException(nameof(scenePanel));

        if (panels.TryGetValue(id, out BasePanel registered) && registered != null)
        {
            if (registered.gameObject == scenePanel)
                return registered;
            throw new InvalidOperationException("Panel ID already registered: " + id);
        }

        BasePanel panel = scenePanel.GetComponent<BasePanel>();
        if (panel == null)
            panel = scenePanel.AddComponent<BasePanel>();
        panels[id] = panel;
        if (RequiresControlLock(id))
        {
            MenuControlLock menuLock = scenePanel.GetComponent<MenuControlLock>();
            if (menuLock == null) menuLock = scenePanel.AddComponent<MenuControlLock>();
            menuLock.Configure();
        }
        return panel;
    }

    public BasePanel GetPanel(UIPanelId id)
    {
        return panels.TryGetValue(id, out BasePanel panel) && panel != null ? panel : null;
    }

    public bool TryGetPanelId(GameObject scenePanel, out UIPanelId id)
    {
        if (scenePanel != null)
        {
            foreach (var entry in panels)
            {
                if (entry.Value != null && entry.Value.gameObject == scenePanel)
                {
                    id = entry.Key;
                    return true;
                }
            }
        }
        id = default;
        return false;
    }

    public bool OpenPanel(UIPanelId id, params object[] args)
    {
        BasePanel panel = GetPanel(id);
        if (panel == null || !panel.enabled ||
            (panel.transform.parent != null && !panel.transform.parent.gameObject.activeInHierarchy)) return false;
        MenuControlLock menuLock = RequiresControlLock(id) ? panel.GetComponent<MenuControlLock>() : null;
        if (menuLock != null && !menuLock.PrepareOpen()) return false;
        panel.OnShow(args);
        if (!panel.gameObject.activeInHierarchy)
        {
            if (menuLock != null) menuLock.Release();
            return false;
        }
        return true;
    }

    public bool ClosePanel(UIPanelId id)
    {
        BasePanel panel = GetPanel(id);
        if (panel == null) return false;
        panel.OnClose();
        // Also handles already-inactive panels and repeated close requests.
        if (RequiresControlLock(id)) panel.GetComponent<MenuControlLock>()?.Release();
        return true;
    }

    /// <summary>Only the four menus and HUD participate; overlays are independent.</summary>
    public bool ShowExclusive(UIPanelId id)
    {
        BasePanel target = GetPanel(id);
        if (target == null || !target.enabled ||
            (target.transform.parent != null && !target.transform.parent.gameObject.activeInHierarchy)) return false;
        MenuControlLock targetLock = RequiresControlLock(id) ? target.GetComponent<MenuControlLock>() : null;
        if (targetLock != null && !targetLock.PrepareOpen()) return false;
        bool opened = false;
        try
        {
            foreach (UIPanelId other in menuGroup)
                if (other != id) ClosePanel(other);
            opened = OpenPanel(id);
            return opened;
        }
        finally
        {
            if (!opened && targetLock != null) targetLock.Release();
        }
    }

    public void CloseMenuGroup()
    {
        foreach (UIPanelId id in menuGroup)
            ClosePanel(id);
    }

    // The main-menu room panel has no gameplay input or pause ownership.
    private static bool RequiresControlLock(UIPanelId id) => id != UIPanelId.InGame;

    /// <summary>Release scene references; scene objects remain owned by Unity.</summary>
    public void Clear()
    {
        foreach (BasePanel panel in panels.Values)
            if (panel != null) panel.GetComponent<MenuControlLock>()?.Unconfigure();
        panels.Clear();
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Clear();
    }
}
