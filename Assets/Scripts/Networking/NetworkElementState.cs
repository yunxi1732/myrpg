using Rpg.Networking.Protocol;
using UnityEngine;

namespace Rpg.Networking
{
    /// <summary>Shared per-entity element application/cleanup. Only A changes movement; expiry belongs to the room tick service.</summary>
    [DisallowMultipleComponent]
    public sealed class NetworkElementState : MonoBehaviour
    {
        public ElementEffectKind? ActiveKind { get; private set; }
        public float MovementMultiplier { get; private set; } = 1;
        public float ArmorMultiplier { get; private set; } = 1;
        public float NormalAnimationSpeed => baseAnimationSpeed * (ActiveKind == ElementEffectKind.Chill ? MovementMultiplier : 1);
        public float PlaybackAnimationSpeed
        {
            get
            {
                var p = GetComponent<Player>(); var s = GetComponent<EnemySkeleton>();
                bool authority = GetComponent<NetworkEntityIdentity>()?.IsAuthoritative == true;
                return authority && (p != null && p.stateMachine?.currentState != null && p.stateMachine.currentState == p.primaryAttackState ||
                    s != null && s.NetworkAiState == MonsterAiState.Attack) ? 1 : NormalAnimationSpeed;
            }
        }
        private float move, jump, dash, baseAnimationSpeed = 1;
        private bool changedMovement;
        private Animator animator;
        private Player player;
        private Enemy enemy;

        public void ApplyConfirmed(ElementStateDto state)
        {
            ElementEffectKind? kind = state != null && state.active ? state.kind : (ElementEffectKind?)null;
            if (ActiveKind == kind) return; // Repeated snapshots never multiply movement parameters or restart particles.
            Restore();
            if (!kind.HasValue) return;
            animator = GetComponentInChildren<Animator>(true);
            baseAnimationSpeed = animator != null && animator.speed > 0 ? animator.speed : 1;
            ActiveKind = kind;
            MovementMultiplier = state.movementMultiplier; ArmorMultiplier = state.armorMultiplier;
            var stats = GetComponent<CharaterStats>();
            if (stats != null) { stats.SetConfirmedBurn(kind == ElementEffectKind.Burn); stats.isChilled = kind == ElementEffectKind.Chill; stats.isShocked = kind == ElementEffectKind.Shock; }
            if (kind == ElementEffectKind.Chill && GetComponent<NetworkEntityIdentity>()?.IsAuthoritative == true)
            {
                player = GetComponent<Player>(); enemy = GetComponent<Enemy>();
                if (player != null)
                {
                    move = player.moveSpeed; jump = player.jumpForce; dash = player.dashSpeed;
                    player.moveSpeed = move * MovementMultiplier; player.jumpForce = jump * MovementMultiplier; player.dashSpeed = dash * MovementMultiplier;
                }
                else if (enemy != null) { move = enemy.moveSpeed; enemy.moveSpeed = move * MovementMultiplier; }
                changedMovement = true;
            }
            if (animator != null && animator.speed > 0) animator.speed = PlaybackAnimationSpeed;
            GetComponentInChildren<EntityFX>(true)?.SetConfirmedElement(kind);
        }

        private void Restore()
        {
            if (!ActiveKind.HasValue) return;
            if (changedMovement)
            {
                if (player != null) { player.moveSpeed = move; player.jumpForce = jump; player.dashSpeed = dash; }
                else if (enemy != null) enemy.moveSpeed = move;
            }
            ActiveKind = null; MovementMultiplier = ArmorMultiplier = 1; changedMovement = false;
            var stats = GetComponent<CharaterStats>();
            if (stats != null) { stats.SetConfirmedBurn(false); stats.isChilled = false; stats.isShocked = false; }
            if (animator != null && animator.speed > 0) animator.speed = PlaybackAnimationSpeed;
            GetComponentInChildren<EntityFX>(true)?.SetConfirmedElement(null);
        }
        private void OnDisable() => Restore();
    }
}
