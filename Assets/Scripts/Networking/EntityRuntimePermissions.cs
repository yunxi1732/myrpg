using UnityEngine;

namespace Rpg.Networking
{
    /// <summary>Shared runtime gates, separate from protocol mapping and local menu/input locks.</summary>
    public static class EntityRuntimePermissions
    {
        public static bool NetworkActive => RoomSceneCoordinator.ProtectSaves || GameSimulationClock.IsNetwork;
        // Room actors are deliberately bound/configured while inactive, before Awake/Start.
        // Their children must also retain the assigned role during staging and cleanup.
        public static NetworkEntityIdentity Identity(Component target) => target != null ? target.GetComponentInParent<NetworkEntityIdentity>(true) : null;
        public static bool CanSimulate(Component target)
        {
            if (target == null) return false;
            var identity = Identity(target);
            return identity != null ? identity.IsAuthoritative : !NetworkActive;
        }
        public static bool CanCollectLocalInput(Component target)
        {
            if (target == null) return false;
            var identity = Identity(target);
            return identity != null ? identity.IsLocalInputOwner : !NetworkActive && target.GetComponentInParent<Player>() != null;
        }
        public static bool CanReadSimulationInput(Component target) => CanSimulate(target) && CanCollectLocalInput(target) && Identity(target)?.MovementHeld != true;
        // Existing skill/inventory/save paths use single-player global managers. Network adaptations come later.
        public static bool CanUseLegacyWorld(Component target) => target != null && !NetworkActive && Identity(target) == null;
        public static bool CanUseLegacyActions(Component target) => CanUseLegacyWorld(target) && CanCollectLocalInput(target);
        public static bool CanPersist(Component target) => CanUseLegacyWorld(target);
        public static bool CanDamage(CharaterStats source, CharaterStats target)
        {
            if (!CanSimulate(source) || !CanSimulate(target)) return false;
            if ((NetworkActive || Identity(source) != null || Identity(target) != null) && source is PlayerStats && target is PlayerStats) return false;
            return true;
        }
    }
}
