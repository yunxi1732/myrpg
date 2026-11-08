using UnityEngine;

namespace Rpg.Networking
{
    /// <summary>Removes externally destroyed objects from their room registry as well.</summary>
    [DisallowMultipleComponent]
    public sealed class RoomNetworkObject : MonoBehaviour
    {
        private RoomNetworkObjects owner;
        private long registrationKey;
        public string RoomSessionId => owner?.RoomSessionId;
        public int OwnerPlayerId { get; private set; }
        public bool IsAssigned => owner != null && owner.AllowsOwner(OwnerPlayerId);
        public bool IsRetired { get; private set; }
        internal bool BelongsTo(RoomNetworkObjects scope) => ReferenceEquals(owner, scope);

        internal void Assign(RoomNetworkObjects scope, long key, int playerId)
        { owner = scope; registrationKey = key; OwnerPlayerId = playerId; }
        internal void Retire() { IsRetired = true; owner = null; OwnerPlayerId = 0; }
        private void OnDestroy()
        {
            var scope = owner;
            Retire();
            scope?.Release(registrationKey, false);
        }
    }
}
