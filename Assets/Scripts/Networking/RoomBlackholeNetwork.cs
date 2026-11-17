using System;
using System.Collections.Generic;
using UnityEngine;
using Rpg.Networking.Protocol;

namespace Rpg.Networking
{
    public sealed class RoomBlackholeNetwork : IDisposable
    {
        private sealed class Prompt
        {
            public BlackholePromptDto State;
            public GameObject Object;
            public RoomNetworkObjects.Registration Lifetime;
            public Transform Target;
            public bool Selected;
        }
        private sealed class Hole
        {
            public BlackholeObjectDto State;
            public GameObject Object;
            public RoomNetworkObjects.Registration Lifetime;
            public readonly Dictionary<int, Prompt> Prompts = new Dictionary<int, Prompt>();
            public readonly HashSet<Enemy> Frozen = new HashSet<Enemy>();
            public readonly HashSet<int> SelectedTargets = new HashSet<int>();
            public ulong StartTick;
            public long CastRequestSeq, ReleaseRequestSeq;
            public bool Published, Released;
        }
        private readonly RoomPresentationContext context;
        private readonly RoomPlayerEntities players;
        private readonly RoomSceneMonsters monsters;
        private readonly RoomNetworkObjects objects;
        private readonly BlackholeSkill template;
        private readonly RoomCloneNetwork clones;
        private readonly AuthorityResultSequence results;
        private readonly Func<int> allocateEntity;
        private readonly Action<BlackholeStatesDto> publish;
        private readonly Action<BlackholeInstanceEventDto> publishEvent;
        private readonly Func<long> currentLoadId;
        private readonly Action<int, int, long, SkillRequestRejection> reject;
        private readonly Func<BlackholeInputDto, bool> sendInput;
        private readonly Dictionary<long, Hole> holes = new Dictionary<long, Hole>();
        private readonly Dictionary<int, long> requests = new Dictionary<int, long>();
        private readonly Dictionary<int, long> castRequests = new Dictionary<int, long>();
        private long localActionSeq, stateSeq, appliedSeq;
        private long localPreviewSeq;
        private GameObject localPreview;
        private float localPreviewEndTime;
        private ulong appliedTick;
        private bool disposed;

