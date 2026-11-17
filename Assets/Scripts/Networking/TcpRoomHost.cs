using System;
using System.Net;
using System.Collections.Generic;
using System.Diagnostics;
using Rpg.Networking.Protocol;
using System.IO;

namespace Rpg.Networking
{
    /// <summary>A's listener and admission handshake. No scene loading or entity creation.</summary>
    public sealed class TcpRoomHost : ISystem, IUpdateModule, IDisposable
    {
        private UtpRoomTransport listener;
        private bool disposed;
        private RoomHandshakePeer guestPeer;
        private Stopwatch handshakeClock;
        private int nextPlayerId, nextEntityId;
        private readonly List<Tuple<NetConnection, RoomHandshakePeer, Stopwatch>> rejected =
            new List<Tuple<NetConnection, RoomHandshakePeer, Stopwatch>>();
        public string RoomSessionId { get; private set; }
        public RoomPlayerIdentity HostIdentity { get; private set; }
        public RoomPlayerIdentity GuestIdentity { get; private set; }
        public event Action StateChanged;
        public event Action<NetworkDisconnectedEvent> Disconnected;
        public event Action<RoomExitEvent> RoomExited;
        public event Action SceneInitializationAllowed;
        public event Action<RoomPlayerIdentity, MovementInputDto> MovementInputReceived;
        public event Action<RoomPlayerIdentity, JumpRequestDto> JumpRequested;
        public event Action<RoomPlayerIdentity, DashRequestDto> DashRequested;
        public event Action<RoomPlayerIdentity, PrimaryAttackRequestDto> PrimaryAttackRequested;
        public event Action<RoomPlayerIdentity, EquipmentChangeRequestDto> EquipmentChangeRequested;
        public event Action<RoomPlayerIdentity, CraftRequestDto> CraftRequested;
        public event Action<RoomPlayerIdentity, PickupRequestDto> PickupRequested;
        public event Action<RoomPlayerIdentity, RoomRetryReadyDto> RetryReadyReceived;
        public event Action<RoomPlayerIdentity, SkillUnlockRequestDto> SkillUnlockRequested;
        public event Action<RoomPlayerIdentity, FlaskUseRequestDto> FlaskUseRequested;
        public event Action<RoomPlayerIdentity, SwordInputDto> SwordInputReceived;
        public event Action<RoomPlayerIdentity, CrystalInputDto> CrystalInputReceived;
        public event Action<RoomPlayerIdentity, BlackholeInputDto> BlackholeInputReceived;
        public event Action<RoomPlayerIdentity, ParryInputDto> ParryInputReceived;
        public long LastPrimaryAttackRequestSeq { get; private set; }
        public event Action<RoomPlayerIdentity, LocalInputLockDto> InputLockReceived;
        public long LastInputLockSeq { get; private set; }
        public long LastActionSeq { get; private set; }
        public long LastMovementInputSeq { get; private set; }
        private bool playerInitializationSent;
        private bool monsterInitializationSent;
        public AuthorityResultSequence Results { get; private set; }
        public long SceneLoadId { get; private set; }
        public bool LocalSceneReady { get; private set; }
        public bool GuestSceneReady { get; private set; }
        public bool CanInitializeEntities { get; private set; }
        private double sceneDeadline;
        private long sceneGuestConnection;
        private double nextOldInputLogTime;
        public bool IsListening => listener != null;
        public int Port { get; private set; }
        public NetConnection Guest { get; private set; }
        public int RejectedConnections { get; private set; }

        public void Init() { }

        public void StartListening(int port, int sendDelayMs = 0, int sendJitterMs = 0, float sendLossPercent = 0)
        {
            if (disposed) throw new ObjectDisposedException(nameof(TcpRoomHost));
            if (IsListening) return; // Idempotent: repeated creation cannot start another listener.
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            listener = UtpRoomTransport.Listen(port, sendDelayMs, sendJitterMs, sendLossPercent);
            Port = port;
            RejectedConnections = 0;
            RoomSessionId = Guid.NewGuid().ToString("N");
            Results = new AuthorityResultSequence(RoomSessionId);
            nextPlayerId = nextEntityId = 0;
            HostIdentity = AllocateIdentity();
            GameSimulationClock.Acquire(this, true);
            StateChanged?.Invoke();
        }

