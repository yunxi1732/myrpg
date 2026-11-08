using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    private Animator anim;
    public string id;
    public bool activationStatus;

    private void Start()
    {
        anim = GetComponent<Animator>();
    }

    [ContextMenu("Generate checkpoint id")]
    private void GenerateId()
    {
        id = System.Guid.NewGuid().ToString();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        var player = collision.GetComponentInParent<Player>();
        if (Rpg.Networking.EntityRuntimePermissions.CanUseLegacyActions(player))
        {
            ActiveCheckpoint();
        }
    }

    public void ActiveCheckpoint()
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanUseLegacyWorld(this)) return;
        if (!activationStatus)
            AudioManager.instance.PlaySFX(3, transform);

        activationStatus = true;
        anim.SetBool("active", true);

    }
}
