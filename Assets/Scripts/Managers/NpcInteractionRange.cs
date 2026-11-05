using System.Collections.Generic;
using UnityEngine;

/// <summary>Receives physics callbacks on the independent trigger child, not on the NPC root.</summary>
[RequireComponent(typeof(Collider2D))]
public sealed class NpcInteractionRange : MonoBehaviour
{
    [SerializeField] private NpcInteractable owner;
    private readonly HashSet<Collider2D> contacts = new HashSet<Collider2D>();
    private Collider2D range;
    public int ContactCount => contacts.Count;

    private void Awake() => range = GetComponent<Collider2D>();
    private bool CanTrack => owner != null && owner.isActiveAndEnabled && range != null &&
        owner.interactionRange == range && range.enabled && range.isTrigger;

    private void OnTriggerEnter2D(Collider2D other) => Track(other);
    // Recover when enabling the NPC component while the player is already inside.
    private void OnTriggerStay2D(Collider2D other) => Track(other);

    private void Track(Collider2D other)
    {
        if (!isActiveAndEnabled || !CanTrack || !owner.IsPlayerCollider(other)) return;
        if (contacts.Add(other)) owner.SetPlayerInRange(true);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (contacts.Remove(other) && owner != null)
            owner.SetPlayerInRange(contacts.Count > 0);
    }

    private void FixedUpdate()
    {
        if (!CanTrack) { ClearContacts(); return; }
        // Disabling/destroying a player collider is not guaranteed to deliver an exit callback.
        if (contacts.RemoveWhere(collider => !owner.IsPlayerCollider(collider)) > 0)
            owner.SetPlayerInRange(contacts.Count > 0);
    }

    public void ClearContacts()
    {
        contacts.Clear();
        if (owner != null) owner.SetPlayerInRange(false);
    }

    private void OnDisable() => ClearContacts();
    private void OnDestroy() => ClearContacts();
}
