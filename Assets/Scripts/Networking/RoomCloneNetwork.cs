using System;
using System.Collections.Generic;
using UnityEngine;
using Rpg.Networking.Protocol;

namespace Rpg.Networking
{
    public sealed class RoomCloneNetwork : IDisposable
    {
        private sealed class Clone
        {
            public CloneObjectDto State;
            public GameObject Object;
            public RoomNetworkObjects.Registration Lifetime;
            public PrimaryAttackInstance Attack;
            public float Multiplier;
            public bool Duplicate;
        }

        private readonly RoomPresentationContext context;
        private readonly RoomPlayerEntities players;
        private readonly RoomNetworkObjects objects;
        private readonly CloneSkill template;
        private readonly RoomCrystalNetwork crystals;
        private readonly RoomCombatDamage damage;
        private readonly AuthorityResultSequence results;
        private readonly Func<int> allocateEntity;
        private readonly Action<CloneStatesDto> publish;
        private readonly Dictionary<int, Clone> clones = new Dictionary<int, Clone>();
        private long stateSeq, appliedSeq;
        private ulong appliedTick;
        private bool disposed;

        public RoomCloneNetwork(RoomPresentationContext context, RoomPlayerEntities players, RoomNetworkObjects objects,
            CloneSkill template, RoomCrystalNetwork crystals, RoomCombatDamage damage,
            AuthorityResultSequence results, Func<int> allocateEntity, Action<CloneStatesDto> publish)
        {
            this.context = context; this.players = players; this.objects = objects; this.template = template;
            this.crystals = crystals; this.damage = damage;
            this.results = results; this.allocateEntity = allocateEntity; this.publish = publish;
        }

        public void EnsurePlayers()
        {
            foreach (var player in players.Entities.Values) player.RoomClones = this;
        }

        public void Spawn(Player owner) => SpawnAt(owner, owner.transform, Vector3.zero);

        public void SpawnAt(Player owner, Transform origin, Vector3 offset, bool blackholeAttack = false)
        {
            if (disposed || context.IsGuest) return;
            if (owner.Combat.IsRoomNodeUnlocked("Crystal mirage"))
            {
                crystals.SpawnFromClone(owner, origin, offset);
                return;
            }
            bool cloneAttack = owner.Combat.Skills.Get<CloneSkill>().Unlocked;
            bool attacking = cloneAttack || blackholeAttack;
            bool aggressive = owner.Combat.IsRoomNodeUnlocked("Aggressive mirage");
            bool duplicate = owner.Combat.IsRoomNodeUnlocked("Multiple mirage");
            float multiplier = duplicate ? template.RoomMultipleMultiplier : aggressive ? template.RoomAggressiveMultiplier :
                cloneAttack ? template.RoomBaseMultiplier : 0;
            var instance = UnityEngine.Object.Instantiate(template.ClonePrefab);
            instance.transform.position = origin.position + offset;
            var target = template.FindRoomTarget(instance.transform);
            var controller = instance.GetComponent<CloneSkillController>();
            controller.SetupClone(origin, template.CloneDuration, attacking, offset, target,
                duplicate, template.RoomDuplicateChance, owner, multiplier);
            var state = new CloneObjectDto { entityId = allocateEntity(), sourcePlayerId = owner.Combat.PlayerId,
                sourceEntityId = owner.Combat.EntityId, skillInstanceId = objects.AllocatePrimaryAttackInstanceId(),
                attackNumber = controller.AttackNumber };
            Register(instance, state);
            var clone = clones[state.entityId];
            clone.Multiplier = multiplier; clone.Duplicate = duplicate;
            if (attacking)
            {
                ulong tick = GameSimulationClock.AuthorityTick;
                int duration = Mathf.Max(1, Mathf.CeilToInt(template.CloneDuration / GameSimulationClock.FixedDeltaTime));
                clone.Attack = new PrimaryAttackInstance(context.RoomSessionId, state.sourcePlayerId, state.sourceEntityId,
                    state.skillInstanceId, 0, tick, checked(tick + (ulong)duration),
                    new PrimaryAttackTiming(0, duration, new PrimaryHitSegmentTiming(0, duration)));
            }
            Publish();
        }

