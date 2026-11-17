using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DeadZone : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (Rpg.Networking.RoomSceneCoordinator.Active?.TryKillFromDeadZone(this, collision) == true) return;
        var stats = collision.GetComponent<CharaterStats>();
        if (stats != null)
        {
            if (stats is EnemyStats && GameSimulationClock.IsNetwork &&
                Rpg.Networking.EntityRuntimePermissions.Identity(stats)?.IsBound == true)
                stats.ApplyAuthorityNetworkHealth(0);
            else stats.KillEntity();
        }
        else
            Destroy(collision.gameObject);
    }
}
