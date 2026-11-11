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
        private readonly Dictionary<int, Crystal> crystals = new Dictionary<int, Crystal>();
        private readonly Dictionary<int, long> requests = new Dictionary<int, long>();
        private readonly Dictionary<int, (int remaining, ulong deadline)> stacks = new Dictionary<int, (int, ulong)>();
        private long stateSeq, appliedSeq;
        private ulong appliedTick;
        private bool disposed;

        public RoomCrystalNetwork(RoomPresentationContext context, RoomPlayerEntities players, RoomNetworkObjects objects,
            CrystalSkill template, RoomCombatDamage damage, AuthorityResultSequence results, Func<int> allocateEntity, Action<CrystalStatesDto> publish)
        {
            this.context = context; this.players = players; this.objects = objects; this.template = template;
            this.damage = damage;
            this.results = results; this.allocateEntity = allocateEntity; this.publish = publish;
        }

        public void Request(RoomPlayerIdentity actor, CrystalInputDto input)
        {
            if (disposed || context.IsGuest || input.roomSessionId != context.RoomSessionId ||
                requests.TryGetValue(actor.PlayerId, out long last) && input.actionSeq <= last) return;
            requests[actor.PlayerId] = input.actionSeq;
            var player = players.Entities[actor.EntityId];
            if (player.NetworkInputLocked || player.IsLocalInputOwner && GameplayControlLock.IsActionInputBlocked ||
                player.stats.currentHealth <= 0 || player.isBusy ||
                !player.Combat.Skills.Get<CrystalSkill>().Unlocked) return;
            bool exploding = player.Combat.IsRoomNodeUnlocked("Explosion");
            bool moving = player.Combat.IsRoomNodeUnlocked("Controlled destruction");
            var slot = player.Combat.Skills.Get<CrystalSkill>();
            if (player.Combat.IsRoomNodeUnlocked("Multiple destruction"))
            {
                if (slot.Remaining > 0) return;
                if (!stacks.TryGetValue(actor.PlayerId, out var stack))
                {
                    slot.Configure(template.RoomStackCooldown);
                    stack = (template.RoomStackCount, checked(GameSimulationClock.AuthorityTick +
                        (ulong)Mathf.CeilToInt(template.RoomStackWindow / GameSimulationClock.FixedDeltaTime)));
                }
                Spawn(player, player.transform.position, exploding, moving, false);
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
                if (moving || slot.Remaining > 0) return;
                Vector3 position = player.transform.position;
                player.transform.position = existing.Object.transform.position;
                existing.Object.transform.position = position;
                if (player.Combat.IsRoomNodeUnlocked("Mirage blink")) Clones.SpawnAt(player, existing.Object.transform, Vector3.zero);
                existing.Object.GetComponent<CrystalSkillController>().FinishCrystal();
                if (existing.Attack == null) existing.Lifetime.Dispose();
                Publish();
                return;
            }
            if (!player.Combat.Skills.Commit<CrystalSkill>()) return;
            Spawn(player, player.transform.position, exploding, moving, false);
        }

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

        private void Spawn(Player owner, Vector3 position, bool exploding, bool moving, bool derived)
        {
            var crystal = UnityEngine.Object.Instantiate(template.CrystalPrefab, position, Quaternion.identity);
            var target = moving ? template.FindRoomTarget(crystal.transform) : null;
            var controller = crystal.GetComponent<CrystalSkillController>();
            controller.SetUpCrystal(template.CrystalDuration, exploding, moving, template.RoomMoveSpeed, target, owner);
            var state = new CrystalObjectDto { entityId = allocateEntity(), sourcePlayerId = owner.Combat.PlayerId,
                sourceEntityId = owner.Combat.EntityId, skillInstanceId = objects.AllocatePrimaryAttackInstanceId() };
            Register(crystal, state);
            var registered = crystals[state.entityId];
            registered.Derived = derived;
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
            Spawn(owner, origin.position + offset, owner.Combat.IsRoomNodeUnlocked("Explosion"), true, true);
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
            crystals.Add(state.entityId, crystal);
            value.GetComponent<CrystalSkillController>().Room = this;
            if (context.IsGuest)
            {
                value.GetComponent<CrystalSkillController>().enabled = false;
                value.GetComponent<Collider2D>().enabled = false;
            }
            crystal.Lifetime = objects.Register(context.RoomSessionId, state.sourcePlayerId, value,
                () => { crystal.Attack?.Close(); crystals.Remove(state.entityId); });
        }

        public void Publish()
        {
            if (disposed || context.IsGuest || GameSimulationClock.AuthorityTick == 0) return;
            foreach (var value in new List<Crystal>(crystals.Values))
                if (value.Object == null) value.Lifetime.Dispose();
            var states = new List<CrystalObjectDto>();
            foreach (var value in crystals.Values)
            {
                Vector2 position = value.Object.transform.position;
                value.State.position = new[] { position.x, position.y };
                value.State.scale = value.Object.transform.localScale.x;
                value.State.exploding = value.Object.GetComponent<Animator>().GetBool("Explode");
                states.Add(value.State);
            }
            publish(new CrystalStatesDto { roomSessionId = context.RoomSessionId,
                authorityTick = GameSimulationClock.AuthorityTick, resultId = results.Allocate(),
                stateSeq = ++stateSeq, crystals = states.ToArray() });
        }

        public void Apply(CrystalStatesDto value)
        {
            if (disposed || !context.IsGuest || value.roomSessionId != context.RoomSessionId ||
                value.stateSeq <= appliedSeq || value.authorityTick < appliedTick) return;
            appliedSeq = value.stateSeq; appliedTick = value.authorityTick;
            var live = new HashSet<int>();
            foreach (var state in value.crystals)
            {
                live.Add(state.entityId);
                if (!crystals.TryGetValue(state.entityId, out var crystal))
                {
                    var instance = UnityEngine.Object.Instantiate(template.CrystalPrefab);
                    Register(instance, state);
                    crystal = crystals[state.entityId];
                }
                crystal.Object.transform.position = new Vector2(state.position[0], state.position[1]);
                crystal.Object.transform.localScale = Vector3.one * state.scale;
                crystal.Object.GetComponent<Animator>().SetBool("Explode", state.exploding);
            }
            foreach (var crystal in new List<Crystal>(crystals.Values))
                if (!live.Contains(crystal.State.entityId)) crystal.Lifetime.Dispose();
        }

        public void Dispose()
        {
            disposed = true;
            foreach (var crystal in new List<Crystal>(crystals.Values)) crystal.Lifetime.Dispose();
        }
    }
}
