using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rpg.Networking
{
    /// <summary>Current A-world melee geometry, shared by player and monster hit windows. No damage or RNG.</summary>
    public static class AuthorityMeleeQuery
    {
        public static void Collect<T>(Entity source, Func<T, bool> validTarget, Action<T, Collider2D, int> accept) where T : Entity
        {
            var owner = source != null ? EntityRuntimePermissions.Identity(source) : null;
            if (source == null || !source.isActiveAndEnabled || owner == null || !owner.IsAuthoritative ||
                source.attackCheck == null || !source.attackCheck.IsChildOf(source.transform) ||
                !(source.attackCheckRadius > 0) || float.IsInfinity(source.attackCheckRadius) || validTarget == null || accept == null) return;
            var physics = source.gameObject.scene.GetPhysicsScene2D();
            var overlaps = new List<Collider2D>();
            physics.OverlapCircle(source.attackCheck.position, source.attackCheckRadius, new ContactFilter2D { useTriggers = false }, overlaps);
            Vector2 origin = source.transform.position, center = source.attackCheck.position;
            var walls = new List<RaycastHit2D>();
            var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = source.AttackBlockingLayers };
            foreach (var collider in overlaps)
            {
                if (collider == null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy ||
                    collider.gameObject.scene != source.gameObject.scene) continue;
                T target = collider.GetComponentInParent<T>();
                var identity = target != null ? EntityRuntimePermissions.Identity(target) : null;
                if (target == null || target == source || !target.isActiveAndEnabled || identity == null || !identity.IsAuthoritative ||
                    identity.RoomSessionId != owner.RoomSessionId || !validTarget(target) ||
                    (target.transform.position.x - origin.x) * source.facingDir < 0) continue;
                if ((collider.ClosestPoint(center) - center).sqrMagnitude > source.attackCheckRadius * source.attackCheckRadius + .000001f) continue;
                walls.Clear(); physics.Linecast(origin, collider.ClosestPoint(origin), filter, walls);
                bool blocked = false;
                foreach (var wall in walls)
                {
                    var entity = wall.collider != null ? wall.collider.GetComponentInParent<Entity>() : null;
                    if (wall.collider == null || entity == source || entity == target) continue;
                    blocked = true; break;
                }
                if (!blocked) accept(target, collider, identity.EntityId);
            }
        }
    }
}