        public RoomBlackholeNetwork(RoomPresentationContext context, RoomPlayerEntities players, RoomSceneMonsters monsters,
            RoomNetworkObjects objects, BlackholeSkill template, RoomCloneNetwork clones, AuthorityResultSequence results,
            Func<int> allocateEntity, Action<BlackholeStatesDto> publish, Func<BlackholeInputDto, bool> sendInput,
            Action<BlackholeInstanceEventDto> publishEvent, Func<long> currentLoadId,
            Action<int, int, long, SkillRequestRejection> reject = null)
        {
            this.context = context; this.players = players; this.monsters = monsters; this.objects = objects;
            this.template = template; this.clones = clones; this.results = results; this.allocateEntity = allocateEntity;
            this.publish = publish; this.sendInput = sendInput;
            this.publishEvent = publishEvent; this.currentLoadId = currentLoadId;
            this.reject = reject;
        }
        public void EnsurePlayers()
        {
            foreach (var player in players.Entities.Values) player.RoomBlackhole = this;
        }
        public void SendCastOrRelease(Player player)
        {
            var input = new BlackholeInputDto { roomSessionId = context.RoomSessionId, actionSeq = ++localActionSeq,
                action = FindOwnerHole(player.Combat.PlayerId) != null ? BlackholeAction.Release : BlackholeAction.Cast };
            if (sendInput(input) && context.IsGuest && input.action == BlackholeAction.Cast)
                BeginLocalPreview(input.actionSeq, player.transform.position);
        }
        private Hole FindOwnerHole(int playerId)
        {
            foreach (var hole in holes.Values)
                if (hole.State.sourcePlayerId == playerId) return hole;
            return null;
        }
        public void SendSelection(int targetEntityId, KeyCode key)
        {
            sendInput(new BlackholeInputDto { roomSessionId = context.RoomSessionId, actionSeq = ++localActionSeq,
                action = BlackholeAction.Select, targetEntityId = targetEntityId, keyCode = (int)key });
        }
        private void BeginLocalPreview(long actionSeq, Vector3 position)
        {
            CancelLocalPreview();
            var fx = template.BlackholePrefab.transform.GetChild(0).gameObject;
            localPreview = UnityEngine.Object.Instantiate(fx, position, Quaternion.identity);
            localPreviewSeq = actionSeq;
            localPreviewEndTime = GameSimulationClock.Time + fx.GetComponent<Animator>().runtimeAnimatorController.animationClips[0].length;
        }
        private void CancelLocalPreview()
        {
            if (localPreview != null) UnityEngine.Object.Destroy(localPreview);
            localPreview = null;
            localPreviewSeq = 0;
        }
        public void RejectLocalPreview(long requestSeq)
        {
            if (localPreviewSeq == requestSeq) CancelLocalPreview();
        }
        public void AdvanceLocalPreview(Player player)
        {
            if (localPreviewSeq > 0 && (player.stats.currentHealth <= 0 || GameSimulationClock.Time >= localPreviewEndTime))
                CancelLocalPreview();
        }
        public void Request(RoomPlayerIdentity actor, BlackholeInputDto input)
        {
            if (disposed || context.IsGuest || input.roomSessionId != context.RoomSessionId ||
                requests.TryGetValue(actor.PlayerId, out long last) && input.actionSeq <= last) return;
            requests[actor.PlayerId] = input.actionSeq;
            var player = players.Entities[actor.EntityId];
            if (player.NetworkInputLocked || player.IsLocalInputOwner && GameplayControlLock.IsActionInputBlocked)
            { Reject(actor, input.actionSeq, SkillRequestRejection.InputLocked); return; }
            if (player.stats.currentHealth <= 0)
            { Reject(actor, input.actionSeq, SkillRequestRejection.Dead); return; }
            var hole = FindOwnerHole(actor.PlayerId);
            if (input.action == BlackholeAction.Cast)
            {
                if (hole != null) { Reject(actor, input.actionSeq, SkillRequestRejection.Busy); return; }
                if (!player.Combat.Skills.Get<BlackholeSkill>().Unlocked)
                { Reject(actor, input.actionSeq, SkillRequestRejection.NotUnlocked); return; }
                if (player.Combat.Skills.Get<BlackholeSkill>().Remaining > 0)
                { Reject(actor, input.actionSeq, SkillRequestRejection.Cooldown); return; }
                if (!player.IsGroundDetected() || player.stateMachine.currentState != player.idleState &&
                    player.stateMachine.currentState != player.moveState)
                { Reject(actor, input.actionSeq, SkillRequestRejection.InvalidState); return; }
                castRequests[actor.PlayerId] = input.actionSeq;
                player.stateMachine.ChangeState(player.blackHoleState);
            }
            else if (hole != null && input.action == BlackholeAction.Release)
            {
                if (hole.Released) { Reject(actor, input.actionSeq, SkillRequestRejection.InvalidState); return; }
                hole.ReleaseRequestSeq = input.actionSeq;
                hole.Object.GetComponent<BlackholeSkillController>().ReleaseRoom();
                hole.ReleaseRequestSeq = 0;
                if (!hole.Released) Reject(actor, input.actionSeq, SkillRequestRejection.NoTarget);
            }
            else if (hole != null && input.action == BlackholeAction.Select &&
                hole.Prompts.TryGetValue(input.targetEntityId, out var prompt) && !prompt.Selected &&
                input.keyCode == prompt.State.keyCode && hole.Frozen.Contains(prompt.Target.GetComponent<Enemy>()) &&
                monsters.IsValidPrimaryAttackTarget(prompt.Target.GetComponent<Enemy>()))
            {
                prompt.Selected = true;
                hole.SelectedTargets.Add(input.targetEntityId);
                hole.Object.GetComponent<BlackholeSkillController>().AddEnemyToList(prompt.Target);
                prompt.Object.GetComponent<BlackholeHotkeyController>().ConfirmSelection();
                PublishInstanceEvent(hole, BlackholeInstanceEventKind.Select, input.actionSeq, input.targetEntityId);
                Publish();
            }
            else if (input.action == BlackholeAction.Release)
                Reject(actor, input.actionSeq, SkillRequestRejection.NoInstance);
            else if (input.action == BlackholeAction.Select)
                Reject(actor, input.actionSeq, hole == null ? SkillRequestRejection.NoInstance : SkillRequestRejection.InvalidTarget);
        }
        private void Reject(RoomPlayerIdentity actor, long requestSeq, SkillRequestRejection reason)
            => reject?.Invoke(actor.PlayerId, actor.EntityId, requestSeq, reason);
        public void RejectPendingCast(Player player)
        {
            if (context.IsGuest || !castRequests.TryGetValue(player.Combat.PlayerId, out long requestSeq)) return;
            castRequests.Remove(player.Combat.PlayerId);
            reject?.Invoke(player.Combat.PlayerId, player.Combat.EntityId, requestSeq, SkillRequestRejection.Interrupted);
        }
        public void Spawn(Player owner)
        {
            if (!owner.Combat.Skills.Commit<BlackholeSkill>())
            {
                long requestSeq = castRequests[owner.Combat.PlayerId];
                castRequests.Remove(owner.Combat.PlayerId);
                reject?.Invoke(owner.Combat.PlayerId, owner.Combat.EntityId, requestSeq, SkillRequestRejection.Cooldown);
                return;
            }
            var value = UnityEngine.Object.Instantiate(template.BlackholePrefab, owner.transform.position, Quaternion.identity);
            var state = new BlackholeObjectDto { entityId = allocateEntity(), sourcePlayerId = owner.Combat.PlayerId,
                sourceEntityId = owner.Combat.EntityId, skillInstanceId = objects.AllocatePrimaryAttackInstanceId() };
            var hole = Register(value, state);
            hole.StartTick = GameSimulationClock.AuthorityTick;
            hole.CastRequestSeq = castRequests[owner.Combat.PlayerId];
            castRequests.Remove(owner.Combat.PlayerId);
            var controller = value.GetComponent<BlackholeSkillController>();
            controller.Room = this;
            controller.SetUpBlackhole(template.AmountOfAttack, template.MaxSize, template.GrowSpeed,
                template.ShrinkSpeed, template.CloneAttackCooldown, template.BlackholeDuration);
            Publish();
        }
        private Hole Register(GameObject value, BlackholeObjectDto state)
        {
            var hole = new Hole { Object = value, State = state };
            holes.Add(state.skillInstanceId, hole);
            if (context.IsGuest)
            {
                value.GetComponent<BlackholeSkillController>().enabled = false;
                foreach (var collider in value.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
            }
            hole.Lifetime = objects.Register(context.RoomSessionId, state.sourcePlayerId, value, () =>
            {
                if (!context.IsGuest) foreach (var enemy in hole.Frozen) if (enemy != null) enemy.FreezeTime(false);
                foreach (var prompt in new List<Prompt>(hole.Prompts.Values)) prompt.Lifetime.Dispose();
                holes.Remove(state.skillInstanceId);
                if (!context.IsGuest && !disposed && hole.Published)
                    PublishInstanceEvent(hole, BlackholeInstanceEventKind.End, hole.CastRequestSeq);
            });
            return hole;
        }
        public bool IsValidTarget(Enemy enemy) => monsters.IsValidPrimaryAttackTarget(enemy);
        public void TrackFrozen(BlackholeSkillController controller, Enemy enemy, bool frozen)
        {
            foreach (var hole in holes.Values)
                if (hole.Object.GetComponent<BlackholeSkillController>() == controller)
                { if (frozen) hole.Frozen.Add(enemy); else hole.Frozen.Remove(enemy); return; }
        }
        public bool HasPrompt(BlackholeSkillController controller, Transform target)
        {
            int id = target.GetComponent<NetworkEntityIdentity>().EntityId;
            foreach (var hole in holes.Values)
                if (hole.Object.GetComponent<BlackholeSkillController>() == controller) return hole.Prompts.ContainsKey(id);
            return false;
        }
        public void SpawnDerived(BlackholeSkillController controller, Transform target, Vector3 offset)
        {
            foreach (var hole in holes.Values)
                if (hole.Object.GetComponent<BlackholeSkillController>() == controller)
                { clones.SpawnAt(players.Entities[hole.State.sourceEntityId], target, offset, true); return; }
        }
        public void OnReleased(BlackholeSkillController controller)
        {
            foreach (var hole in holes.Values)
                if (hole.Object.GetComponent<BlackholeSkillController>() == controller && !hole.Released)
                {
                    hole.Released = true;
                    PublishInstanceEvent(hole, BlackholeInstanceEventKind.Release, hole.ReleaseRequestSeq);
                    Publish();
                    return;
                }
        }
        public void UnregisterHotkey(BlackholeSkillController controller, Enemy enemy)
        {
            foreach (var hole in holes.Values)
                if (hole.Object.GetComponent<BlackholeSkillController>() == controller &&
                    hole.Prompts.TryGetValue(enemy.GetComponent<NetworkEntityIdentity>().EntityId, out var prompt))
                { prompt.Lifetime.Dispose(); Publish(); return; }
        }
        public void RegisterHotkey(BlackholeSkillController controller, GameObject value, KeyCode key, Transform target)
        {
            Hole hole = null;
            foreach (var candidate in holes.Values)
                if (candidate.Object.GetComponent<BlackholeSkillController>() == controller) { hole = candidate; break; }
            var id = target.GetComponent<NetworkEntityIdentity>().EntityId;
            var prompt = new Prompt { Object = value, Target = target, State = new BlackholePromptDto
                { targetEntityId = id, keyCode = (int)key, position = new[] { value.transform.position.x, value.transform.position.y } } };
            hole.Prompts.Add(id, prompt);
            var component = value.GetComponent<BlackholeHotkeyController>();
            component.Room = this; component.TargetEntityId = id;
            component.enabled = players.Entities[hole.State.sourceEntityId].IsLocalInputOwner;
            prompt.Lifetime = objects.Register(context.RoomSessionId, hole.State.sourcePlayerId, value,
                () => hole.Prompts.Remove(id));
            Publish();
        }
        public bool Completed(Player player) => FindOwnerHole(player.Combat.PlayerId) is Hole hole &&
            hole.Object.GetComponent<BlackholeSkillController>().playerCanExitState;
        public void Publish()
        {
            if (disposed || context.IsGuest || GameSimulationClock.AuthorityTick == 0) return;
            foreach (var hole in new List<Hole>(holes.Values))
            {
                foreach (var prompt in new List<Prompt>(hole.Prompts.Values))
                    if (prompt.Object == null) prompt.Lifetime.Dispose();
                if (hole.Object == null) hole.Lifetime.Dispose();
            }
            var states = new List<BlackholeTrajectoryDto>();
            foreach (var hole in holes.Values)
            {
                var position = hole.Object.transform.position;
                hole.State.position = new[] { position.x, position.y };
                hole.State.scale = Mathf.Max(0, hole.Object.transform.localScale.x);
                if (!hole.Published)
                {
                    PublishInstanceEvent(hole, BlackholeInstanceEventKind.Spawn, hole.CastRequestSeq);
                    hole.Published = true;
                }
                var prompts = new List<BlackholePromptDto>();
                foreach (var prompt in hole.Prompts.Values) if (!prompt.Selected) prompts.Add(prompt.State);
                states.Add(new BlackholeTrajectoryDto { skillInstanceId = hole.State.skillInstanceId,
                    position = hole.State.position, scale = hole.State.scale, prompts = prompts.ToArray() });
            }
            publish(new BlackholeStatesDto { roomSessionId = context.RoomSessionId, authorityTick = GameSimulationClock.AuthorityTick,
                resultId = results.Allocate(), stateSeq = ++stateSeq, blackholes = states.ToArray() });
        }
        public void ReplayActiveInstances()
        {
            foreach (var hole in holes.Values)
            {
                PublishInstanceEvent(hole, BlackholeInstanceEventKind.Spawn, hole.CastRequestSeq);
                if (hole.Released) PublishInstanceEvent(hole, BlackholeInstanceEventKind.Release, 0);
            }
        }
        private void PublishInstanceEvent(Hole hole, BlackholeInstanceEventKind kind, long requestSeq, int targetEntityId = 0)
        {
            var state = hole.State;
            Vector2 position = hole.Object != null ? hole.Object.transform.position :
                new Vector2(state.position[0], state.position[1]);
            var value = new BlackholeInstanceEventDto { roomSessionId = context.RoomSessionId, loadId = currentLoadId(),
                authorityTick = GameSimulationClock.AuthorityTick, resultId = results.Allocate(),
                requestSeq = requestSeq, skillInstanceId = state.skillInstanceId, entityId = state.entityId,
                sourcePlayerId = state.sourcePlayerId, sourceEntityId = state.sourceEntityId,
                startTick = hole.StartTick, kind = kind, targetEntityId = targetEntityId,
                position = new[] { position.x, position.y },
                scale = hole.Object != null ? Mathf.Max(0, hole.Object.transform.localScale.x) : state.scale };
            publishEvent(value);
            Debug.Log($"[RoomBlackhole] A {kind} instance={value.skillInstanceId} target={targetEntityId} resultId={value.resultId}");
        }
        public void ApplyEvent(BlackholeInstanceEventDto value)
        {
            if (disposed || !context.IsGuest || value.roomSessionId != context.RoomSessionId) return;
            if (!holes.TryGetValue(value.skillInstanceId, out var hole))
            {
                if (value.kind == BlackholeInstanceEventKind.End) return;
                if (value.kind != BlackholeInstanceEventKind.Spawn)
                    throw new InvalidOperationException("Blackhole event arrived before its spawn event.");
                var state = new BlackholeObjectDto { entityId = value.entityId, sourcePlayerId = value.sourcePlayerId,
                    sourceEntityId = value.sourceEntityId, skillInstanceId = value.skillInstanceId };
                hole = Register(UnityEngine.Object.Instantiate(template.BlackholePrefab), state);
                hole.StartTick = value.startTick; hole.CastRequestSeq = value.requestSeq;
            }
            if (hole.State.entityId != value.entityId || hole.State.sourcePlayerId != value.sourcePlayerId ||
                hole.State.sourceEntityId != value.sourceEntityId || hole.StartTick != value.startTick)
                throw new InvalidOperationException("Blackhole metadata changed within an instance.");
            if (value.kind == BlackholeInstanceEventKind.End) hole.Lifetime.Dispose();
            else
            {
                if (value.kind == BlackholeInstanceEventKind.Spawn && value.sourcePlayerId == context.LocalPlayerId)
                    CancelLocalPreview();
                hole.Object.transform.position = new Vector2(value.position[0], value.position[1]);
                hole.Object.transform.localScale = Vector3.one * value.scale;
                if (value.kind == BlackholeInstanceEventKind.Select)
                {
                    hole.SelectedTargets.Add(value.targetEntityId);
                    if (!hole.Released && hole.Prompts.TryGetValue(value.targetEntityId, out var prompt))
                        prompt.Object.GetComponent<BlackholeHotkeyController>().ConfirmSelection();
                }
                else if (value.kind == BlackholeInstanceEventKind.Release)
                {
                    hole.Released = true;
                    foreach (var prompt in new List<Prompt>(hole.Prompts.Values)) prompt.Lifetime.Dispose();
                }
            }
            Debug.Log($"[RoomBlackhole] B {value.kind} instance={value.skillInstanceId} target={value.targetEntityId} resultId={value.resultId}");
        }
        public void Apply(BlackholeStatesDto value)
        {
            if (disposed || !context.IsGuest || value.roomSessionId != context.RoomSessionId ||
                value.stateSeq <= appliedSeq || value.authorityTick < appliedTick) return;
            appliedSeq = value.stateSeq; appliedTick = value.authorityTick;
            foreach (var state in value.blackholes)
            {
                if (!holes.TryGetValue(state.skillInstanceId, out var hole)) continue;
                hole.Object.transform.position = new Vector2(state.position[0], state.position[1]);
                hole.Object.transform.localScale = new Vector3(state.scale, state.scale, state.scale);
                var promptIds = new HashSet<int>();
                foreach (var promptState in state.prompts)
                {
                    if (hole.Released || hole.SelectedTargets.Contains(promptState.targetEntityId)) continue;
                    promptIds.Add(promptState.targetEntityId);
                    if (hole.Prompts.ContainsKey(promptState.targetEntityId)) continue;
                    context.TryGetEntity(promptState.targetEntityId, out var targetView);
                    var target = targetView.View;
                    var promptObject = UnityEngine.Object.Instantiate(hole.Object.GetComponent<BlackholeSkillController>().HotkeyPrefab,
                        new Vector2(promptState.position[0], promptState.position[1]), Quaternion.identity);
                    var prompt = new Prompt { Object = promptObject, Target = target, State = promptState };
                    hole.Prompts.Add(promptState.targetEntityId, prompt);
                    var component = promptObject.GetComponent<BlackholeHotkeyController>();
                    component.SetUpHotkey((KeyCode)promptState.keyCode, target, hole.Object.GetComponent<BlackholeSkillController>());
                    component.Room = this; component.TargetEntityId = promptState.targetEntityId;
                    component.enabled = players.Entities[hole.State.sourceEntityId].IsLocalInputOwner;
                    prompt.Lifetime = objects.Register(context.RoomSessionId, hole.State.sourcePlayerId, promptObject,
                        () => hole.Prompts.Remove(promptState.targetEntityId));
                }
                foreach (var prompt in new List<Prompt>(hole.Prompts.Values))
                    if (!promptIds.Contains(prompt.State.targetEntityId)) prompt.Lifetime.Dispose();
            }
        }
        public void Dispose()
        {
            disposed = true;
            CancelLocalPreview();
            foreach (var hole in new List<Hole>(holes.Values)) hole.Lifetime.Dispose();
        }
        public void RemovePlayer(int playerId)
        {
            requests.Remove(playerId);
            castRequests.Remove(playerId);
            if (playerId == context.LocalPlayerId) CancelLocalPreview();
            if (FindOwnerHole(playerId) is Hole hole) hole.Lifetime.Dispose();
        }
    }
}
