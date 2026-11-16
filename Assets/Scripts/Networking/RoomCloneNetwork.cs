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
            public ulong StartTick;
            public bool Published;
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
        private readonly Action<CloneInstanceEventDto> publishEvent;
        private readonly Func<long> currentLoadId;
        private readonly Dictionary<long, Clone> clones = new Dictionary<long, Clone>();
        private long stateSeq, appliedSeq;
        private ulong appliedTick;
        private bool disposed;

        public RoomCloneNetwork(RoomPresentationContext context, RoomPlayerEntities players, RoomNetworkObjects objects,
            CloneSkill template, RoomCrystalNetwork crystals, RoomCombatDamage damage,
            AuthorityResultSequence results, Func<int> allocateEntity, Action<CloneStatesDto> publish,
            Action<CloneInstanceEventDto> publishEvent, Func<long> currentLoadId)
        {
            this.context = context; this.players = players; this.objects = objects; this.template = template;
            this.crystals = crystals; this.damage = damage;
            this.results = results; this.allocateEntity = allocateEntity; this.publish = publish;
            this.publishEvent = publishEvent; this.currentLoadId = currentLoadId;
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
            var clone = clones[state.skillInstanceId];
            clone.Multiplier = multiplier; clone.Duplicate = duplicate;
            clone.StartTick = GameSimulationClock.AuthorityTick;
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
            clones.Add(state.skillInstanceId, clone);
            value.GetComponent<CloneSkillController>().Room = this;
            if (context.IsGuest)
            {
                value.GetComponent<CloneSkillController>().enabled = false;
                foreach (var collider in value.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
                value.GetComponent<Animator>().SetInteger("AttackNumber", state.attackNumber);
            }
            clone.Lifetime = objects.Register(context.RoomSessionId, state.sourcePlayerId, value,
                () =>
                {
                    clones.Remove(state.skillInstanceId);
                    if (!context.IsGuest && !disposed && clone.Published) PublishInstanceEvent(clone, CloneInstanceEventKind.End);
                    clone.Attack?.Close();
                });
        }

        public void Publish()
        {
            if (disposed || context.IsGuest || GameSimulationClock.AuthorityTick == 0) return;
            foreach (var clone in new List<Clone>(clones.Values))
                if (clone.Object == null) clone.Lifetime.Dispose();
            var states = new List<CloneTrajectoryDto>();
            foreach (var clone in clones.Values)
            {
                Vector2 position = clone.Object.transform.position;
                clone.State.position = new[] { position.x, position.y };
                clone.State.facingLeft = clone.Object.transform.eulerAngles.y > 90;
                clone.State.alpha = clone.Object.GetComponent<SpriteRenderer>().color.a;
                if (!clone.Published) { PublishInstanceEvent(clone, CloneInstanceEventKind.Spawn); clone.Published = true; }
                states.Add(new CloneTrajectoryDto { skillInstanceId = clone.State.skillInstanceId,
                    position = clone.State.position, facingLeft = clone.State.facingLeft, alpha = clone.State.alpha });
            }
            publish(new CloneStatesDto { roomSessionId = context.RoomSessionId,
                authorityTick = GameSimulationClock.AuthorityTick, resultId = results.Allocate(),
                stateSeq = ++stateSeq, clones = states.ToArray() });
        }

        public void ReplayActiveInstances()
        {
            foreach (var clone in clones.Values) PublishInstanceEvent(clone, CloneInstanceEventKind.Spawn);
        }

        private void PublishInstanceEvent(Clone clone, CloneInstanceEventKind kind)
        {
            var state = clone.State;
            Vector2 position = clone.Object != null ? clone.Object.transform.position :
                new Vector2(state.position[0], state.position[1]);
            var value = new CloneInstanceEventDto { roomSessionId = context.RoomSessionId, loadId = currentLoadId(),
                authorityTick = GameSimulationClock.AuthorityTick, resultId = results.Allocate(), requestSeq = 0,
                skillInstanceId = state.skillInstanceId, entityId = state.entityId,
                sourcePlayerId = state.sourcePlayerId, sourceEntityId = state.sourceEntityId,
                startTick = clone.StartTick, kind = kind, position = new[] { position.x, position.y },
                facingLeft = clone.Object != null ? clone.Object.transform.eulerAngles.y > 90 : state.facingLeft,
                attackNumber = state.attackNumber };
            publishEvent(value);
            Debug.Log($"[RoomClone] A {kind} instance={value.skillInstanceId} action={value.attackNumber} resultId={value.resultId}");
        }

        public void ApplyEvent(CloneInstanceEventDto value)
        {
            if (disposed || !context.IsGuest || value.roomSessionId != context.RoomSessionId) return;
            if (!clones.TryGetValue(value.skillInstanceId, out var clone))
            {
                if (value.kind != CloneInstanceEventKind.Spawn)
                    throw new InvalidOperationException("Clone end arrived without its spawn event.");
                var state = new CloneObjectDto { entityId = value.entityId, sourcePlayerId = value.sourcePlayerId,
                    sourceEntityId = value.sourceEntityId, skillInstanceId = value.skillInstanceId,
                    attackNumber = value.attackNumber };
                Register(UnityEngine.Object.Instantiate(template.ClonePrefab), state);
                clone = clones[value.skillInstanceId];
                clone.StartTick = value.startTick;
            }
            if (clone.State.entityId != value.entityId || clone.State.sourcePlayerId != value.sourcePlayerId ||
                clone.State.sourceEntityId != value.sourceEntityId || clone.StartTick != value.startTick ||
                clone.State.attackNumber != value.attackNumber)
                throw new InvalidOperationException("Clone metadata changed within an instance.");
            if (value.kind == CloneInstanceEventKind.End)
                clone.Lifetime.Dispose();
            else
            {
                clone.Object.transform.position = new Vector2(value.position[0], value.position[1]);
                clone.Object.transform.rotation = Quaternion.Euler(0, value.facingLeft ? 180 : 0, 0);
            }
            Debug.Log($"[RoomClone] B {value.kind} instance={value.skillInstanceId} action={value.attackNumber} resultId={value.resultId}");
        }

        public void Apply(CloneStatesDto value)
        {
            if (disposed || !context.IsGuest || value.roomSessionId != context.RoomSessionId ||
                value.stateSeq <= appliedSeq || value.authorityTick < appliedTick) return;
            appliedSeq = value.stateSeq; appliedTick = value.authorityTick;
            foreach (var state in value.clones)
            {
                if (!clones.TryGetValue(state.skillInstanceId, out var clone)) continue;
                clone.Object.transform.position = new Vector2(state.position[0], state.position[1]);
                clone.Object.transform.rotation = Quaternion.Euler(0, state.facingLeft ? 180 : 0, 0);
                var sprite = clone.Object.GetComponent<SpriteRenderer>();
                var color = sprite.color; color.a = state.alpha; sprite.color = color;
            }
        }

        public void Dispose()
        {
            disposed = true;
            foreach (var clone in new List<Clone>(clones.Values)) clone.Lifetime.Dispose();
        }
        public void RemovePlayer(int playerId)
        {
            foreach (var clone in new List<Clone>(clones.Values))
                if (clone.State.sourcePlayerId == playerId) clone.Lifetime.Dispose();
        }
    }
}