        public void SetTestNetwork(int sendDelayMs, int sendJitterMs, float sendLossPercent)
            => listener?.SetTestNetwork(sendDelayMs, sendJitterMs, sendLossPercent);

        public void Update()
        {
            if (disposed || listener == null) return;
            listener.Update();
            // Accept at most eight new UDP peers in one frame.
            for (int i = 0; i < 8 && listener != null && listener.TryAccept(out NetConnection accepted); i++)
            {
                if (Guest != null)
                {
                    // Keep the UDP peer alive long enough to flush its reliable rejection.
                    if (rejected.Count < 8)
                    {
                        NetConnection extra = null;
                        try
                        {
                            extra = accepted;
                            extra.Send(Rejection(HandshakeRejection.RoomFull));
                            rejected.Add(Tuple.Create(extra, (RoomHandshakePeer)null, Stopwatch.StartNew()));
                        }
                        catch { accepted.Dispose(); }
                    }
                    else accepted.Dispose();
                    RejectedConnections++;
                    continue;
                }
                try
                {
                    Guest = accepted;
                    guestPeer = new RoomHandshakePeer(Guest);
                    guestPeer.Subscribe<HandshakeHelloDto>(value => AcceptHello((HandshakeHelloDto)value));
                    NetConnection boundGuest = Guest;
                    SubscribeSkillInput<SwordInputDto>(boundGuest, (actor, input) => SwordInputReceived?.Invoke(actor, input));
                    SubscribeSkillInput<CrystalInputDto>(boundGuest, (actor, input) => CrystalInputReceived?.Invoke(actor, input));
                    SubscribeSkillInput<BlackholeInputDto>(boundGuest, (actor, input) => BlackholeInputReceived?.Invoke(actor, input));
                    SubscribeSkillInput<ParryInputDto>(boundGuest, (actor, input) => ParryInputReceived?.Invoke(actor, input));
                    guestPeer.Subscribe<FlaskUseRequestDto>(value =>
                    {
                        var request = (FlaskUseRequestDto)value;
                        if (!playerInitializationSent || !CanInitializeEntities || request.roomSessionId != RoomSessionId ||
                            !TryResolvePlayer(boundGuest, out var actor)) return;
                        FlaskUseRequested?.Invoke(actor, request);
                    });
                    guestPeer.Subscribe<SkillUnlockRequestDto>(value =>
                    {
                        var request = (SkillUnlockRequestDto)value;
                        if (!playerInitializationSent || !CanInitializeEntities || request.roomSessionId != RoomSessionId ||
                            !TryResolvePlayer(boundGuest, out var actor)) return;
                        SkillUnlockRequested?.Invoke(actor, request);
                    });
                    guestPeer.Subscribe<CraftRequestDto>(value =>
                    {
                        var request = (CraftRequestDto)value;
                        if (!playerInitializationSent || !CanInitializeEntities || request.roomSessionId != RoomSessionId ||
                            !TryResolvePlayer(boundGuest, out var actor)) return;
                        CraftRequested?.Invoke(actor, request);
                    });
                    guestPeer.Subscribe<PickupRequestDto>(value =>
                    {
                        var request = (PickupRequestDto)value;
                        if (!playerInitializationSent || !CanInitializeEntities || request.roomSessionId != RoomSessionId ||
                            !TryResolvePlayer(boundGuest, out var actor)) return;
                        PickupRequested?.Invoke(actor, request);
                    });
                    guestPeer.Subscribe<RoomRetryReadyDto>(value =>
                    {
                        var ready = (RoomRetryReadyDto)value;
                        if (!playerInitializationSent || !CanInitializeEntities || ready.roomSessionId != RoomSessionId ||
                            ready.loadId != SceneLoadId || !TryResolvePlayer(boundGuest, out var actor)) return;
                        RetryReadyReceived?.Invoke(actor, ready);
                    });
                    guestPeer.Subscribe<EquipmentChangeRequestDto>(value =>
                    {
                        var request = (EquipmentChangeRequestDto)value;
                        if (!playerInitializationSent || !CanInitializeEntities || request.roomSessionId != RoomSessionId ||
                            !TryResolvePlayer(boundGuest, out var actor)) return;
                        EquipmentChangeRequested?.Invoke(actor, request);
                    });
                    guestPeer.Subscribe<PrimaryAttackRequestDto>(value =>
                    {
                        var request = (PrimaryAttackRequestDto)value;
                        if (!playerInitializationSent || !CanInitializeEntities ||
                            !TryResolvePlayer(boundGuest, out var actor)) return;
                        LastPrimaryAttackRequestSeq = Math.Max(LastPrimaryAttackRequestSeq, request.requestSeq);
                        PrimaryAttackRequested?.Invoke(actor, request);
                    });
                    guestPeer.Subscribe<LocalInputLockDto>(value =>
                    {
                        var inputLock = (LocalInputLockDto)value;
                        if (!playerInitializationSent || !CanInitializeEntities ||
                            !TryResolvePlayer(boundGuest, out var actor) || inputLock.lockSeq <= LastInputLockSeq) return;
                        LastInputLockSeq = inputLock.lockSeq;
                        InputLockReceived?.Invoke(actor, inputLock);
                    });
                    guestPeer.Subscribe<DashRequestDto>(value =>
                    {
                        var dash = (DashRequestDto)value;
                        if (!playerInitializationSent || !CanInitializeEntities ||
                            !TryResolvePlayer(boundGuest, out var actor) || dash.actionSeq <= LastActionSeq) return;
                        LastActionSeq = dash.actionSeq;
                        DashRequested?.Invoke(actor, dash);
                    });
                    guestPeer.Subscribe<JumpRequestDto>(value =>
                    {
                        var jump = (JumpRequestDto)value;
                        if (!playerInitializationSent || !CanInitializeEntities ||
                            !TryResolvePlayer(boundGuest, out var actor) || jump.actionSeq <= LastActionSeq) return;
                        LastActionSeq = jump.actionSeq; // Ground rejection in gameplay still consumes this request.
                        JumpRequested?.Invoke(actor, jump);
                    });
                    guestPeer.Subscribe<MovementInputDto>(value =>
                    {
                        var movement = (MovementInputDto)value;
                        if (!playerInitializationSent || !CanInitializeEntities ||
                            !TryResolvePlayer(boundGuest, out var actor)) return;
                        if (movement.inputSeq <= LastMovementInputSeq)
                        {
                            if (RoomMessageMetrics.Enabled && MonotonicClock.Now >= nextOldInputLogTime)
                            {
                                UnityEngine.Debug.Log($"[RoomInputOrder] rejectedSeq={movement.inputSeq} currentSeq={LastMovementInputSeq} conn={boundGuest.SessionId}");
                                nextOldInputLogTime = MonotonicClock.Now + 5;
                            }
                            return;
                        }
                        // Codec already rejected non-finite/out-of-range axes. The connection supplies ownership.
                        LastMovementInputSeq = movement.inputSeq;
                        MovementInputReceived?.Invoke(actor, movement);
                    });
                    guestPeer.Subscribe<GuestLeaveDto>(value =>
                    {
                        var leave = (GuestLeaveDto)value;
                        if (!ReferenceEquals(Guest, boundGuest) || GuestIdentity == null || leave.roomSessionId != RoomSessionId) return;
                        var exit = new RoomExitEvent(RoomSessionId, boundGuest.SessionId, RoomExitReason.GuestLeft, false);
                        DropGuest();
                        StateChanged?.Invoke();
                        RoomExited?.Invoke(exit);
                    });
                    guestPeer.Subscribe<SceneReadyDto>(value =>
                    {
                        var ready = (SceneReadyDto)value;
                        if (!ReferenceEquals(Guest, boundGuest) || GuestIdentity == null ||
                            ready.roomSessionId != RoomSessionId || ready.loadId != SceneLoadId) return;
                        GuestSceneReady = true;
                        TryStartScene();
                    });
                    guestPeer.Subscribe<SceneFailedDto>(value =>
                    {
                        var failed = (SceneFailedDto)value;
                        if (ReferenceEquals(Guest, boundGuest) && failed.roomSessionId == RoomSessionId && failed.loadId == SceneLoadId)
                        {
                            var exit = new RoomExitEvent(RoomSessionId, boundGuest.SessionId, RoomExitReason.GuestLeft, false);
                            DropGuest();
                            StateChanged?.Invoke();
                            RoomExited?.Invoke(exit);
                        }
                    });
                    guestPeer.Disconnected += value =>
                    {
                        if (!ReferenceEquals(Guest, boundGuest)) return;
                        DropGuest();
                        StateChanged?.Invoke();
                        Disconnected?.Invoke(value);
                    };
                    handshakeClock = Stopwatch.StartNew();
                }
                catch { DropGuest(); accepted.Dispose(); continue; }
                StateChanged?.Invoke();
            }
            for (int i = rejected.Count - 1; i >= 0; i--)
            {
                var entry = rejected[i];
                if (!entry.Item1.IsOpen || entry.Item3.ElapsedMilliseconds >= 5000)
                {
                    entry.Item2?.Dispose();
                    entry.Item1.Dispose();
                    rejected.RemoveAt(i);
                }
            }
            guestPeer?.Update();
            if (SceneLoadId > 0 && GuestIdentity != null && !CanInitializeEntities && MonotonicClock.Now >= sceneDeadline)
                CloseRoom();
            if (GuestIdentity == null && guestPeer != null &&
                (guestPeer.Network.RejectedCount > 0 || handshakeClock.ElapsedMilliseconds >= 5000 || !Guest.IsOpen))
                RejectGuest(HandshakeRejection.InvalidRequest);
        }

