using System.Collections.Generic;
using System.Collections;
using Unity.VisualScripting;
using UnityEditor;
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
    public bool IsInitialized { get; private set; }
    private bool receivedLoadData;

    public List<InventoryItem> stash;
    public Dictionary<ItemData, InventoryItem> stashDictionary;

    [Header("Inventory UI")]
    [SerializeField] private Transform inventorySlotParent;
    [SerializeField] private Transform stashSlotParent;
    [SerializeField] private Transform equipmentSlotParent;
    [SerializeField] private Transform statSlotParent;

    private UI_ItemSlot[] inventoryItemSlot;
    private UI_ItemSlot[] stashItemSlot;
    private UI_EquipmentSlot[] equipmentSlot;
    private UI_StatSlot[] statSlot;

    public float flaskCooldown => inventorySystem == null ? 0 : inventorySystem.FlaskCooldown;

    [Header("Data base")]
    public List<ItemData> itemDataBase;
    public List<InventoryItem> loadedItems;
    public List<ItemData_Equipment> loadedEquipment;

    private void Awake()
    {
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
        if (instance != this) yield break;
        inventoryItemSlot = inventorySlotParent.GetComponentsInChildren<UI_ItemSlot>(true);
        stashItemSlot = stashSlotParent.GetComponentsInChildren<UI_ItemSlot>(true);
        equipmentSlot = equipmentSlotParent.GetComponentsInChildren<UI_EquipmentSlot>(true);
        statSlot = statSlotParent.GetComponentsInChildren<UI_StatSlot>(true);

        // Let scene Start methods (including player stats and save loading) finish first.
        yield return null;
        while (SaveManager.instance != null && !SaveManager.instance.HasLoaded)
            yield return null;
        AddStartingItems();
        IsInitialized = true;
        UpdateSlotUI();
    }

    private void OnDestroy()
    {
        inventoryArchitecture?.Dispose();
        inventoryArchitecture = null;
        Model = null;
        inventorySystem = null;
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
        int capacity = inventoryItemSlot == null ? 0 : inventoryItemSlot.Length;
        if (!inventorySystem.EquipItem(_item, capacity) && inventoryItemSlot != null)
            Debug.Log("no more space");
        UpdateSlotUI();
    }
    public void UnequipItem(ItemData_Equipment itemToRemove)
    {
        inventorySystem.UnequipItem(itemToRemove);
    }

    private void UpdateSlotUI()
    {
        if (inventoryItemSlot == null || stashItemSlot == null || equipmentSlot == null || statSlot == null)
            return;
        for (int i = 0; i < equipmentSlot.Length; i++)
        {
            foreach (KeyValuePair<ItemData_Equipment, InventoryItem> item in equipmentDictionary)
            {
                if (item.Key.equipmentType == equipmentSlot[i].slotType)
                    equipmentSlot[i].UpdateSlot(item.Value);
            }
        }

        for (int i = 0; i < inventoryItemSlot.Length; i++)
        {
            inventoryItemSlot[i].CleanUpSlot();
        }
        for (int i = 0; i < stashItemSlot.Length; i++)
        {
            stashItemSlot[i].CleanUpSlot();
        }

        for (int i = 0; i < inventory.Count; i++)
        {
            inventoryItemSlot[i].UpdateSlot(inventory[i]);
        }
        for (int i = 0; i < stash.Count; i++)
        {
            stashItemSlot[i].UpdateSlot(stash[i]);
        }
        UpdateStatsUI();
    }

    public void UpdateStatsUI()
    {
        if (statSlot == null) return;
        for (int i = 0; i < statSlot.Length; i++)
        {
            statSlot[i].UpdateStatValueUI();
        }
    }

    public void AddItem(ItemData _item)
    {
        int capacity = inventoryItemSlot == null ? 0 : inventoryItemSlot.Length;
        if (!inventorySystem.AddItem(_item, capacity) && _item.itemType == ItemType.Equipment && inventoryItemSlot != null)
            Debug.Log("no more space");
        UpdateSlotUI();
    }
    public void RemoveItem(ItemData _item)
    {
        inventorySystem.RemoveItem(_item);
        UpdateSlotUI();
    }

    public bool CanAddItem()
    {
        if (inventoryItemSlot == null) return false;
        if (!inventorySystem.CanAddItem(inventoryItemSlot.Length))
        {
            Debug.Log("no more space");
            return false;
        }
        return true;
    }

    public bool canCraft(ItemData_Equipment _itemToCraft, List<InventoryItem> _requiredMaterials)
    {
        int capacity = inventoryItemSlot == null ? 0 : inventoryItemSlot.Length;
        if (!inventorySystem.CanCraft(_itemToCraft, _requiredMaterials, capacity, out bool productAdded))
        {
            Debug.Log("not enough materials");
            return false;
        }
        if (!productAdded && inventoryItemSlot != null)
            Debug.Log("no more space");
        UpdateSlotUI();
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
        if (GetEquipment(EquipmentType.Flask) == null) return;
        if (!inventorySystem.UseFlask(Time.time))
            Debug.Log("Flask on cooldown");
    }

    public bool CanUseArmor()
    {
        if (inventorySystem.CanUseArmor(Time.time)) return true;
        Debug.Log("armor on cooldown");
        return false;
    }
    void ISaveManager.LoadData(GameData _data)
    {
        if (IsInitialized) return;
        receivedLoadData = true;
        if (loadedItems == null) loadedItems = new List<InventoryItem>();
        if (loadedEquipment == null) loadedEquipment = new List<ItemData_Equipment>();
        loadedItems.Clear();
        loadedEquipment.Clear();
        foreach (KeyValuePair<string, int> pair in _data.inventory)
        {
            foreach (var item in itemDataBase)
            {
                if (item != null && item.itemId == pair.Key)
                {
                    InventoryItem itemToLoad = new InventoryItem(item);
                    itemToLoad.stackSize = pair.Value;
                    loadedItems.Add(itemToLoad);
                }
            }
        }

        foreach (string loadedItemId in _data.equipmentId)
        {
            foreach (var item in itemDataBase)
            {
                if (item != null && loadedItemId == item.itemId)
                {
                    loadedEquipment.Add(item as ItemData_Equipment);
                }
            }
        }
    }

    void ISaveManager.SaveData(ref GameData _data)
    {
        if (!IsInitialized) return;
        _data.inventory.Clear();
        _data.equipmentId.Clear();
        foreach (KeyValuePair<ItemData, InventoryItem> pair in inventoryDictionary)
        {
            _data.inventory.Add(pair.Key.itemId, pair.Value.stackSize);
        }

        foreach (KeyValuePair<ItemData, InventoryItem> pair in stashDictionary)
        {
            _data.inventory.Add(pair.Key.itemId, pair.Value.stackSize);
        }
        foreach (KeyValuePair<ItemData_Equipment, InventoryItem> pair in equipmentDictionary)
        {
            _data.equipmentId.Add(pair.Key.itemId);
        }
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
