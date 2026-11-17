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
        private readonly Action<SwordInstanceEventDto> publishEvent;
        private readonly Func<long> currentLoadId;
        private readonly Action<Player, SwordInputDto> sendInput;
        private readonly Action<int, int, long, SkillRequestRejection> reject;
        private readonly Dictionary<long, NetworkSkillObject> swords = new Dictionary<long, NetworkSkillObject>();
        private readonly Dictionary<long, SwordPhase> publishedPhases = new Dictionary<long, SwordPhase>();
        private readonly Dictionary<int, long> requests = new Dictionary<int, long>();
        private readonly Dictionary<int, long> releaseRequests = new Dictionary<int, long>();
        private long stateSeq, appliedSeq;
        private ulong appliedTick;
        private bool disposed;

        public RoomSwordNetwork(RoomPresentationContext context, RoomPlayerEntities players, RoomNetworkObjects objects,
            RoomCombatDamage damage, SwordSkill template, Action<Player, SwordInputDto> sendInput,
            AuthorityResultSequence results = null, Func<int> allocateEntity = null, Action<SwordStatesDto> publish = null,
            Action<SwordInstanceEventDto> publishEvent = null, Func<long> currentLoadId = null,
            Action<int, int, long, SkillRequestRejection> reject = null)
        {
            this.context = context; this.players = players; this.objects = objects; this.damage = damage;
            this.template = template; this.sendInput = sendInput; this.results = results;
            this.allocateEntity = allocateEntity; this.publish = publish;
            this.publishEvent = publishEvent; this.currentLoadId = currentLoadId;
            this.reject = reject;
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
            if (player.NetworkInputLocked || player.IsLocalInputOwner && GameplayControlLock.IsActionInputBlocked)
            { Reject(actor, input.actionSeq, SkillRequestRejection.InputLocked); return; }
            if (player.stats.currentHealth <= 0)
            { Reject(actor, input.actionSeq, SkillRequestRejection.Dead); return; }
            if (player.isBusy || player.GetComponent<NetworkHitFeedback>()?.IsAuthorityKnockbackActive == true)
            { Reject(actor, input.actionSeq, SkillRequestRejection.Busy); return; }
            if (!player.Combat.Skills.Get<SwordSkill>().Unlocked)
            { Reject(actor, input.actionSeq, SkillRequestRejection.NotUnlocked); return; }
            var state = player.stateMachine.currentState;
            if (!player.IsGroundDetected() || state != player.idleState && state != player.moveState && state != player.aimSwordState)
            { Reject(actor, input.actionSeq, SkillRequestRejection.InvalidState); return; }
            if (input.action == SwordAction.Recall)
            {
                if (player.sword != null) player.sword.GetComponent<SwordSkillController>().ReturnSword();
                else Reject(actor, input.actionSeq, SkillRequestRejection.NoInstance);
                return;
            }
            if (input.action == SwordAction.Direction && state != player.aimSwordState)
            { Reject(actor, input.actionSeq, SkillRequestRejection.InvalidState); return; }
            if (HasSword(actor.PlayerId) || skill.ThrowPending)
            { Reject(actor, input.actionSeq, SkillRequestRejection.Busy); return; }
            if (player.Combat.Skills.Get<SwordSkill>().Remaining > 0)
            { Reject(actor, input.actionSeq, SkillRequestRejection.Cooldown); return; }
            Vector2 direction = new Vector2(input.direction[0], input.direction[1]);
            skill.RoomDirection = direction;
            player.FlipController(direction.x);
            if (input.action == SwordAction.Aim && state != player.aimSwordState) player.stateMachine.ChangeState(player.aimSwordState);
            else if (input.action == SwordAction.Release && state == player.aimSwordState)
            {
                releaseRequests[actor.PlayerId] = input.actionSeq;
                player.aimSwordState.ReleaseRoomSword(direction);
            }
            else if (input.action == SwordAction.Release)
            { Reject(actor, input.actionSeq, SkillRequestRejection.InvalidState); return; }
            Publish();
        }
        private void Reject(RoomPlayerIdentity actor, long requestSeq, SkillRequestRejection reason)
            => reject?.Invoke(actor.PlayerId, actor.EntityId, requestSeq, reason);
        public void RejectPendingRelease(Player player)
        {
            if (context.IsGuest || !releaseRequests.TryGetValue(player.Combat.PlayerId, out long requestSeq)) return;
            releaseRequests.Remove(player.Combat.PlayerId);
            reject?.Invoke(player.Combat.PlayerId, player.Combat.EntityId, requestSeq, SkillRequestRejection.Interrupted);
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
                dto.skillInstanceId, releaseRequests[dto.sourcePlayerId], tick, dto.endTick,
                new PrimaryAttackTiming(0, duration, new PrimaryHitSegmentTiming(0, duration)));
            releaseRequests.Remove(dto.sourcePlayerId);
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
            swords.Add(dto.skillInstanceId, sword);
            sword.Lifetime = objects.Register(context.RoomSessionId, dto.sourcePlayerId, value, () =>
            {
                swords.Remove(dto.skillInstanceId);
                if (!context.IsGuest && !disposed && publishedPhases.Remove(dto.skillInstanceId)) PublishInstanceEvent(sword, SwordInstanceEventKind.End);
                sword.Attack?.Close(); context.RemoveEntity(dto.entityId);
                if (!context.IsGuest && players.Entities.TryGetValue(dto.sourceEntityId, out var source) &&
                    source != null && source.sword == value) source.AssignNewSword(null);
                if (!context.IsGuest && !disposed) Publish();
            });
            return sword;
        }
        internal bool Hit(NetworkSkillObject sword, Enemy enemy) => damage.OnSkillHit(sword.Attack, enemy);
        public void Publish()
        {
            if (disposed || context.IsGuest || GameSimulationClock.AuthorityTick == 0) return;
            foreach (var sword in swords.Values)
            {
                if (!publishedPhases.TryGetValue(sword.SkillInstanceId, out var phase))
                    PublishInstanceEvent(sword, SwordInstanceEventKind.Spawn);
                else if (phase != sword.Phase)
                    PublishInstanceEvent(sword, SwordInstanceEventKind.Phase);
                publishedPhases[sword.SkillInstanceId] = sword.Phase;
            }
            var owners = new List<SwordOwnerDto>();
            foreach (var player in players.Entities.Values)
                if (player != null && player.Combat.PlayerId > 0)
                    owners.Add(new SwordOwnerDto { playerId = player.Combat.PlayerId, entityId = player.Combat.EntityId,
                        aiming = player.stateMachine.currentState == player.aimSwordState,
                        catching = player.stateMachine.currentState == player.catchSwordState });
            var values = new List<SwordTrajectoryDto>();
            foreach (var sword in swords.Values)
            {
                Vector2 position = sword.transform.position, direction = sword.transform.right;
                values.Add(new SwordTrajectoryDto { skillInstanceId = sword.SkillInstanceId,
                    position = new[] { position.x, position.y }, direction = new[] { direction.x, direction.y } });
            }
            publish(new SwordStatesDto { roomSessionId = context.RoomSessionId, authorityTick = GameSimulationClock.AuthorityTick,
                resultId = results.Allocate(), stateSeq = ++stateSeq, owners = owners.ToArray(), swords = values.ToArray() });
        }

        public void ReplayActiveInstances()
        {
            foreach (var sword in swords.Values)
            {
                PublishInstanceEvent(sword, SwordInstanceEventKind.Spawn);
                publishedPhases[sword.SkillInstanceId] = sword.Phase;
            }
        }

        private void PublishInstanceEvent(NetworkSkillObject sword, SwordInstanceEventKind kind)
        {
            Vector2 position = sword.transform.position, direction = sword.transform.right;
            var value = new SwordInstanceEventDto { roomSessionId = context.RoomSessionId, loadId = currentLoadId(),
                authorityTick = GameSimulationClock.AuthorityTick, resultId = results.Allocate(),
                requestSeq = sword.Attack.RequestSeq, skillInstanceId = sword.SkillInstanceId,
                entityId = sword.EntityId, sourcePlayerId = sword.SourcePlayerId, sourceEntityId = sword.SourceEntityId,
                startTick = sword.StartTick, endTick = sword.EndTick, kind = kind, phase = sword.Phase,
                position = new[] { position.x, position.y }, direction = new[] { direction.x, direction.y } };
            publishEvent(value);
            Debug.Log($"[RoomSword] A {kind} instance={value.skillInstanceId} requestSeq={value.requestSeq} phase={value.phase} tick={value.authorityTick}");
        }

        public void ApplyEvent(SwordInstanceEventDto value)
        {
            if (disposed || !context.IsGuest || value.roomSessionId != context.RoomSessionId) return;
            if (value.kind == SwordInstanceEventKind.End)
            {
                if (!swords.TryGetValue(value.skillInstanceId, out var ended)) return;
                if (ended.EntityId != value.entityId || ended.SourceEntityId != value.sourceEntityId ||
                    ended.StartTick != value.startTick || ended.EndTick != value.endTick)
                    throw new InvalidOperationException("Sword end metadata changed within an instance.");
                ended.Lifetime.Dispose();
                Debug.Log($"[RoomSword] B End instance={value.skillInstanceId} resultId={value.resultId}");
                return;
            }
            bool created = false;
            if (!swords.TryGetValue(value.skillInstanceId, out var sword))
            {
                if (value.kind != SwordInstanceEventKind.Spawn) throw new InvalidOperationException("Sword phase arrived before its spawn event.");
                var dto = new SwordObjectDto { entityId = value.entityId, sourcePlayerId = value.sourcePlayerId,
                    sourceEntityId = value.sourceEntityId, skillInstanceId = value.skillInstanceId,
                    startTick = value.startTick, endTick = value.endTick };
                sword = Register(UnityEngine.Object.Instantiate(template.SwordPrefab), dto);
                created = true;
            }
            if (sword.EntityId != value.entityId || sword.SourceEntityId != value.sourceEntityId ||
                sword.StartTick != value.startTick || sword.EndTick != value.endTick)
                throw new InvalidOperationException("Sword phase metadata changed within an instance.");
            sword.ResetTrajectory();
            sword.transform.position = new Vector2(value.position[0], value.position[1]);
            sword.transform.right = new Vector2(value.direction[0], value.direction[1]);
            bool changed = sword.Phase != value.phase || created;
            sword.GetComponent<SwordSkillController>().ShowNetworkPhase(value.phase, changed);
            sword.Phase = value.phase;
            Debug.Log($"[RoomSword] B {value.kind} instance={value.skillInstanceId} requestSeq={value.requestSeq} phase={value.phase} resultId={value.resultId}");
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
                var view = player.GetComponent<RemotePlayerView>();
                if (!view.LocalSwordPreviewActive) player.anim.SetBool("AimSword", owner.aiming);
                player.anim.SetBool("CatchSword", owner.catching);
                view.SwordActionActive = view.LocalSwordPreviewActive || owner.aiming || owner.catching;
            }
            foreach (var dto in value.swords)
            {
                if (swords.TryGetValue(dto.skillInstanceId, out var sword))
                    sword.Apply(dto, value.authorityTick, value.stateSeq);
            }
        }
        public void Dispose()
        {
            disposed = true;
            foreach (var sword in new List<NetworkSkillObject>(swords.Values)) sword.Lifetime.Dispose();
        }
        public void RemovePlayer(int playerId)
        {
            requests.Remove(playerId);
            releaseRequests.Remove(playerId);
            foreach (var player in players.Entities.Values)
                if (player.Combat.PlayerId == playerId) { Cancel(player); break; }
            foreach (var sword in new List<NetworkSkillObject>(swords.Values))
                if (sword.SourcePlayerId == playerId) sword.Lifetime.Dispose();
        }
    }
}