        private static HandshakeResultDto Rejection(HandshakeRejection reason) => new HandshakeResultDto
        {
            schemaVersion = ProtocolSchema.SchemaVersion, rejection = reason
        };

        private void AcceptHello(HandshakeHelloDto hello)
        {
            if (GuestIdentity != null) return; // Replayed hello cannot allocate another identity.
            if (hello.schemaVersion != ProtocolSchema.SchemaVersion)
            {
                RejectGuest(HandshakeRejection.VersionMismatch);
                return;
            }
            GuestIdentity = AllocateIdentity();
            try
            {
                Guest.Send(new HandshakeResultDto
                {
                    schemaVersion = ProtocolSchema.SchemaVersion, accepted = true, roomSessionId = RoomSessionId,
                    hostPlayerId = HostIdentity.PlayerId, hostEntityId = HostIdentity.EntityId,
                    guestPlayerId = GuestIdentity.PlayerId, guestEntityId = GuestIdentity.EntityId
                });
                guestPeer.ConfirmRoom(RoomSessionId);
            }
            catch { DropGuest(); throw; }
            StateChanged?.Invoke();
        }

        private void RejectGuest(HandshakeRejection reason)
        {
            try { if (Guest.IsOpen) Guest.Send(Rejection(reason)); }
            catch (Exception error) when (error is IOException ||
                error is ObjectDisposedException || error is InvalidOperationException) { }
            finally { DropGuest(true); StateChanged?.Invoke(); }
        }

