using System;
using Rpg.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>The existing K-key skill tree renders only the local actor's A-confirmed nodes.</summary>
public sealed class NetworkSkillTreePanel : MonoBehaviour
{
    private GameObject root;
    private UI ui;
    private UI_SkillTreeSlot[] slots = Array.Empty<UI_SkillTreeSlot>();
    private RoomEntityPresentation local;
    private MenuControlLock controlLock;
    private Action changed;
    private int bindingVersion;
    public bool IsOpen => root != null && root.activeInHierarchy;
    public void Prepare(Scene scene)
    {
        Unbind(); root = null; ui = null; controlLock = null; slots = Array.Empty<UI_SkillTreeSlot>();
        foreach (var sceneRoot in scene.GetRootGameObjects())
        {
            var candidate = sceneRoot.GetComponentInChildren<UI>(true);
            if (candidate == null || candidate.SkillTreePanel == null) continue;
            ui = candidate; root = ui.SkillTreePanel; break;
        }
        if (root == null) return;
        slots = root.GetComponentsInChildren<UI_SkillTreeSlot>(true);
        foreach (var slot in slots) slot.BindNetwork(null);
        root.SetActive(false);
        controlLock = root.GetComponent<MenuControlLock>();
        if (controlLock == null) controlLock = root.AddComponent<MenuControlLock>();
        controlLock.Configure();
    }
    public void BindNetwork(RoomEntityPresentation entity)
    {
        Unbind();
        if (entity != null && entity.IsValid && entity.IsReady && entity.View != null &&
            entity.View.GetComponent<NetworkEntityIdentity>()?.IsLocalInputOwner == true) local = entity;
    }
    private void Update()
    {
        if (local == null || !local.IsValid || !local.IsReady || local.View == null) { if (IsOpen) Close(); return; }
        if (!Application.isFocused || !Input.GetKeyDown(KeyCode.K)) return;
        var focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (focused != null && (focused.GetComponentInParent<TMP_InputField>() != null || focused.GetComponentInParent<UnityEngine.UI.InputField>() != null)) return;
        if (IsOpen) Close();
        else if (GetComponent<NetworkStatsPanel>()?.IsOpen == true || GetComponent<NetworkCraftPanel>()?.IsOpen == true)
        { GetComponent<NetworkStatsPanel>()?.Close(); GetComponent<NetworkCraftPanel>()?.Close(); Open(); }
        else if (!GameplayControlLock.IsActionInputBlocked) Open();
    }
    public void Open()
    {
        if (root == null || local == null || !local.IsValid || !local.IsReady || local.View == null ||
            local.View.GetComponent<NetworkEntityIdentity>()?.IsLocalInputOwner != true || controlLock == null || !controlLock.PrepareOpen()) return;
        Detach();
        var expected = local; int version = bindingVersion;
        changed = () => { if (this != null && version == bindingVersion && ReferenceEquals(local, expected)) Refresh(); };
        local.Changed += changed;
        Refresh(); root.SetActive(true);
    }
    private void Refresh() { foreach (var slot in slots) if (slot != null) slot.BindNetwork(local); }
    private void Detach()
    {
        ++bindingVersion;
        if (local != null && changed != null) local.Changed -= changed;
        changed = null;
    }
    public void Close()
    {
        Detach();
        foreach (var slot in slots) if (slot != null) slot.BindNetwork(null);
        if (ui != null && ui.skillTooltip != null) ui.skillTooltip.HideTooltip();
        if (root != null) root.SetActive(false);
        if (controlLock != null) controlLock.Release();
    }
    public void Unbind() { Close(); local = null; }
    private void OnDisable() => Unbind();
    private void OnDestroy() => Unbind();
}
