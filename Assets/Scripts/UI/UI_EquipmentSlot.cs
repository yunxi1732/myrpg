using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class UI_EquipmentSlot : UI_ItemSlot
{
    public EquipmentType slotType;

    private void OnValidate()
    {
        gameObject.name = "Equipment slot - " + slotType.ToString();
    }

    public override void OnPointerDown(PointerEventData eventData)
    {
        if (Rpg.Networking.RoomSceneCoordinator.ProtectSaves)
        {
            if (item?.data is ItemData_Equipment equipped && networkEntity != null)
                Rpg.Networking.RoomSceneCoordinator.Active.RequestLocalEquipmentChange(networkEntity, equipped, true);
            if (ui != null && ui.itemTooltip != null) ui.itemTooltip.HideTooltip();
            return; // Wait for A's confirmation; never clear the slot or add a local item here.
        }
        if (item == null || item.data == null)
            return;

        // Unload notification refreshes this slot synchronously; retain the item before it is cleared.
        ItemData_Equipment equipment = item.data as ItemData_Equipment;
        Inventory.instance.UnequipItem(equipment);
        Inventory.instance.AddItem(equipment);

        ui.itemTooltip.HideTooltip();
        CleanUpSlot();
    }
}
