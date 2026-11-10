using Rpg.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>Opens the existing B-key craft UI for the admitted local actor; no legacy UI controller is enabled.</summary>
public sealed class NetworkCraftPanel : MonoBehaviour
{
    private GameObject root;
    private UI_CraftWindow window;
    private RoomEntityPresentation local;
    private MenuControlLock controlLock;
    public bool IsOpen => root != null && root.activeInHierarchy;
    public void Prepare(Scene scene)
    {
        Unbind(); root = null; window = null; controlLock = null;
        foreach (var sceneRoot in scene.GetRootGameObjects())
        {
            var ui = sceneRoot.GetComponentInChildren<UI>(true);
            if (ui == null || ui.CraftPanel == null) continue;
            root = ui.CraftPanel; window = ui.craftWindow; break;
        }
        if (root == null || window == null) return;
        root.SetActive(false); window.BindNetwork(null);
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
        if (local == null || !local.IsValid || !local.IsReady || local.View == null) { Close(); return; }
        if (!Application.isFocused || !Input.GetKeyDown(KeyCode.B)) return;
        var focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (focused != null && (focused.GetComponentInParent<TMP_InputField>() != null || focused.GetComponentInParent<UnityEngine.UI.InputField>() != null)) return;
        if (IsOpen) Close();
        else if (GetComponent<NetworkSkillTreePanel>()?.IsOpen == true) { GetComponent<NetworkSkillTreePanel>().Close(); Open(); }
        else if (GetComponent<NetworkStatsPanel>()?.IsOpen == true) { GetComponent<NetworkStatsPanel>().Close(); Open(); }
        else if (!GameplayControlLock.IsActionInputBlocked) Open();
    }
    public void Open()
    {
        if (root == null || window == null || local == null || !local.IsValid || !local.IsReady || local.View == null ||
            local.View.GetComponent<NetworkEntityIdentity>()?.IsLocalInputOwner != true || controlLock == null || !controlLock.PrepareOpen()) return;
        window.BindNetwork(local); root.SetActive(true);
    }
    public void Close()
    {
        if (window != null) window.BindNetwork(null);
        if (root != null) root.SetActive(false);
        if (controlLock != null) controlLock.Release();
    }
    public void Unbind() { Close(); local = null; }
    private void OnDisable() => Unbind();
    private void OnDestroy() => Unbind();
}
