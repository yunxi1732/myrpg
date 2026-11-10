using System;
using System.Collections.Generic;
using UnityEngine;
using Rpg.Networking.Protocol;

namespace Rpg.Networking
{
    public sealed class RoomSwordNetwork : IDisposable
    {
        private readonly RoomPresentationContext context;
        private readonly RoomPlayerEntities players;
        private readonly RoomNetworkObjects objects;
        private readonly RoomCombatDamage damage;
        private readonly SwordSkill template;
        private readonly AuthorityResultSequence results;
        private readonly Func<int> allocateEntity;
        private readonly Action<SwordStatesDto> publish;
        private readonly Action<Player, SwordInputDto> sendInput;
        private readonly Dictionary<int, NetworkSkillObject> swords = new Dictionary<int, NetworkSkillObject>();
        private readonly Dictionary<int, long> requests = new Dictionary<int, long>();
        private long stateSeq, appliedSeq;
        private ulong appliedTick;
        private bool disposed;

        public RoomSwordNetwork(RoomPresentationContext context, RoomPlayerEntities players, RoomNetworkObjects objects,
            RoomCombatDamage damage, SwordSkill template, Action<Player, SwordInputDto> sendInput,
            AuthorityResultSequence results = null, Func<int> allocateEntity = null, Action<SwordStatesDto> publish = null)
        {
            this.context = context; this.players = players; this.objects = objects; this.damage = damage;
            this.template = template; this.sendInput = sendInput; this.results = results;
            this.allocateEntity = allocateEntity; this.publish = publish;
        }
        public void EnsurePlayers()
        {
            foreach (var player in players.Entities.Values)
                if (player.RoomSword == null) player.RoomSword = template.CreateRoomAdapter(player, this);
        }
        public void SendInput(Player player, SwordInputDto input) => sendInput(player, input);
        public bool HasSword(int playerId)
        {
            foreach (var sword in swords.Values) if (sword.SourcePlayerId == playerId) return true;
            return false;
        }
        public void Request(RoomPlayerIdentity actor, SwordInputDto input)
        {
            if (disposed || context.IsGuest || input.roomSessionId != context.RoomSessionId ||
                !players.Entities.TryGetValue(actor.EntityId, out var player) || player.Combat.PlayerId != actor.PlayerId ||
                requests.TryGetValue(actor.PlayerId, out long last) && input.actionSeq <= last) return;
            requests[actor.PlayerId] = input.actionSeq;
            var skill = player.RoomSword;
            if (input.action == SwordAction.Cancel) { Cancel(player); Publish(); return; }
            if (player.NetworkInputLocked || player.IsLocalInputOwner && GameplayControlLock.IsActionInputBlocked ||
                player.stats.currentHealth <= 0 || player.isBusy ||
                player.GetComponent<NetworkHitFeedback>()?.IsAuthorityKnockbackActive == true ||
                !player.Combat.Skills.Get<SwordSkill>().Unlocked) return;
            var state = player.stateMachine.currentState;
            if (!player.IsGroundDetected() || state != player.idleState && state != player.moveState && state != player.aimSwordState) return;
            if (input.action == SwordAction.Recall)
            {
                if (player.sword != null) player.sword.GetComponent<SwordSkillController>().ReturnSword();
                return;
            }
            if (input.action == SwordAction.Direction && state != player.aimSwordState) return;
            if (HasSword(actor.PlayerId) || skill.ThrowPending || player.Combat.Skills.Get<SwordSkill>().Remaining > 0) return;
            Vector2 direction = new Vector2(input.direction[0], input.direction[1]);
            skill.RoomDirection = direction;
            player.FlipController(direction.x);
            if (input.action == SwordAction.Aim && state != player.aimSwordState) player.stateMachine.ChangeState(player.aimSwordState);
            else if (input.action == SwordAction.Release && state == player.aimSwordState) player.aimSwordState.ReleaseRoomSword(direction);
            Publish();
        }
        private static void Cancel(Player player)
        {
            player.RoomSword.CancelAim();
            if (player.stateMachine.currentState == player.aimSwordState) player.aimSwordState.CancelForControlLock();
        }
        public void Advance()
        {
            foreach (var player in players.Entities.Values)
                if (player != null && (player.NetworkInputLocked || player.stats.currentHealth <= 0 ||
                    player.IsLocalInputOwner && GameplayControlLock.IsActionInputBlocked)) Cancel(player);
        }
        public void RegisterSword(Player owner, GameObject value)
        {
            ulong tick = GameSimulationClock.AuthorityTick;
            int duration = Mathf.CeilToInt(SwordSkillController.LifetimeSeconds / GameSimulationClock.FixedDeltaTime);
            var dto = new SwordObjectDto { entityId = allocateEntity(), sourcePlayerId = owner.Combat.PlayerId,
                sourceEntityId = owner.Combat.EntityId, skillInstanceId = objects.AllocatePrimaryAttackInstanceId(),
                startTick = tick, endTick = tick + (ulong)duration };
            var sword = Register(value, dto);
            sword.Attack = new PrimaryAttackInstance(context.RoomSessionId, dto.sourcePlayerId, dto.sourceEntityId,
                dto.skillInstanceId, requests[dto.sourcePlayerId], tick, dto.endTick,
                new PrimaryAttackTiming(0, duration, new PrimaryHitSegmentTiming(0, duration)));
        }
        private NetworkSkillObject Register(GameObject value, SwordObjectDto dto)
        {
            var sword = value.AddComponent<NetworkSkillObject>();
            sword.Room = this; sword.EntityId = dto.entityId; sword.SourcePlayerId = dto.sourcePlayerId;
            sword.SourceEntityId = dto.sourceEntityId; sword.SkillInstanceId = dto.skillInstanceId;
            sword.StartTick = dto.startTick; sword.EndTick = dto.endTick;
            value.GetComponent<SwordSkillController>().Network = sword;
            context.RegisterEntity(dto.entityId, 0, value.transform);
            if (context.IsGuest) value.GetComponent<RemotePlayerView>().enabled = false;
            swords.Add(dto.entityId, sword);
            sword.Lifetime = objects.Register(context.RoomSessionId, dto.sourcePlayerId, value, () =>
            {
                swords.Remove(dto.entityId); sword.Attack?.Close(); context.RemoveEntity(dto.entityId);
                if (!context.IsGuest && players.Entities.TryGetValue(dto.sourceEntityId, out var source) &&
                    source != null && source.sword == value) source.AssignNewSword(null);
                if (!context.IsGuest && !disposed) Publish();
            });
            return sword;
        }
        internal bool Hit(NetworkSkillObject sword, Enemy enemy) => damage.OnSwordHit(sword.Attack, enemy);
        public void Publish()
        {
            if (disposed || context.IsGuest || GameSimulationClock.AuthorityTick == 0) return;
            var owners = new List<SwordOwnerDto>();
            foreach (var player in players.Entities.Values)
                if (player != null && player.Combat.PlayerId > 0)
                    owners.Add(new SwordOwnerDto { playerId = player.Combat.PlayerId, entityId = player.Combat.EntityId,
                        aiming = player.stateMachine.currentState == player.aimSwordState,
                        catching = player.stateMachine.currentState == player.catchSwordState });
            var values = new List<SwordObjectDto>();
            foreach (var sword in swords.Values)
            {
                Vector2 position = sword.transform.position, direction = sword.transform.right;
                values.Add(new SwordObjectDto { entityId = sword.EntityId, sourcePlayerId = sword.SourcePlayerId,
                    sourceEntityId = sword.SourceEntityId, skillInstanceId = sword.SkillInstanceId,
                    startTick = sword.StartTick, endTick = sword.EndTick, phase = sword.Phase,
                    position = new[] { position.x, position.y }, direction = new[] { direction.x, direction.y } });
            }
            publish(new SwordStatesDto { roomSessionId = context.RoomSessionId, authorityTick = GameSimulationClock.AuthorityTick,
                resultId = results.Allocate(), stateSeq = ++stateSeq, owners = owners.ToArray(), swords = values.ToArray() });
        }
        public void Apply(SwordStatesDto value)
        {
            if (disposed || !context.IsGuest || value.roomSessionId != context.RoomSessionId || value.stateSeq <= appliedSeq ||
                value.authorityTick < appliedTick || !damage.Receipts.TryAccept(value)) return;
            appliedSeq = value.stateSeq; appliedTick = value.authorityTick;
            foreach (var owner in value.owners)
            {
                if (!players.Entities.TryGetValue(owner.entityId, out var player) || player.Combat.PlayerId != owner.playerId) continue;
                if (owner.catching && !player.anim.GetBool("CatchSword") && player.IsLocalInputOwner)
                    player.fx.ScreenShake(player.fx.shakeSwordImpact);
                player.anim.SetBool("AimSword", owner.aiming); player.anim.SetBool("CatchSword", owner.catching);
                player.GetComponent<RemotePlayerView>().SwordActionActive = owner.aiming || owner.catching;
            }
            var live = new HashSet<int>();
            foreach (var dto in value.swords)
            {
                live.Add(dto.entityId);
                if (!swords.TryGetValue(dto.entityId, out var sword))
                    sword = Register(UnityEngine.Object.Instantiate(template.SwordPrefab), dto);
                sword.Apply(dto, value.authorityTick, value.stateSeq);
            }
            foreach (var sword in new List<NetworkSkillObject>(swords.Values))
                if (!live.Contains(sword.EntityId)) sword.Lifetime.Dispose();
        }
        public void Dispose()
        {
            disposed = true;
            foreach (var sword in new List<NetworkSkillObject>(swords.Values)) sword.Lifetime.Dispose();
        }
    }
}
