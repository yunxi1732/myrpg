using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using Unity.VisualScripting;

public class UI_ItemSlot : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] protected Image itemImage;
    [SerializeField] protected TextMeshProUGUI itemText;

    protected UI ui;
    public InventoryItem item;
    protected Rpg.Networking.RoomEntityPresentation networkEntity;
    public void BindNetwork(Rpg.Networking.RoomEntityPresentation entity) { networkEntity = entity; CleanUpSlot(); }

    protected virtual void Start()
    {
        ui = GetComponentInParent<UI>();
    }

    public void UpdateSlot(InventoryItem _item)
    {
        item = _item;
        itemImage.color = Color.white;

        if (item != null)
        {
            itemImage.sprite = item.data.itemIcon;

            if (item.stackSize > 0)
            {
                itemText.text = item.stackSize.ToString();
            }
            else
                itemText.text = "";
        }
    }

    public void CleanUpSlot()
    {
        item = null;

        itemImage.sprite = null;
        itemImage.color = Color.clear;
        itemText.text = "";
    }

    public virtual void OnPointerDown(PointerEventData eventData)
    {
        if (Rpg.Networking.RoomSceneCoordinator.ProtectSaves)
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return; // Discard is not adapted.
            if (item?.data is ItemData_Equipment equipment && networkEntity != null)
                Rpg.Networking.RoomSceneCoordinator.Active.RequestLocalEquipmentChange(networkEntity, equipment, false);
            if (ui != null && ui.itemTooltip != null) ui.itemTooltip.HideTooltip();
            return;
        }
        if (item == null)
            return;

        AudioManager.instance.PlaySFX(4, null);

        if (Input.GetKey(KeyCode.LeftControl))
        {
            Inventory.instance.RemoveItem(item.data);
            return;
        }

        if (item.data.itemType == ItemType.Equipment)
            Inventory.instance.EquipItem(item.data);

        ui.itemTooltip.HideTooltip();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (item == null)
            return;

        if (ui != null && ui.itemTooltip != null) ui.itemTooltip.ShowTooltip(item.data as ItemData_Equipment);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (item == null)
            return;

        if (ui != null && ui.itemTooltip != null) ui.itemTooltip.HideTooltip();
    }
}
