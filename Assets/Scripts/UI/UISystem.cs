using System;
using System.Collections.Generic;
using UnityEngine;

public enum UIPanelId
{
    Character,
    SkillTree,
    Craft,
    Options,
    InGame
}

/// <summary>Scene-owned panel registry. Registration does not change visibility.</summary>
public sealed class UISystem
{
    private static readonly UIPanelId[] menuGroup =
    {
        UIPanelId.Character, UIPanelId.Craft, UIPanelId.SkillTree,
        UIPanelId.Options, UIPanelId.InGame
    };

    // Existing panels can share BasePanel, so use explicit IDs rather than component types.
    private readonly Dictionary<UIPanelId, BasePanel> panels = new Dictionary<UIPanelId, BasePanel>();

    public BasePanel RegisterPanel(UIPanelId id, GameObject scenePanel)
    {
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
        if (panel == null) return false;
        panel.OnShow(args);
        return true;
    }

    public bool ClosePanel(UIPanelId id)
    {
        BasePanel panel = GetPanel(id);
        if (panel == null) return false;
        panel.OnClose();
        return true;
    }

    /// <summary>Only the four menus and HUD participate; overlays are independent.</summary>
    public bool ShowExclusive(UIPanelId id)
    {
        if (GetPanel(id) == null) return false;
        CloseMenuGroup();
        return OpenPanel(id);
    }

    public void CloseMenuGroup()
    {
        foreach (UIPanelId id in menuGroup)
            ClosePanel(id);
    }

    /// <summary>Release scene references; scene objects remain owned by Unity.</summary>
    public void Clear() => panels.Clear();
}
