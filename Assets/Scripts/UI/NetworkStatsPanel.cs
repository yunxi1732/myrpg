using Rpg.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>Reuses the local room actor's stats, bag and equipment branches of the character panel.</summary>
public sealed class NetworkStatsPanel : MonoBehaviour
{
    private Transform statRoot;
    private GameObject characterRoot;
    private UI_StatSlot[] slots = new UI_StatSlot[0];
    private RoomEntityPresentation local;
    private MenuControlLock controlLock;
    private NetworkInventoryView inventoryView;
    private readonly System.Collections.Generic.Dictionary<GameObject, bool> characterVisibility = new System.Collections.Generic.Dictionary<GameObject, bool>();
    public int BoundPlayerId => local != null && local.IsValid ? local.PlayerId : 0;
    public bool IsOpen => characterRoot != null && characterRoot.activeInHierarchy;

    public void Prepare(Scene scene)
    {
        Unbind(); statRoot = null; characterRoot = null; controlLock = null; slots = new UI_StatSlot[0];
        inventoryView?.Dispose(); inventoryView = null;
        characterVisibility.Clear();
        foreach (var root in scene.GetRootGameObjects())
        {
            var ui = root.GetComponentInChildren<UI>(true);
            var inventory = root.GetComponentInChildren<Inventory>(true);
            if (ui != null && ui.CharacterPanel != null) characterRoot = ui.CharacterPanel;
            if (inventory != null && inventory.StatSlotParent != null) statRoot = inventory.StatSlotParent;
            if (inventory != null) inventoryView = new NetworkInventoryView(inventory);
        }
        if (characterRoot == null || statRoot == null || !statRoot.IsChildOf(characterRoot.transform)) return;
        // Preserve the authored header/navigation/decorations; tooltips open only on hover.
        foreach (Transform child in characterRoot.transform)
            characterVisibility[child.gameObject] = child.gameObject.activeSelf && child.GetComponent<UI_Tooltip>() == null;
        slots = statRoot.GetComponentsInChildren<UI_StatSlot>(true);
        foreach (var slot in slots) slot.BindNetwork(null);
        characterRoot.SetActive(false);
        controlLock = characterRoot.GetComponent<MenuControlLock>();
        if (controlLock == null) controlLock = characterRoot.AddComponent<MenuControlLock>();
        controlLock.Configure();
    }

    public void BindNetwork(RoomEntityPresentation entity)
    {
        Unbind();
        if (entity == null || !entity.IsValid || !entity.IsReady || entity.View == null ||
            entity.View.GetComponent<NetworkEntityIdentity>()?.IsLocalInputOwner != true) return;
        local = entity;
        foreach (var slot in slots) if (slot != null) slot.BindNetwork(entity);
    }

    private void Update()
    {
        if (local == null || !local.IsValid || !local.IsReady || local.View == null) { if (IsOpen) Close(); return; }
        if (!Application.isFocused || !Input.GetKeyDown(KeyCode.C)) return;
        var focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (focused != null && (focused.GetComponentInParent<TMP_InputField>() != null || focused.GetComponentInParent<UnityEngine.UI.InputField>() != null)) return;
        if (IsOpen) Close();
        else if (GetComponent<NetworkSkillTreePanel>()?.IsOpen == true) { GetComponent<NetworkSkillTreePanel>().Close(); Open(); }
        else if (GetComponent<NetworkCraftPanel>()?.IsOpen == true) { GetComponent<NetworkCraftPanel>().Close(); Open(); }
        else if (!GameplayControlLock.IsActionInputBlocked) Open();
    }

    public void Open()
    {
        if (characterRoot == null || statRoot == null || slots.Length == 0 || local == null || !local.IsValid || !local.IsReady ||
            local.View == null || local.View.GetComponent<NetworkEntityIdentity>()?.IsLocalInputOwner != true ||
            characterRoot.transform.parent != null && !characterRoot.transform.parent.gameObject.activeInHierarchy) return;
        foreach (var entry in characterVisibility) if (entry.Key != null) entry.Key.SetActive(entry.Value);
        // Activate the adapted data paths without hiding their sibling headers or navigation.
        foreach (var branch in new[] { statRoot, inventoryView?.BagRoot, inventoryView?.EquipmentRoot, inventoryView?.MaterialsRoot })
        {
            if (branch == null || !branch.IsChildOf(characterRoot.transform)) continue;
            for (var path = branch; path != characterRoot.transform; path = path.parent) path.gameObject.SetActive(true);
        }
        if (controlLock == null || !controlLock.PrepareOpen()) return;
        characterRoot.SetActive(true);
        inventoryView?.Bind(local);
        foreach (var slot in slots) if (slot != null) slot.UpdateStatValueUI();
    }
    public void Close() { inventoryView?.Dispose(); if (characterRoot != null) characterRoot.SetActive(false); if (controlLock != null) controlLock.Release(); }
    public void Unbind()
    {
        Close(); local = null;
        foreach (var slot in slots) if (slot != null) slot.BindNetwork(null);
    }
    private void OnDisable() => Unbind();
    private void OnDestroy() => Unbind();
}