        private RoomPlayerIdentity AllocateIdentity() =>
            new RoomPlayerIdentity(RoomSessionId, ++nextPlayerId, ++nextEntityId);

        public void BeginSceneLoad(bool restart = false)
        {
            if (GuestIdentity == null || Guest == null) throw new InvalidOperationException("Admit B before loading a scene.");
            if (!restart && sceneGuestConnection == Guest.SessionId && SceneLoadId > 0) return;
            sceneGuestConnection = Guest.SessionId;
            SceneLoadId++;
            LocalSceneReady = GuestSceneReady = CanInitializeEntities = false;
            playerInitializationSent = false;
            monsterInitializationSent = false;
            LastActionSeq = LastPrimaryAttackRequestSeq = LastMovementInputSeq = LastInputLockSeq = 0;
            sceneDeadline = MonotonicClock.Now + 60;
            Guest.Send(new SceneLoadDto { roomSessionId = RoomSessionId, loadId = SceneLoadId, sceneName = RoomScene.Name }, true);
        }

        public void MarkSceneReady(string actualScene)
        {
            if (SceneLoadId <= 0 || actualScene != RoomScene.Name || !IsListening)
                throw new InvalidOperationException("A must initialize the requested MainScene first.");
            LocalSceneReady = true;
            TryStartScene();
        }

        private void TryStartScene()
        {
            if (!LocalSceneReady || !GuestSceneReady || CanInitializeEntities || GuestIdentity == null) return;
            Guest.Send(new SceneStartDto { roomSessionId = RoomSessionId, loadId = SceneLoadId, sceneName = RoomScene.Name }, true);
            CanInitializeEntities = true;
            SceneInitializationAllowed?.Invoke();
        }

