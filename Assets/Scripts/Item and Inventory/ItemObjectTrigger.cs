using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Rpg.Networking;

public class ItemObjectTrigger : MonoBehaviour
{
    private ItemObject myItem => GetComponentInParent<ItemObject>();

    private void OnTriggerEnter2D(Collider2D collision)
    {
        Player player = collision.GetComponentInParent<Player>();
        if (player != null && EntityRuntimePermissions.CanUseLegacyActions(player))
        {
            if (player.GetComponent<CharaterStats>().isDead)
                return;

            Debug.Log("Picked up item");
            myItem.PickUpItem();
        }
    }
}
