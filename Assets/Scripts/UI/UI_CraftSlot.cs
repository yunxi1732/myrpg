using UnityEngine.EventSystems;

public class UI_CraftSlot : UI_ItemSlot
{
    protected override void Start()
    {
        base.Start();
    }

    public void SetupCraftSlot(ItemData_Equipment _data)
    {
        if (_data == null)
            return;

        item.data = _data;
        itemImage.sprite = _data.itemIcon;
        itemText.text = _data.itemName;
    }

    public override void OnPointerDown(PointerEventData eventData)
    {
        ItemData_Equipment craftData = item.data as ItemData_Equipment;

        Inventory.instance.canCraft(craftData, craftData.craftingMaterials);
    }
}
