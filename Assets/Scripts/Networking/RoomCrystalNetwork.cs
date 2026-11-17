using System;
using System.Collections.Generic;
using UnityEngine;
using Rpg.Networking.Protocol;

namespace Rpg.Networking
{
    public sealed class RoomCrystalNetwork : IDisposable
    {
        private sealed class Crystal
        {
            public CrystalObjectDto State;
            public GameObject Object;
            public RoomNetworkObjects.Registration Lifetime;
            public bool Derived;
            public PrimaryAttackInstance Attack;
            public long RequestSeq;
            public ulong StartTick;
            public bool Published, PublishedExploding;
        }

        private readonly RoomPresentationContext context;
        private readonly RoomPlayerEntities players;
        private readonly RoomNetworkObjects objects;
        private readonly CrystalSkill template;
        private readonly RoomCombatDamage damage;
        public RoomCloneNetwork Clones { private get; set; }
        public bool IsAuthoritative => !context.IsGuest;
        private readonly AuthorityResultSequence results;
        private readonly Func<int> allocateEntity;
        private readonly Action<CrystalStatesDto> publish;
        private readonly Action<CrystalInstanceEventDto> publishEvent;
        private readonly Func<long> currentLoadId;
        private readonly Action<int, int, long, SkillRequestRejection> reject;
        private readonly Dictionary<long, Crystal> crystals = new Dictionary<long, Crystal>();
        private readonly Dictionary<int, long> requests = new Dictionary<int, long>();
        private readonly Dictionary<int, (int remaining, ulong deadline)> stacks = new Dictionary<int, (int, ulong)>();
        private long stateSeq, appliedSeq;
        private ulong appliedTick;
        private bool disposed;

        public RoomCrystalNetwork(RoomPresentationContext context, RoomPlayerEntities players, RoomNetworkObjects objects,
            CrystalSkill template, RoomCombatDamage damage, AuthorityResultSequence results, Func<int> allocateEntity,
            Action<CrystalStatesDto> publish, Action<CrystalInstanceEventDto> publishEvent, Func<long> currentLoadId,
            Action<int, int, long, SkillRequestRejection> reject = null)
        {
            this.context = context; this.players = players; this.objects = objects; this.template = template;
            this.damage = damage;
            this.results = results; this.allocateEntity = allocateEntity; this.publish = publish;
            this.publishEvent = publishEvent; this.currentLoadId = currentLoadId;
            this.reject = reject;
        }

        public void Request(RoomPlayerIdentity actor, CrystalInputDto input)
        {
            if (disposed || context.IsGuest || input.roomSessionId != context.RoomSessionId ||
                requests.TryGetValue(actor.PlayerId, out long last) && input.actionSeq <= last) return;
            requests[actor.PlayerId] = input.actionSeq;
            var player = players.Entities[actor.EntityId];
            if (player.NetworkInputLocked || player.IsLocalInputOwner && GameplayControlLock.IsActionInputBlocked)
            { Reject(actor, input.actionSeq, SkillRequestRejection.InputLocked); return; }
            if (player.stats.currentHealth <= 0)
            { Reject(actor, input.actionSeq, SkillRequestRejection.Dead); return; }
            if (player.isBusy)
            { Reject(actor, input.actionSeq, SkillRequestRejection.Busy); return; }
            if (!player.Combat.Skills.Get<CrystalSkill>().Unlocked)
            { Reject(actor, input.actionSeq, SkillRequestRejection.NotUnlocked); return; }
            bool exploding = player.Combat.IsRoomNodeUnlocked("Explosion");
            bool moving = player.Combat.IsRoomNodeUnlocked("Controlled destruction");
            var slot = player.Combat.Skills.Get<CrystalSkill>();
            if (player.Combat.IsRoomNodeUnlocked("Multiple destruction"))
            {
                if (slot.Remaining > 0) { Reject(actor, input.actionSeq, SkillRequestRejection.Cooldown); return; }
                if (!stacks.TryGetValue(actor.PlayerId, out var stack))
                {
                    slot.Configure(template.RoomStackCooldown);
                    stack = (template.RoomStackCount, checked(GameSimulationClock.AuthorityTick +
                        (ulong)Mathf.CeilToInt(template.RoomStackWindow / GameSimulationClock.FixedDeltaTime)));
                }
                Spawn(player, player.transform.position, exploding, moving, false, input.actionSeq);
                stack.remaining--;
                if (stack.remaining == 0) { stacks.Remove(actor.PlayerId); player.Combat.Skills.Commit<CrystalSkill>(); }
                else stacks[actor.PlayerId] = stack;
                return;
            }
            Crystal existing = null;
            foreach (var value in crystals.Values)
                if (!value.Derived && value.State.sourcePlayerId == actor.PlayerId) { existing = value; break; }
            if (existing != null)
            {
                if (moving || slot.Remaining > 0)
                { Reject(actor, input.actionSeq, moving ? SkillRequestRejection.Busy : SkillRequestRejection.Cooldown); return; }
                Vector3 position = player.transform.position;
                player.transform.position = existing.Object.transform.position;
                existing.Object.transform.position = position;
                if (player.Combat.IsRoomNodeUnlocked("Mirage blink")) Clones.SpawnAt(player, existing.Object.transform, Vector3.zero);
                existing.Object.GetComponent<CrystalSkillController>().FinishCrystal();
                if (existing.Attack == null) existing.Lifetime.Dispose();
                Publish();
                return;
            }
            if (!player.Combat.Skills.Commit<CrystalSkill>())
            { Reject(actor, input.actionSeq, SkillRequestRejection.Cooldown); return; }
            Spawn(player, player.transform.position, exploding, moving, false, input.actionSeq);
        }
        private void Reject(RoomPlayerIdentity actor, long requestSeq, SkillRequestRejection reason)
            => reject?.Invoke(actor.PlayerId, actor.EntityId, requestSeq, reason);

