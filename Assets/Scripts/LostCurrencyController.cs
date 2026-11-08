using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LostCurrencyController : MonoBehaviour
{
    public int currency;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        var player = collision.GetComponentInParent<Player>();
        if (Rpg.Networking.EntityRuntimePermissions.CanUseLegacyActions(player))
        {
            PlayerManager.instance.currency += currency;
            Destroy(this.gameObject);
        }
    }
}
