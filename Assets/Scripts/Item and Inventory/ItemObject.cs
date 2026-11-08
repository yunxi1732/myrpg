using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Rpg.Networking;

public class ItemObject : MonoBehaviour
{
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private ItemData itemData;

    private void SetUpVisuals()
    {
        if (itemData == null)
            return;

        GetComponent<SpriteRenderer>().sprite = itemData.itemIcon;
        gameObject.name = "Item Object - " + itemData.itemName;
    }

    public void SetUpItem(ItemData _itemData, Vector2 _velocity)
    {
        itemData = _itemData;
        rb.velocity = _velocity;

        SetUpVisuals();
    }

    public void PickUpItem()
    {
        // Legacy pickup has no per-player inventory. Do not consume a network drop through it.
        if (!EntityRuntimePermissions.CanUseLegacyWorld(this)) return;
        if (!Inventory.instance.CanAddItem() && itemData.itemType == ItemType.Equipment)
        {
            rb.velocity = new Vector2(0, 5);
            PlayerManager.instance.player.fx.CreatePopUpText("Inventory is full");
            return;
        }

        Inventory.instance.AddItem(itemData);
        Destroy(gameObject);
    }
}