        public void Advance()
        {
            ulong tick = GameSimulationClock.AuthorityTick;
            foreach (var pair in new List<KeyValuePair<int, (int remaining, ulong deadline)>>(stacks))
                if (tick >= pair.Value.deadline)
                {
                    stacks.Remove(pair.Key);
                    foreach (var player in players.Entities.Values)
                        if (player.Combat.PlayerId == pair.Key) { player.Combat.Skills.Commit<CrystalSkill>(); break; }
                }
        }

        private void Spawn(Player owner, Vector3 position, bool exploding, bool moving, bool derived, long requestSeq)
        {
            var crystal = UnityEngine.Object.Instantiate(template.CrystalPrefab, position, Quaternion.identity);
            var target = moving ? template.FindRoomTarget(crystal.transform) : null;
            var controller = crystal.GetComponent<CrystalSkillController>();
            controller.SetUpCrystal(template.CrystalDuration, exploding, moving, template.RoomMoveSpeed, target, owner);
            var state = new CrystalObjectDto { entityId = allocateEntity(), sourcePlayerId = owner.Combat.PlayerId,
                sourceEntityId = owner.Combat.EntityId, skillInstanceId = objects.AllocatePrimaryAttackInstanceId() };
            Register(crystal, state);
            var registered = crystals[state.skillInstanceId];
            registered.Derived = derived;
            registered.RequestSeq = requestSeq;
            registered.StartTick = GameSimulationClock.AuthorityTick;
            if (exploding)
            {
                ulong tick = GameSimulationClock.AuthorityTick;
                int duration = Mathf.CeilToInt((template.CrystalDuration + 1) / GameSimulationClock.FixedDeltaTime);
                registered.Attack = new PrimaryAttackInstance(context.RoomSessionId, state.sourcePlayerId, state.sourceEntityId,
                    state.skillInstanceId, 0, tick, checked(tick + (ulong)duration),
                    new PrimaryAttackTiming(0, duration, new PrimaryHitSegmentTiming(0, duration)));
            }
            Publish();
        }

        public void SpawnFromClone(Player owner, Transform origin, Vector3 offset)
        {
            Spawn(owner, origin.position + offset, owner.Combat.IsRoomNodeUnlocked("Explosion"), true, true, 0);
        }

        public void Explode(CrystalSkillController controller, float radius)
        {
            foreach (var crystal in crystals.Values)
            {
                if (crystal.Object.GetComponent<CrystalSkillController>() != controller) continue;
                foreach (var hit in Physics2D.OverlapCircleAll(controller.transform.position, radius))
                    if (hit.GetComponent<Enemy>() is Enemy enemy) damage.OnSkillHit(crystal.Attack, enemy, magicOnly: true);
                return;
            }
        }

        private void Register(GameObject value, CrystalObjectDto state)
        {
            var crystal = new Crystal { State = state, Object = value };
            crystals.Add(state.skillInstanceId, crystal);
            value.GetComponent<CrystalSkillController>().Room = this;
            if (context.IsGuest)
            {
                value.GetComponent<CrystalSkillController>().enabled = false;
                value.GetComponent<Collider2D>().enabled = false;
            }
            crystal.Lifetime = objects.Register(context.RoomSessionId, state.sourcePlayerId, value,
                () =>
                {
                    crystals.Remove(state.skillInstanceId);
                    if (!context.IsGuest && !disposed && crystal.Published) PublishInstanceEvent(crystal, CrystalInstanceEventKind.End);
                    crystal.Attack?.Close();
                });
        }

        public void Publish()
        {
            if (disposed || context.IsGuest || GameSimulationClock.AuthorityTick == 0) return;
            foreach (var value in new List<Crystal>(crystals.Values))
                if (value.Object == null) value.Lifetime.Dispose();
            var states = new List<CrystalTrajectoryDto>();
            foreach (var value in crystals.Values)
            {
                Vector2 position = value.Object.transform.position;
                value.State.position = new[] { position.x, position.y };
                value.State.scale = value.Object.transform.localScale.x;
                bool exploding = value.Object.GetComponent<Animator>().GetBool("Explode");
                if (!value.Published) { PublishInstanceEvent(value, CrystalInstanceEventKind.Spawn); value.Published = true; }
                if (exploding && !value.PublishedExploding)
                    PublishInstanceEvent(value, CrystalInstanceEventKind.Explode);
                value.PublishedExploding = exploding;
                states.Add(new CrystalTrajectoryDto { skillInstanceId = value.State.skillInstanceId,
                    position = new[] { position.x, position.y }, scale = value.Object.transform.localScale.x });
            }
            publish(new CrystalStatesDto { roomSessionId = context.RoomSessionId,
                authorityTick = GameSimulationClock.AuthorityTick, resultId = results.Allocate(),
                stateSeq = ++stateSeq, crystals = states.ToArray() });
        }

