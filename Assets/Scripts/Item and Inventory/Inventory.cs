using System.Collections.Generic;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class Inventory : MonoBehaviour, ISaveManager
{
    public static Inventory instance;

    public List<ItemData> startingItems;

    public List<InventoryItem> equipment;
    public Dictionary<ItemData_Equipment, InventoryItem> equipmentDictionary;

    public List<InventoryItem> inventory;
    public Dictionary<ItemData, InventoryItem> inventoryDictionary;
    public InventoryModel Model { get; private set; }
    private Architecture inventoryArchitecture;
    private InventorySystem inventorySystem;
    private Player combatOwner;
    public void BindOwner(Player owner)
    {
        if (owner == null || combatOwner != null && combatOwner != owner)
            throw new System.InvalidOperationException("Inventory cannot change combat owner.");
        owner.Combat.BindLegacyInventory(this);
        combatOwner = owner;
        inventorySystem.BindOwner(owner.Combat);
    }

    private void BindStandaloneOwner()
    {
        if (combatOwner == null && PlayerManager.instance != null) BindOwner(PlayerManager.instance.player);
    }
    public bool IsInitialized { get; private set; }
    private bool receivedLoadData;

    public List<InventoryItem> stash;
    public Dictionary<ItemData, InventoryItem> stashDictionary;

    [Header("Inventory UI")]
    [SerializeField] private Transform inventorySlotParent;
    [SerializeField] private Transform stashSlotParent;
    [SerializeField] private Transform equipmentSlotParent;
    [SerializeField] private Transform statSlotParent;

    private InventoryView inventoryView;
    private InventoryStatsView statsView;

    public float flaskCooldown => inventorySystem == null ? 0 : inventorySystem.FlaskCooldown;
    public float FlaskCooldownRemaining => inventorySystem == null ? 0 :
        inventorySystem.GetFlaskCooldownRemaining(GameSimulationClock.Time);

    [Header("Data base")]
    public List<ItemData> itemDataBase;
    public List<InventoryItem> loadedItems;
    public List<ItemData_Equipment> loadedEquipment;

    private void Awake()
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanUseLegacyWorld(this)) { enabled = false; return; }
        if (instance == null)
            instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }
        InitializeModel();
    }

    private void InitializeModel()
    {
        inventoryArchitecture = new Architecture();
        var model = new InventoryModel();
        inventoryArchitecture.RegistModel<InventoryModel>(model);
        inventoryArchitecture.RegistSystem<InventorySystem>(new InventorySystem(model));
        inventoryArchitecture.InitAllModules();
        Model = inventoryArchitecture.GetModel<InventoryModel>();
        inventorySystem = inventoryArchitecture.GetSystem<InventorySystem>();
        // Compatibility fields alias the model collections; UI and saves use the same item objects.
        equipment = Model.Equipment;
        equipmentDictionary = Model.EquipmentDictionary;
        inventory = Model.Items;
        inventoryDictionary = Model.ItemDictionary;

        stash = Model.Stash;
        stashDictionary = Model.StashDictionary;

    }

    private IEnumerator Start()
    {
        if (instance != this || !Rpg.Networking.EntityRuntimePermissions.CanUseLegacyWorld(this)) yield break;
        inventoryView = new InventoryView(inventorySlotParent, stashSlotParent, equipmentSlotParent, inventoryArchitecture, Model);
        statsView = new InventoryStatsView(statSlotParent);

        // Let scene Start methods (including player stats and save loading) finish first.
        yield return null;
        while (SaveManager.instance != null && !SaveManager.instance.HasLoaded)
            yield return null;
        if (!Rpg.Networking.EntityRuntimePermissions.CanUseLegacyWorld(this)) yield break;
        AddStartingItems();
        IsInitialized = true;
        NotifyInventoryChanged();
    }

    private void OnDestroy()
    {
        inventoryView?.Dispose();
        inventoryArchitecture?.Dispose();
        inventoryArchitecture = null;
        Model = null;
        inventorySystem = null;
        inventoryView = null;
        statsView = null;
        if (instance == this)
            instance = null;
    }

    private void AddStartingItems()
    {
        if (IsInitialized) return;
        bool useSave = receivedLoadData && SaveManager.instance != null && SaveManager.instance.LoadedFromSave;
        if (useSave)
        {
            foreach (ItemData_Equipment item in loadedEquipment)
            {
                EquipItem(item);
            }

            foreach (InventoryItem item in loadedItems)
            {
                for (int i = 0; i < item.stackSize; i++)
                {
                    AddItem(item.data);
                }
            }
            return;
        }

        if (startingItems == null) return;
        for (int i = 0; i < startingItems.Count; i++)
        {
            if (startingItems[i] != null)
                AddItem(startingItems[i]);
        }
    }

    public void EquipItem(ItemData _item)
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanUseLegacyWorld(this)) return;
        BindStandaloneOwner();
        int capacity = inventoryView == null ? 0 : inventoryView.InventoryCapacity;
        if (!inventorySystem.EquipItem(_item, capacity) && inventoryView != null)
            Debug.Log("no more space");
        NotifyInventoryChanged();
    }
    public void UnequipItem(ItemData_Equipment itemToRemove)
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanUseLegacyWorld(this)) return;
        BindStandaloneOwner();
        inventorySystem.UnequipItem(itemToRemove);
        NotifyInventoryChanged();
    }

    private void NotifyInventoryChanged()
    {
        if (!IsInitialized) return;
        inventoryArchitecture.SendEvent<InventoryChanged>(Model);
        UpdateStatsUI();
    }

    public void UpdateStatsUI()
    {
        statsView?.Refresh();
    }
    public void AddItem(ItemData _item)
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanUseLegacyWorld(this)) return;
        int capacity = inventoryView == null ? 0 : inventoryView.InventoryCapacity;
        if (!inventorySystem.AddItem(_item, capacity) && _item.itemType == ItemType.Equipment && inventoryView != null)
            Debug.Log("no more space");
        NotifyInventoryChanged();
    }
    public void RemoveItem(ItemData _item)
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanUseLegacyWorld(this)) return;
        inventorySystem.RemoveItem(_item);
        NotifyInventoryChanged();
    }

    public bool CanAddItem()
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanUseLegacyWorld(this)) return false;
        if (inventoryView == null) return false;
        if (!inventorySystem.CanAddItem(inventoryView.InventoryCapacity))
        {
            Debug.Log("no more space");
            return false;
        }
        return true;
    }

    public bool canCraft(ItemData_Equipment _itemToCraft, List<InventoryItem> _requiredMaterials)
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanUseLegacyWorld(this)) return false;
        int capacity = inventoryView == null ? 0 : inventoryView.InventoryCapacity;
        if (!inventorySystem.CanCraft(_itemToCraft, _requiredMaterials, capacity, out bool productAdded))
        {
            Debug.Log("not enough materials");
            return false;
        }
        if (!productAdded && inventoryView != null)
            Debug.Log("no more space");
        NotifyInventoryChanged();
        Debug.Log("here is your item - " + _itemToCraft.name);
        return true;
    }
    public List<InventoryItem> GetEquipmentList() => equipment;

    public List<InventoryItem> GetStashList() => stash;

    public ItemData_Equipment GetEquipment(EquipmentType _type)
    {
        ItemData_Equipment equipedItem = null;

        foreach (KeyValuePair<ItemData_Equipment, InventoryItem> item in equipmentDictionary)
        {
            if (item.Key.equipmentType == _type)
                equipedItem = item.Key;
        }

        return equipedItem;
    }

    public void UseFlask()
    {
        // Network flask use still requires the later authoritative request/result adaptation.
        if (!Rpg.Networking.EntityRuntimePermissions.CanUseLegacyWorld(this) || !IsInitialized || GameplayControlLock.IsActionInputBlocked ||
            Time.timeScale <= 0 || PlayerManager.instance == null || PlayerManager.instance.player == null ||
            PlayerManager.instance.player.stats == null || PlayerManager.instance.player.stats.isDead) return;
        if (GetEquipment(EquipmentType.Flask) == null) return;
        if (!inventorySystem.UseFlask(GameSimulationClock.Time))
            Debug.Log("Flask on cooldown");
    }

    public bool CanUseArmor()
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanUseLegacyWorld(this)) return false;
        if (inventorySystem.CanUseArmor(GameSimulationClock.Time)) return true;
        Debug.Log("armor on cooldown");
        return false;
    }
    void ISaveManager.LoadData(GameData _data)
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanPersist(this)) return;
        if (IsInitialized) return;
        receivedLoadData = true;
        if (loadedItems == null) loadedItems = new List<InventoryItem>();
        if (loadedEquipment == null) loadedEquipment = new List<ItemData_Equipment>();
        InventorySaveMapper.Read(_data, itemDataBase, loadedItems, loadedEquipment);
    }

    void ISaveManager.SaveData(ref GameData _data)
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanPersist(this)) return;
        if (!IsInitialized) return;
        InventorySaveMapper.Write(Model, _data);
    }
#if UNITY_EDITOR
    [ContextMenu("Fill up item data base")]
    private void FillUpItemDataBase() => itemDataBase = new List<ItemData>(GetItemDataBase());

    private List<ItemData> GetItemDataBase()
    {
        List<ItemData> itemDataBase = new List<ItemData>();
        string[] assetNames = AssetDatabase.FindAssets("", new[] { "Assets/Data/Items" });
        foreach (string SOName in assetNames)
        {
            var SOpath = AssetDatabase.GUIDToAssetPath(SOName);
            var itemData = AssetDatabase.LoadAssetAtPath<ItemData>(SOpath);
            itemDataBase.Add(itemData);
        }
        return itemDataBase;
    }
#endif
}
