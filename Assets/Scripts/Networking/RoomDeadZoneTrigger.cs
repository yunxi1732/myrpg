using UnityEngine;

namespace Rpg.Networking
{
    public sealed class RoomDeadZoneTrigger : MonoBehaviour
    {
        private RoomSceneCoordinator room;
        private DeadZone zone;

        public void Bind(RoomSceneCoordinator owner, DeadZone source)
        {
            room = owner;
            zone = source;
        }

        private void OnTriggerEnter2D(Collider2D collision) => room.TryKillFromDeadZone(zone, collision);
    }
}
