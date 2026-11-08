using System;
using UnityEngine;

namespace Rpg.Networking
{
    public enum NetworkEntityRole { Unassigned, Authoritative, Display }

    /// <summary>Reusable entity role and 2D physics ownership. Assigned by room registration, independently of local player input.</summary>
    [DisallowMultipleComponent]
    public sealed class NetworkEntityIdentity : MonoBehaviour
    {
        private RoomPresentationContext context;
        private RoomEntityPresentation presentation;
        private Rigidbody2D[] bodies;
        private bool[] originalSimulation;
        private RigidbodyConstraints2D[] originalConstraints;
        private float[] originalGravity;
        private RigidbodyInterpolation2D[] originalInterpolation;
        private Collider2D[] colliders;
        private bool[] originalColliders;
        private Animator[] animators;
        private bool[] originalRootMotion;
        private AnimatorUpdateMode[] originalAnimatorMode;
        private bool suppressed;
        private bool retired;
        private RemotePlayerView display;
        public bool MovementHeld { get; private set; }
        public bool IsBound => context != null && presentation != null && presentation.IsValid && presentation.View == transform && HasLiveLifetime;
        private bool HasLiveLifetime
        {
            get
            {
                var lifetime = GetComponent<RoomNetworkObject>();
                return lifetime == null || lifetime.IsAssigned;
            }
        }
        public string RoomSessionId => IsBound ? presentation.RoomSessionId : null;
        public int PlayerId => IsBound ? presentation.PlayerId : 0;
        public int EntityId => IsBound ? presentation.EntityId : 0;
        public NetworkEntityRole Role => !IsBound ? NetworkEntityRole.Unassigned :
            context.IsGuest ? NetworkEntityRole.Display : NetworkEntityRole.Authoritative;
        public bool IsAuthoritative => Role == NetworkEntityRole.Authoritative;
        // B owns local input collection, even though its entity never runs authoritative gameplay.
        public bool IsLocalInputOwner => IsBound && PlayerId > 0 && PlayerId == context.LocalPlayerId;
        public bool CanCollectMovementInput => IsLocalInputOwner && presentation.IsReady;

        internal void HoldMovement()
        {
            MovementHeld = true;
            foreach (Rigidbody2D body in bodies)
                if (body != null) { body.velocity = Vector2.zero; body.angularVelocity = 0; body.constraints = RigidbodyConstraints2D.FreezeAll; }
        }

        internal void ReleaseAuthoritativeMovement()
        {
            // B's displays retain disabled physics; only A releases the two authoritative bodies.
            if (!MovementHeld || !IsAuthoritative) return;
            for (int i = 0; i < bodies.Length; i++)
                if (bodies[i] != null) bodies[i].constraints = originalConstraints[i];
            MovementHeld = false;
        }

        internal void Bind(RoomPresentationContext owner, RoomEntityPresentation entity)
        {
            if (retired) throw new ObjectDisposedException(nameof(NetworkEntityIdentity), "Create a fresh entity for a new session identity.");
            if (IsBound && (!ReferenceEquals(context, owner) || !ReferenceEquals(presentation, entity)))
                throw new InvalidOperationException("Entity is already bound to another room identity.");
            Detach(); context = owner; presentation = entity;
            presentation.Changed += OnPresentationChanged;
            ApplyRuntimeRole();
        }

        private void OnPresentationChanged() { if (!IsBound) Unbind(); }
        private void OnEnable() => ApplyRuntimeRole();
        private void ApplyRuntimeRole()
        {
            if (bodies == null)
            {
                bodies = GetComponentsInChildren<Rigidbody2D>(true);
                originalSimulation = new bool[bodies.Length];
                originalConstraints = new RigidbodyConstraints2D[bodies.Length];
                originalGravity = new float[bodies.Length];
                originalInterpolation = new RigidbodyInterpolation2D[bodies.Length];
                for (int i = 0; i < bodies.Length; i++)
                {
                    originalSimulation[i] = bodies[i].simulated;
                    originalConstraints[i] = bodies[i].constraints;
                    originalGravity[i] = bodies[i].gravityScale;
                    originalInterpolation[i] = bodies[i].interpolation;
                }
                // Also includes child/static trigger colliders without a Rigidbody2D of their own.
                colliders = GetComponentsInChildren<Collider2D>(true);
                originalColliders = new bool[colliders.Length];
                for (int i = 0; i < colliders.Length; i++) originalColliders[i] = colliders[i].enabled;
                animators = GetComponentsInChildren<Animator>(true);
                originalRootMotion = new bool[animators.Length];
                originalAnimatorMode = new AnimatorUpdateMode[animators.Length];
                for (int i = 0; i < animators.Length; i++)
                { originalRootMotion[i] = animators[i].applyRootMotion; originalAnimatorMode[i] = animators[i].updateMode; }
            }
            if (IsAuthoritative)
            {
                if (display == null) display = GetComponent<RemotePlayerView>();
                if (display != null) display.enabled = false;
                if (!suppressed) return;
                for (int i = 0; i < bodies.Length; i++) if (bodies[i] != null)
                {
                    bodies[i].simulated = originalSimulation[i];
                    bodies[i].gravityScale = originalGravity[i];
                    bodies[i].interpolation = originalInterpolation[i];
                }
                for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null) colliders[i].enabled = originalColliders[i];
                for (int i = 0; i < animators.Length; i++) if (animators[i] != null)
                { animators[i].applyRootMotion = originalRootMotion[i]; animators[i].updateMode = originalAnimatorMode[i]; }
                suppressed = false;
                return;
            }
            suppressed = true;
            foreach (Rigidbody2D body in bodies)
            {
                if (body == null) continue;
                body.velocity = Vector2.zero; body.angularVelocity = 0;
                body.gravityScale = 0; body.interpolation = RigidbodyInterpolation2D.None; body.simulated = false;
            }
            // No presentation-query colliders are retained: displays cannot push, trigger damage/pickups or appear in physics queries.
            foreach (Collider2D collider in colliders) if (collider != null) collider.enabled = false;
            foreach (Animator animator in animators)
            {
                if (animator == null) continue;
                animator.applyRootMotion = false;
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            }
            // These own gameplay work. Presentation components and their animations remain enabled.
            foreach (Entity entity in GetComponentsInChildren<Entity>(true)) { entity.CancelInvoke(); entity.StopAllCoroutines(); }
            foreach (CharaterStats stats in GetComponentsInChildren<CharaterStats>(true)) { stats.CancelInvoke(); stats.StopAllCoroutines(); }
            if (display == null) display = GetComponent<RemotePlayerView>();
            if (Role == NetworkEntityRole.Display)
            {
                if (display == null) display = gameObject.AddComponent<RemotePlayerView>();
                display.Bind(this);
                display.enabled = true;
            }
            else if (display != null) display.enabled = false;
        }

        private void Detach()
        {
            if (presentation != null) presentation.Changed -= OnPresentationChanged;
        }
        internal void Unbind()
        {
            retired = true;
            Detach(); context = null; presentation = null;
            ApplyRuntimeRole(); // An expired network entity must not fall back to single-player simulation.
        }
        private void OnDestroy()
        {
            var owner = context;
            int id = EntityId;
            Detach(); context = null; presentation = null;
            if (id > 0) owner?.RemoveEntity(id);
        }
    }
}
