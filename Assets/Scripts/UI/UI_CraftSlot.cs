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

        item = new InventoryItem(_data);
        itemImage.sprite = _data.itemIcon;
        itemText.text = _data.itemName;

        if (itemText.text.Length > 12)
            itemText.fontSize = (float)(itemText.fontSize * 0.7);
        else
            itemText.fontSize = 24;
    }

    public override void OnPointerDown(PointerEventData eventData)
    {
        if (ui == null) ui = GetComponentInParent<UI>(true);
        if (item?.data == null || ui == null || ui.craftWindow == null) return;
        ui.craftWindow.SetupCraftWindow(item.data as ItemData_Equipment);
    }
}