        public void Hit(CloneSkillController controller, Enemy enemy)
        {
            foreach (var clone in clones.Values)
            {
                if (clone.Object.GetComponent<CloneSkillController>() != controller) continue;
                if (!damage.OnSkillHit(clone.Attack, enemy, multiplier: clone.Multiplier)) return;
                if (clone.Duplicate && UnityEngine.Random.Range(0, 100) < template.RoomDuplicateChance)
                {
                    var owner = players.Entities[clone.State.sourceEntityId];
                    float offset = clone.State.facingLeft ? -1.5f : 1.5f;
                    SpawnAt(owner, enemy.transform, new Vector3(offset, 0));
                }
                return;
            }
        }

        private void Register(GameObject value, CloneObjectDto state)
        {
            var clone = new Clone { Object = value, State = state };
            clones.Add(state.entityId, clone);
            value.GetComponent<CloneSkillController>().Room = this;
            if (context.IsGuest)
            {
                value.GetComponent<CloneSkillController>().enabled = false;
                foreach (var collider in value.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
                value.GetComponent<Animator>().SetInteger("AttackNumber", state.attackNumber);
            }
            clone.Lifetime = objects.Register(context.RoomSessionId, state.sourcePlayerId, value,
                () => { clone.Attack?.Close(); clones.Remove(state.entityId); });
        }

        public void Publish()
        {
            if (disposed || context.IsGuest || GameSimulationClock.AuthorityTick == 0) return;
            foreach (var clone in new List<Clone>(clones.Values))
                if (clone.Object == null) clone.Lifetime.Dispose();
            var states = new List<CloneObjectDto>();
            foreach (var clone in clones.Values)
            {
                Vector2 position = clone.Object.transform.position;
                clone.State.position = new[] { position.x, position.y };
                clone.State.facingLeft = clone.Object.transform.eulerAngles.y > 90;
                clone.State.alpha = clone.Object.GetComponent<SpriteRenderer>().color.a;
                clone.State.attackNumber = clone.Object.GetComponent<CloneSkillController>().AttackNumber;
                states.Add(clone.State);
            }
            publish(new CloneStatesDto { roomSessionId = context.RoomSessionId,
                authorityTick = GameSimulationClock.AuthorityTick, resultId = results.Allocate(),
                stateSeq = ++stateSeq, clones = states.ToArray() });
        }

        public void Apply(CloneStatesDto value)
        {
            if (disposed || !context.IsGuest || value.roomSessionId != context.RoomSessionId ||
                value.stateSeq <= appliedSeq || value.authorityTick < appliedTick) return;
            appliedSeq = value.stateSeq; appliedTick = value.authorityTick;
            var live = new HashSet<int>();
            foreach (var state in value.clones)
            {
                live.Add(state.entityId);
                if (!clones.TryGetValue(state.entityId, out var clone))
                {
                    var instance = UnityEngine.Object.Instantiate(template.ClonePrefab);
                    Register(instance, state);
                    clone = clones[state.entityId];
                }
                clone.Object.transform.position = new Vector2(state.position[0], state.position[1]);
                clone.Object.transform.rotation = Quaternion.Euler(0, state.facingLeft ? 180 : 0, 0);
                var sprite = clone.Object.GetComponent<SpriteRenderer>();
                var color = sprite.color; color.a = state.alpha; sprite.color = color;
            }
            foreach (var clone in new List<Clone>(clones.Values))
                if (!live.Contains(clone.State.entityId)) clone.Lifetime.Dispose();
        }

        public void Dispose()
        {
            disposed = true;
            foreach (var clone in new List<Clone>(clones.Values)) clone.Lifetime.Dispose();
        }
    }
}
