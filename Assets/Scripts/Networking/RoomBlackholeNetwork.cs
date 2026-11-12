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
        private readonly Action<BlackholeInputDto> sendInput;
        private readonly Dictionary<int, Hole> holes = new Dictionary<int, Hole>();
        private readonly Dictionary<int, long> requests = new Dictionary<int, long>();
        private long localActionSeq, stateSeq, appliedSeq;
        private ulong appliedTick;
        private bool disposed;

        public RoomBlackholeNetwork(RoomPresentationContext context, RoomPlayerEntities players, RoomSceneMonsters monsters,
            RoomNetworkObjects objects, BlackholeSkill template, RoomCloneNetwork clones, AuthorityResultSequence results,
            Func<int> allocateEntity, Action<BlackholeStatesDto> publish, Action<BlackholeInputDto> sendInput)
        {
            this.context = context; this.players = players; this.monsters = monsters; this.objects = objects;
            this.template = template; this.clones = clones; this.results = results; this.allocateEntity = allocateEntity;
            this.publish = publish; this.sendInput = sendInput;
        }
        public void EnsurePlayers()
        {
            foreach (var player in players.Entities.Values) player.RoomBlackhole = this;
        }
        public void SendCastOrRelease(Player player)
        {
            sendInput(new BlackholeInputDto { roomSessionId = context.RoomSessionId, actionSeq = ++localActionSeq,
                action = holes.ContainsKey(player.Combat.PlayerId) ? BlackholeAction.Release : BlackholeAction.Cast });
        }
        public void SendSelection(int targetEntityId, KeyCode key)
        {
            sendInput(new BlackholeInputDto { roomSessionId = context.RoomSessionId, actionSeq = ++localActionSeq,
                action = BlackholeAction.Select, targetEntityId = targetEntityId, keyCode = (int)key });
        }
        public void Request(RoomPlayerIdentity actor, BlackholeInputDto input)
        {
            if (disposed || context.IsGuest || input.roomSessionId != context.RoomSessionId ||
                requests.TryGetValue(actor.PlayerId, out long last) && input.actionSeq <= last) return;
            requests[actor.PlayerId] = input.actionSeq;
            var player = players.Entities[actor.EntityId];
            if (player.NetworkInputLocked || player.IsLocalInputOwner && GameplayControlLock.IsActionInputBlocked ||
                player.stats.currentHealth <= 0) return;
            holes.TryGetValue(actor.PlayerId, out var hole);
            if (input.action == BlackholeAction.Cast)
            {
                if (hole != null || !player.Combat.Skills.Get<BlackholeSkill>().Unlocked ||
                    player.Combat.Skills.Get<BlackholeSkill>().Remaining > 0 || !player.IsGroundDetected() ||
                    player.stateMachine.currentState != player.idleState && player.stateMachine.currentState != player.moveState) return;
                player.stateMachine.ChangeState(player.blackHoleState);
            }
            else if (hole != null && input.action == BlackholeAction.Release)
                hole.Object.GetComponent<BlackholeSkillController>().ReleaseRoom();
            else if (hole != null && input.action == BlackholeAction.Select &&
                hole.Prompts.TryGetValue(input.targetEntityId, out var prompt) && !prompt.Selected &&
                input.keyCode == prompt.State.keyCode && hole.Frozen.Contains(prompt.Target.GetComponent<Enemy>()) &&
                monsters.IsValidPrimaryAttackTarget(prompt.Target.GetComponent<Enemy>()))
            {
                prompt.Selected = true;
                hole.Object.GetComponent<BlackholeSkillController>().AddEnemyToList(prompt.Target);
                prompt.Object.GetComponent<BlackholeHotkeyController>().ConfirmSelection();
                Publish();
            }
        }
        public void Spawn(Player owner)
        {
            if (!owner.Combat.Skills.Commit<BlackholeSkill>()) return;
            var value = UnityEngine.Object.Instantiate(template.BlackholePrefab, owner.transform.position, Quaternion.identity);
            var state = new BlackholeObjectDto { entityId = allocateEntity(), sourcePlayerId = owner.Combat.PlayerId,
                sourceEntityId = owner.Combat.EntityId, skillInstanceId = objects.AllocatePrimaryAttackInstanceId() };
            var hole = Register(value, state);
            var controller = value.GetComponent<BlackholeSkillController>();
            controller.Room = this;
            controller.SetUpBlackhole(template.AmountOfAttack, template.MaxSize, template.GrowSpeed,
                template.ShrinkSpeed, template.CloneAttackCooldown, template.BlackholeDuration);
            Publish();
        }
        private Hole Register(GameObject value, BlackholeObjectDto state)
        {
            var hole = new Hole { Object = value, State = state };
            holes.Add(state.sourcePlayerId, hole);
            if (context.IsGuest)
            {
                value.GetComponent<BlackholeSkillController>().enabled = false;
                foreach (var collider in value.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
            }
            hole.Lifetime = objects.Register(context.RoomSessionId, state.sourcePlayerId, value, () =>
            {
                if (!context.IsGuest) foreach (var enemy in hole.Frozen) if (enemy != null) enemy.FreezeTime(false);
                foreach (var prompt in new List<Prompt>(hole.Prompts.Values)) prompt.Lifetime.Dispose();
                holes.Remove(state.sourcePlayerId);
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
        public bool Completed(Player player) => holes.TryGetValue(player.Combat.PlayerId, out var hole) &&
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
            var states = new List<BlackholeObjectDto>();
            foreach (var hole in holes.Values)
            {
                var position = hole.Object.transform.position;
                hole.State.position = new[] { position.x, position.y };
                hole.State.scale = Mathf.Max(0, hole.Object.transform.localScale.x);
                var prompts = new List<BlackholePromptDto>();
                foreach (var prompt in hole.Prompts.Values) if (!prompt.Selected) prompts.Add(prompt.State);
                hole.State.prompts = prompts.ToArray(); states.Add(hole.State);
            }
            publish(new BlackholeStatesDto { roomSessionId = context.RoomSessionId, authorityTick = GameSimulationClock.AuthorityTick,
                resultId = results.Allocate(), stateSeq = ++stateSeq, blackholes = states.ToArray() });
        }
        public void Apply(BlackholeStatesDto value)
        {
            if (disposed || !context.IsGuest || value.roomSessionId != context.RoomSessionId ||
                value.stateSeq <= appliedSeq || value.authorityTick < appliedTick) return;
            appliedSeq = value.stateSeq; appliedTick = value.authorityTick;
            var live = new HashSet<int>();
            foreach (var state in value.blackholes)
            {
                live.Add(state.sourcePlayerId);
                if (!holes.TryGetValue(state.sourcePlayerId, out var hole))
                    hole = Register(UnityEngine.Object.Instantiate(template.BlackholePrefab), state);
                hole.Object.transform.position = new Vector2(state.position[0], state.position[1]);
                hole.Object.transform.localScale = new Vector3(state.scale, state.scale, state.scale);
                var promptIds = new HashSet<int>();
                foreach (var promptState in state.prompts)
                {
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
                    component.enabled = players.Entities[state.sourceEntityId].IsLocalInputOwner;
                    prompt.Lifetime = objects.Register(context.RoomSessionId, state.sourcePlayerId, promptObject,
                        () => hole.Prompts.Remove(promptState.targetEntityId));
                }
                foreach (var prompt in new List<Prompt>(hole.Prompts.Values))
                    if (!promptIds.Contains(prompt.State.targetEntityId)) prompt.Lifetime.Dispose();
            }
            foreach (var hole in new List<Hole>(holes.Values))
                if (!live.Contains(hole.State.sourcePlayerId)) hole.Lifetime.Dispose();
        }
        public void Dispose()
        {
            disposed = true;
            foreach (var hole in new List<Hole>(holes.Values)) hole.Lifetime.Dispose();
        }
        public void RemovePlayer(int playerId)
        {
            requests.Remove(playerId);
            if (holes.TryGetValue(playerId, out var hole)) hole.Lifetime.Dispose();
        }
    }
}