        public void SendPlayerInitialization(PlayerInitializationDto value)
        {
            ProtocolCodec.ValidateMessage(value, true);
            if (!CanInitializeEntities || Guest == null || HostIdentity == null || GuestIdentity == null ||
                value.roomSessionId != RoomSessionId || value.loadId != SceneLoadId ||
                ProtocolCodec.FindPlayer(value, HostIdentity.PlayerId, HostIdentity.EntityId) == null ||
                ProtocolCodec.FindPlayer(value, GuestIdentity.PlayerId, GuestIdentity.EntityId) == null)
                throw new InvalidOperationException("Initialize the two admitted scene players before publishing their baseline.");
            Guest.Send(value, true);
            playerInitializationSent = true;
        }

        public int AllocateSceneEntityId()
        {
            if (!IsListening) throw new InvalidOperationException("A room is required to allocate an entity ID.");
            return checked(++nextEntityId);
        }

        public void SendMonsterInitialization(MonsterInitializationDto value)
        {
            ProtocolCodec.ValidateMessage(value, true);
            if (!CanInitializeEntities || !playerInitializationSent || Guest == null || value.roomSessionId != RoomSessionId || value.loadId != SceneLoadId)
                throw new InvalidOperationException("Initialize the room players before the monster manifest.");
            foreach (var monster in value.monsters)
                if (monster.entityId == HostIdentity.EntityId || monster.entityId == GuestIdentity.EntityId)
                    throw new InvalidDataException("Monster ID collides with a player ID.");
            Guest.Send(value, true);
            monsterInitializationSent = true;
        }

        public bool SendElementStates(ElementStatesDto value)
        {
            ProtocolCodec.ValidateMessage(value, true);
            if (!IsListening || !CanInitializeEntities || !playerInitializationSent || !monsterInitializationSent ||
                value.roomSessionId != RoomSessionId || Results == null || value.resultId > Results.LastAllocated || Guest == null || !Guest.IsOpen) return false;
            try { Guest.Send(value, true); return true; }
            catch (Exception error) when (error is IOException || error is ObjectDisposedException || error is InvalidOperationException) { return false; }
        }

        public bool SendDamageResult(DamageResultDto value)
        {
            ProtocolCodec.ValidateMessage(value, true);
            if (!IsListening || !CanInitializeEntities || !playerInitializationSent || !monsterInitializationSent ||
                value.roomSessionId != RoomSessionId || Results == null || value.resultId > Results.LastAllocated || Guest == null || !Guest.IsOpen) return false;
            try { Guest.Send(value, true); return true; }
            catch (Exception error) when (error is IOException ||
                error is ObjectDisposedException || error is InvalidOperationException) { return false; }
        }
        public bool SendMonsterDeath(MonsterDeathDto value) => SendAuthorityInventoryResult(value);
        public bool SendDashExecution(DashExecutionDto value)
        {
            ProtocolCodec.ValidateMessage(value, true);
            if (!CanInitializeEntities || !playerInitializationSent || Guest == null || !Guest.IsOpen ||
                value.roomSessionId != RoomSessionId || value.loadId != SceneLoadId) return false;
            Guest.Send(value, true);
            return true;
        }
        public bool SendJumpExecution(JumpExecutionDto value)
        {
            ProtocolCodec.ValidateMessage(value, true);
            if (!CanInitializeEntities || !playerInitializationSent || Guest == null || !Guest.IsOpen ||
                value.roomSessionId != RoomSessionId || value.loadId != SceneLoadId) return false;
            Guest.Send(value, true);
            return true;
        }
        public bool SendPrimaryAttackExecution(PrimaryAttackExecutionDto value)
        {
            ProtocolCodec.ValidateMessage(value, true);
            if (!CanInitializeEntities || !playerInitializationSent || Guest == null || !Guest.IsOpen ||
                value.roomSessionId != RoomSessionId || value.loadId != SceneLoadId) return false;
            Guest.Send(value, true);
            return true;
        }
        public bool SendPlayerDeath(PlayerDeathDto value) => SendAuthorityInventoryResult(value);
        public bool SendDropSpawn(DropSpawnDto value) => SendAuthorityInventoryResult(value);
        public bool SendPickupResult(PickupResultDto value) => SendInventoryResult(value);