        public void ReplayActiveInstances()
        {
            foreach (var crystal in crystals.Values)
            {
                PublishInstanceEvent(crystal, CrystalInstanceEventKind.Spawn);
                if (crystal.PublishedExploding) PublishInstanceEvent(crystal, CrystalInstanceEventKind.Explode);
            }
        }

        private void PublishInstanceEvent(Crystal crystal, CrystalInstanceEventKind kind)
        {
            var state = crystal.State;
            Vector2 position = crystal.Object != null ? crystal.Object.transform.position :
                new Vector2(state.position[0], state.position[1]);
            var value = new CrystalInstanceEventDto { roomSessionId = context.RoomSessionId, loadId = currentLoadId(),
                authorityTick = GameSimulationClock.AuthorityTick, resultId = results.Allocate(),
                requestSeq = crystal.RequestSeq, skillInstanceId = state.skillInstanceId, entityId = state.entityId,
                sourcePlayerId = state.sourcePlayerId, sourceEntityId = state.sourceEntityId,
                startTick = crystal.StartTick, kind = kind, derived = crystal.Derived,
                position = new[] { position.x, position.y },
                scale = crystal.Object != null ? crystal.Object.transform.localScale.x : state.scale };
            publishEvent(value);
            Debug.Log($"[RoomCrystal] A {kind} instance={value.skillInstanceId} derived={value.derived} resultId={value.resultId}");
        }

        public void ApplyEvent(CrystalInstanceEventDto value)
        {
            if (disposed || !context.IsGuest || value.roomSessionId != context.RoomSessionId) return;
            if (!crystals.TryGetValue(value.skillInstanceId, out var crystal))
            {
                if (value.kind == CrystalInstanceEventKind.End) return;
                if (value.kind != CrystalInstanceEventKind.Spawn)
                    throw new InvalidOperationException("Crystal event arrived before its spawn event.");
                var state = new CrystalObjectDto { entityId = value.entityId, sourcePlayerId = value.sourcePlayerId,
                    sourceEntityId = value.sourceEntityId, skillInstanceId = value.skillInstanceId };
                Register(UnityEngine.Object.Instantiate(template.CrystalPrefab), state);
                crystal = crystals[value.skillInstanceId];
                crystal.StartTick = value.startTick; crystal.RequestSeq = value.requestSeq; crystal.Derived = value.derived;
            }
            if (crystal.State.entityId != value.entityId || crystal.State.sourcePlayerId != value.sourcePlayerId ||
                crystal.State.sourceEntityId != value.sourceEntityId || crystal.StartTick != value.startTick ||
                crystal.RequestSeq != value.requestSeq || crystal.Derived != value.derived)
                throw new InvalidOperationException("Crystal metadata changed within an instance.");
            if (value.kind == CrystalInstanceEventKind.End)
                crystal.Lifetime.Dispose();
            else
            {
                crystal.Object.transform.position = new Vector2(value.position[0], value.position[1]);
                crystal.Object.transform.localScale = Vector3.one * value.scale;
                if (value.kind == CrystalInstanceEventKind.Explode)
                    crystal.Object.GetComponent<Animator>().SetBool("Explode", true);
            }
            Debug.Log($"[RoomCrystal] B {value.kind} instance={value.skillInstanceId} derived={value.derived} resultId={value.resultId}");
        }

        public void Apply(CrystalStatesDto value)
        {
            if (disposed || !context.IsGuest || value.roomSessionId != context.RoomSessionId ||
                value.stateSeq <= appliedSeq || value.authorityTick < appliedTick) return;
            appliedSeq = value.stateSeq; appliedTick = value.authorityTick;
            foreach (var state in value.crystals)
            {
                if (!crystals.TryGetValue(state.skillInstanceId, out var crystal)) continue;
                crystal.Object.transform.position = new Vector2(state.position[0], state.position[1]);
                crystal.Object.transform.localScale = Vector3.one * state.scale;
            }
        }

        public void Dispose()
        {
            disposed = true;
            foreach (var crystal in new List<Crystal>(crystals.Values)) crystal.Lifetime.Dispose();
        }
        public void RemovePlayer(int playerId)
        {
            requests.Remove(playerId); stacks.Remove(playerId);
            foreach (var crystal in new List<Crystal>(crystals.Values))
                if (crystal.State.sourcePlayerId == playerId) crystal.Lifetime.Dispose();
        }
    }
}