        public bool SendEquipmentChangeResult(EquipmentChangeResultDto value)
            => SendInventoryResult(value);
        public bool SendCraftResult(CraftResultDto value) => SendInventoryResult(value);
        public bool SendSkillUnlockResult(SkillUnlockResultDto value) => SendAuthorityInventoryResult(value);
        public bool SendFlaskUseResult(FlaskUseResultDto value) => SendAuthorityInventoryResult(value);
        public bool SendSkillStates<T>(T value) where T : RoomSkillStatesDto => SendAuthorityInventoryResult(value);
        public bool SendSkillInstanceEvent<T>(T value) where T : RoomSkillInstanceEventDto
        {
            if (value.loadId != SceneLoadId) return false;
            return SendAuthorityInventoryResult(value);
        }
        public bool SendSkillRequestRejection(SkillRequestRejectionDto value)
        {
            if (value.loadId != SceneLoadId) return false;
            return SendAuthorityInventoryResult(value);
        }
        private void SubscribeSkillInput<T>(NetConnection boundGuest, Action<RoomPlayerIdentity, T> handler) where T : RoomSkillInputDto
        {
            guestPeer.Subscribe<T>(value =>
            {
                var input = (T)value;
                if (playerInitializationSent && CanInitializeEntities && input.roomSessionId == RoomSessionId &&
                    TryResolvePlayer(boundGuest, out var actor)) handler(actor, input);
            });
        }
        private bool SendInventoryResult(RoomInventoryResultDto value)
            => SendAuthorityInventoryResult(value);
        private bool SendAuthorityInventoryResult(AuthorityResultDto value)
        {
            ProtocolCodec.ValidateMessage(value, true);
            if (!IsListening || !CanInitializeEntities || !playerInitializationSent || value.roomSessionId != RoomSessionId ||
                Results == null || value.resultId > Results.LastAllocated || Guest == null || !Guest.IsOpen) return false;
            try { Guest.Send(value, true); return true; }
            catch (Exception error) when (error is IOException || error is ObjectDisposedException || error is InvalidOperationException) { return false; }
        }

        public bool SendMonsterStates(MonsterStatesDto value)
        {
            ProtocolCodec.ValidateMessage(value, true);
            if (!CanInitializeEntities || !playerInitializationSent || Guest == null || !Guest.IsOpen ||
                value.roomSessionId != RoomSessionId || value.loadId != SceneLoadId) return false;
            try
            {
                int capacity = Guest.UnreliablePayloadCapacity;
                if (ProtocolCodec.Encode(value, true).Length <= capacity) { Guest.Send(value, true); return true; }
                var monsters = new List<MonsterStateDto>();
                var attacks = new List<MonsterAttackStateDto>();
                var group = new MonsterStatesDto { roomSessionId = value.roomSessionId, loadId = value.loadId,
                    authorityTick = value.authorityTick, stateSeq = value.stateSeq };
                foreach (var monster in value.monsters)
                {
                    monsters.Add(monster);
                    MonsterAttackStateDto matchedAttack = null;
                    foreach (var attack in value.attacks)
                        if (attack.entityId == monster.entityId) { matchedAttack = attack; break; }
                    if (matchedAttack != null) attacks.Add(matchedAttack);
                    group.monsters = monsters.ToArray();
                    group.attacks = attacks.ToArray();
                    if (ProtocolCodec.Encode(group, true).Length <= capacity) continue;
                    monsters.RemoveAt(monsters.Count - 1);
                    if (matchedAttack != null) attacks.RemoveAt(attacks.Count - 1);
                    if (monsters.Count == 0) throw new InvalidDataException("One monster state exceeds UDP payload capacity.");
                    group.monsters = monsters.ToArray();
                    group.attacks = attacks.ToArray();
                    Guest.Send(group, true);
                    monsters.Clear(); attacks.Clear();
                    monsters.Add(monster);
                    if (matchedAttack != null) attacks.Add(matchedAttack);
                }
                group.monsters = monsters.ToArray();
                group.attacks = attacks.ToArray();
                Guest.Send(group, true);
                return true;
            }
            catch (Exception error) when (error is IOException ||
                error is ObjectDisposedException || error is InvalidOperationException) { return false; }
        }

        public bool SendPlayerPositions(PlayerPositionsDto value)
        {
            ProtocolCodec.ValidateMessage(value, true);
            if (!playerInitializationSent || !CanInitializeEntities || Guest == null || !Guest.IsOpen ||
                HostIdentity == null || GuestIdentity == null || value.roomSessionId != RoomSessionId || value.loadId != SceneLoadId ||
                ProtocolCodec.FindPosition(value, HostIdentity.EntityId) == null ||
                ProtocolCodec.FindPosition(value, GuestIdentity.EntityId) == null) return false;
            try
            {
                int capacity = Guest.UnreliablePayloadCapacity;
                if (ProtocolCodec.Encode(value, true).Length <= capacity) { Guest.Send(value, true); return true; }
                foreach (var player in value.players)
                {
                    var group = new PlayerPositionsDto { roomSessionId = value.roomSessionId, loadId = value.loadId,
                        authorityTick = value.authorityTick, stateSeq = value.stateSeq, players = new[] { player } };
                    if (ProtocolCodec.Encode(group, true).Length > capacity)
                        throw new InvalidDataException("One player state exceeds UDP payload capacity.");
                    Guest.Send(group, true);
                }
                return true;
            }
            catch (Exception error) when (error is IOException ||
                error is ObjectDisposedException || error is InvalidOperationException)
            {
                // NetConnection records transport failure; normal peer update performs disconnect cleanup.
                return false;
            }
        }

        private void DropGuest(bool holdRejection = false)
        {
            monsterInitializationSent = false;
            var previous = guestPeer;
            var previousConnection = Guest;
            LastMovementInputSeq = 0;
            nextOldInputLogTime = 0;
            LastActionSeq = 0;
            LastPrimaryAttackRequestSeq = 0;
            LastInputLockSeq = 0;
            playerInitializationSent = false;
            GuestSceneReady = CanInitializeEntities = false;
            guestPeer = null;
            Guest = null; GuestIdentity = null; handshakeClock = null;
            if (holdRejection && previousConnection != null)
                rejected.Add(Tuple.Create(previousConnection, previous, Stopwatch.StartNew()));
            else
            {
                try { previous?.Dispose(); }
                finally { previousConnection?.Dispose(); }
            }
        }

        // Request ownership is resolved by the actual connection, never a player ID supplied in a DTO.
        public bool TryResolvePlayer(NetConnection source, out RoomPlayerIdentity identity)
        {
            identity = source != null && ReferenceEquals(source, Guest) && source.IsOpen ? GuestIdentity : null;
            return identity != null;
        }

        public void CloseRoom()
        {
            if (!IsListening && Guest == null) return;
            var exit = new RoomExitEvent(RoomSessionId, Guest?.SessionId ?? 0, RoomExitReason.HostClosed, true);
            try
            {
                if (Guest != null && Guest.IsOpen)
                {
                    Guest.Send(new RoomClosedDto { roomSessionId = RoomSessionId });
                    listener.Flush();
                }
            }
            catch (Exception error) when (error is IOException ||
                error is ObjectDisposedException || error is InvalidOperationException) { }
            finally { StopListening(); }
            RoomExited?.Invoke(exit);
        }

        // Resource cleanup without a notice: used by error paths and low-level verification.
        public void StopListening()
        {
            Results?.Dispose(); Results = null;
            bool changed = listener != null || Guest != null;
            UtpRoomTransport previous = listener;
            listener = null;
            Port = 0;
            DropGuest();
            foreach (var entry in rejected) { entry.Item2?.Dispose(); entry.Item1.Dispose(); }
            rejected.Clear();
            previous?.Dispose();
            RoomSessionId = null;
            HostIdentity = null;
            SceneLoadId = 0;
            sceneGuestConnection = 0;
            LocalSceneReady = GuestSceneReady = CanInitializeEntities = false;
            GameSimulationClock.Release(this);
            if (changed) StateChanged?.Invoke();
        }

        public void Dispose()
        {
            if (disposed) return;
            try { CloseRoom(); }
            finally { disposed = true; StateChanged = null; Disconnected = null; RoomExited = null; SceneInitializationAllowed = null; MovementInputReceived = null; JumpRequested = null; DashRequested = null; InputLockReceived = null; PrimaryAttackRequested = null; EquipmentChangeRequested = null; CraftRequested = null; PickupRequested = null; RetryReadyReceived = null; SkillUnlockRequested = null; FlaskUseRequested = null; SwordInputReceived = null; CrystalInputReceived = null; BlackholeInputReceived = null; ParryInputReceived = null; }
        }
    }
}
