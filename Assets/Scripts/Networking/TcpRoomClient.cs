using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Rpg.Networking.Protocol;
using System.IO;

namespace Rpg.Networking
{
    public enum RoomConnectionState { Idle, Connecting, Connected, Failed, Cancelled, Handshaking, Disconnected }

    /// <summary>Async TCP and admission handshake. Results are consumed on main-thread Update.</summary>
    public sealed class TcpRoomClient : ISystem, IUpdateModule, IDisposable
    {
        private sealed class Attempt
        {
            public readonly long Id;
            public readonly TcpClient Client;
            public readonly Task Task;
            public readonly Stopwatch Elapsed = Stopwatch.StartNew();

            public Attempt(long id, IPAddress address, int port)
            {
                Id = id;
                Client = new TcpClient(address.AddressFamily);
                try { Task = Client.ConnectAsync(address, port); }
                catch { Client.Close(); throw; }
                // Observe even a late fault after cancellation. Never touches state, events or Unity.
                Task.ContinueWith(task => { var ignored = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }

        private readonly int timeoutMilliseconds;
        private Attempt attempt;
        private long generation;
        private bool disposed;
        private RoomHandshakePeer handshakePeer;
        private Stopwatch handshakeClock;
        public string RoomSessionId { get; private set; }
        public RoomPlayerIdentity LocalIdentity { get; private set; }
        public RoomPlayerIdentity HostIdentity { get; private set; }
        public event Action StateChanged;
        public event Action<NetworkDisconnectedEvent> Disconnected;
        public event Action<RoomExitEvent> RoomExited;
        public event Action<SceneLoadDto> SceneLoadRequested;
        public event Action SceneInitializationAllowed;
        public event Action<PlayerInitializationDto> PlayerInitializationReceived;
        public event Action<PlayerPositionsDto> PlayerPositionsReceived;
        public event Action<MonsterInitializationDto> MonsterInitializationReceived;
        public event Action<MonsterStatesDto> MonsterStatesReceived;
        public event Action<DamageResultDto> DamageResultReceived;
        public event Action<MonsterDeathDto> MonsterDeathReceived;
        public event Action<PlayerDeathDto> PlayerDeathReceived;
        public event Action<DropSpawnDto> DropSpawnReceived;
        public event Action<PickupResultDto> PickupResultReceived;
        public event Action<EquipmentChangeResultDto> EquipmentChangeResultReceived;
        public event Action<CraftResultDto> CraftResultReceived;
        public event Action<SkillUnlockResultDto> SkillUnlockResultReceived;
        public event Action<FlaskUseResultDto> FlaskUseResultReceived;
        public event Action<SwordStatesDto> SwordStatesReceived;
        public event Action<CrystalStatesDto> CrystalStatesReceived;
        public event Action<CloneStatesDto> CloneStatesReceived;
        public event Action<BlackholeStatesDto> BlackholeStatesReceived;
        public event Action<ElementStatesDto> ElementStatesReceived;
        private long lastMonsterSeq;
        private ulong lastMonsterTick;
        private long lastPlayerInitializationSeq;
        private ulong lastPlayerInitializationTick;
        private readonly MovementInputSender movementSender = new MovementInputSender();
        public int MovementMinimumIntervalMilliseconds
        {
            get => movementSender.MinimumIntervalMilliseconds;
            set => movementSender.MinimumIntervalMilliseconds = value;
        }
        public long SentMovementInputSeq => movementSender.SentSequence;
        public long LastSentActionSeq { get; private set; }
        public long LastSentPrimaryAttackRequestSeq { get; private set; }
        private long inputLockSeq;
        private bool? sentInputLock;
        public void RequestMovementStop() => movementSender.RequestStop();
        public long SceneLoadId { get; private set; }
        public bool LocalSceneReady { get; private set; }
        public bool CanInitializeEntities { get; private set; }
        public RoomConnectionState State { get; private set; }
        public NetConnection Connection { get; private set; }
        public string FailureMessage { get; private set; }
        public string SceneInitializationDiagnostics => $"load={SceneLoadId} ready={CanInitializeEntities} playerSeq={lastPlayerInitializationSeq} monsterSeq={lastMonsterSeq} " +
            $"rejected={handshakePeer?.Network.RejectedCount ?? 0} lastProtocol={handshakePeer?.Network.LastRejectedProtocolId ?? 0} error={handshakePeer?.Network.LastMessageError?.Message ?? "none"}";

        public TcpRoomClient(int timeoutMilliseconds = 5000)
        {
            if (timeoutMilliseconds < 1) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            this.timeoutMilliseconds = timeoutMilliseconds;
        }

        public void Init() { }

        public void Connect(string address, int port)
        {
            if (disposed) throw new ObjectDisposedException(nameof(TcpRoomClient));
            if (State == RoomConnectionState.Connecting || Connection != null) return;
            if (!IPAddress.TryParse(address, out IPAddress ip)) { Fail("Invalid IP address"); return; }
            if (port < 1 || port > 65535) { Fail("Port must be 1-65535"); return; }
            FailureMessage = null;
            try { attempt = new Attempt(++generation, ip, port); }
            catch (Exception error) { Fail(Describe(error)); return; }
            State = RoomConnectionState.Connecting;
            StateChanged?.Invoke();
        }

        public void Update()
        {
            if (disposed) return;
            if (State == RoomConnectionState.Connected) { handshakePeer?.Update(); return; }
            if (State == RoomConnectionState.Handshaking)
            {
                handshakePeer.Update();
                if (State == RoomConnectionState.Handshaking)
                {
                    if (handshakePeer.Network.RejectedCount > 0)
                        FailHandshake("Invalid handshake response: " + handshakePeer.Network.LastMessageError?.Message);
                    else if (!Connection.IsOpen)
                        FailHandshake("Connection closed during handshake" +
                            (Connection.LastError != null ? ": " + Connection.LastError.Message : ""));
                    else if (handshakeClock.ElapsedMilliseconds >= timeoutMilliseconds)
                        FailHandshake("Handshake timed out: no confirmation from host");
                }
                return;
            }
            if (attempt == null) return;
            Attempt current = attempt;
            if (current.Id != generation) return;
            if (!current.Task.IsCompleted)
            {
                if (current.Elapsed.ElapsedMilliseconds >= timeoutMilliseconds)
                {
                    InvalidateAttempt();
                    Fail("Connection timed out");
                }
                return;
            }
            // Completion is applied only while this exact attempt is still current.
            if (!ReferenceEquals(attempt, current) || current.Id != generation) return;
            attempt = null;
            if (current.Task.IsFaulted)
            {
                current.Client.Close();
                Fail(Describe(current.Task.Exception.GetBaseException()));
                return;
            }
            if (current.Task.IsCanceled)
            {
                current.Client.Close();
                State = RoomConnectionState.Cancelled;
            }
            else
            {
                try
                {
                    Connection = NetConnection.Adopt(current.Client);
                    handshakePeer = new RoomHandshakePeer(Connection);
                    handshakePeer.Subscribe<HandshakeResultDto>(value => ApplyHandshake((HandshakeResultDto)value));
                    NetConnection boundConnection = Connection;
                    handshakePeer.Subscribe<SceneLoadDto>(value =>
                    {
                        var load = (SceneLoadDto)value;
                        if (!ReferenceEquals(Connection, boundConnection) || State != RoomConnectionState.Connected ||
                            load.roomSessionId != RoomSessionId || load.loadId <= SceneLoadId) return;
                        SceneLoadId = load.loadId;
                        lastPlayerInitializationSeq = 0;
                        lastPlayerInitializationTick = 0;
                        lastMonsterSeq = 0; lastMonsterTick = 0;
                        LocalSceneReady = CanInitializeEntities = false;
                        movementSender.Reset();
                        LastSentActionSeq = LastSentPrimaryAttackRequestSeq = 0;
                        inputLockSeq = 0; sentInputLock = null;
                        SceneLoadRequested?.Invoke(load);
                    });
                    handshakePeer.Subscribe<SceneStartDto>(value =>
                    {
                        var start = (SceneStartDto)value;
                        if (!ReferenceEquals(Connection, boundConnection) || State != RoomConnectionState.Connected ||
                            start.roomSessionId != RoomSessionId || start.loadId != SceneLoadId || !LocalSceneReady || CanInitializeEntities) return;
                        CanInitializeEntities = true;
                        SceneInitializationAllowed?.Invoke();
                    });
                    handshakePeer.Subscribe<RoomClosedDto>(value =>
                    {
                        var closed = (RoomClosedDto)value;
                        if (!ReferenceEquals(Connection, boundConnection)) return;
                        if (State == RoomConnectionState.Handshaking)
                        {
                            FailHandshake("Host closed room during handshake");
                            return;
                        }
                        if (State != RoomConnectionState.Connected || closed.roomSessionId != RoomSessionId) return;
                        var exit = new RoomExitEvent(RoomSessionId, boundConnection.SessionId, RoomExitReason.HostClosed, false);
                        Close();
                        StateChanged?.Invoke();
                        RoomExited?.Invoke(exit);
                    });
                    handshakePeer.Subscribe<PlayerInitializationDto>(value =>
                    {
                        var initialization = (PlayerInitializationDto)value;
                        if (!ReferenceEquals(Connection, boundConnection) || State != RoomConnectionState.Connected || !CanInitializeEntities ||
                            initialization.roomSessionId != RoomSessionId || initialization.loadId != SceneLoadId ||
                            initialization.stateSeq <= lastPlayerInitializationSeq || initialization.authorityTick < lastPlayerInitializationTick) return;
                        if (ProtocolCodec.FindPlayer(initialization, HostIdentity.PlayerId, HostIdentity.EntityId) == null ||
                            ProtocolCodec.FindPlayer(initialization, LocalIdentity.PlayerId, LocalIdentity.EntityId) == null)
                            throw new InvalidDataException("Initial player identities do not match A's admission result.");
                        lastPlayerInitializationSeq = initialization.stateSeq;
                        lastPlayerInitializationTick = initialization.authorityTick;
                        PlayerInitializationReceived?.Invoke(initialization);
                    });
                    handshakePeer.Subscribe<MonsterInitializationDto>(value =>
                    {
                        var monsters = (MonsterInitializationDto)value;
                        if (!ReferenceEquals(Connection, boundConnection) || State != RoomConnectionState.Connected || !CanInitializeEntities ||
                            lastPlayerInitializationSeq == 0 || monsters.roomSessionId != RoomSessionId || monsters.loadId != SceneLoadId ||
                            monsters.stateSeq <= lastMonsterSeq || monsters.authorityTick < lastMonsterTick) return;
                        foreach (var monster in monsters.monsters)
                            if (monster.entityId == HostIdentity.EntityId || monster.entityId == LocalIdentity.EntityId)
                                throw new InvalidDataException("Monster ID collides with admission identity.");
                        lastMonsterSeq = monsters.stateSeq; lastMonsterTick = monsters.authorityTick;
                        MonsterInitializationReceived?.Invoke(monsters);
                    });
                    handshakePeer.Subscribe<ElementStatesDto>(value =>
                    {
                        var burns = (ElementStatesDto)value;
                        if (!ReferenceEquals(Connection, boundConnection) || State != RoomConnectionState.Connected || !CanInitializeEntities ||
                            lastPlayerInitializationSeq == 0 || lastMonsterSeq == 0 || burns.roomSessionId != RoomSessionId) return;
                        ElementStatesReceived?.Invoke(burns);
                    });
                    handshakePeer.Subscribe<DamageResultDto>(value =>
                    {
                        var damage = (DamageResultDto)value;
                        if (!ReferenceEquals(Connection, boundConnection) || State != RoomConnectionState.Connected || !CanInitializeEntities ||
                            lastPlayerInitializationSeq == 0 || lastMonsterSeq == 0 || damage.roomSessionId != RoomSessionId) return;
                        DamageResultReceived?.Invoke(damage); // No global maximum ID filter; room result receipts decide equality.
                    });
                    handshakePeer.Subscribe<MonsterDeathDto>(value =>
                    {
                        var death = (MonsterDeathDto)value;
                        if (!ReferenceEquals(Connection, boundConnection) || State != RoomConnectionState.Connected || !CanInitializeEntities ||
                            lastMonsterSeq == 0 || death.roomSessionId != RoomSessionId) return;
                        MonsterDeathReceived?.Invoke(death);
                    });
                    handshakePeer.Subscribe<PlayerDeathDto>(value =>
                    {
                        var death = (PlayerDeathDto)value;
                        if (!ReferenceEquals(Connection, boundConnection) || State != RoomConnectionState.Connected || !CanInitializeEntities ||
                            lastPlayerInitializationSeq == 0 || death.roomSessionId != RoomSessionId) return;
                        PlayerDeathReceived?.Invoke(death);
                    });
                    handshakePeer.Subscribe<DropSpawnDto>(value =>
                    {
                        var drop = (DropSpawnDto)value;
                        if (!ReferenceEquals(Connection, boundConnection) || State != RoomConnectionState.Connected || !CanInitializeEntities ||
                            lastMonsterSeq == 0 || drop.roomSessionId != RoomSessionId) return;
                        DropSpawnReceived?.Invoke(drop);
                    });
                    handshakePeer.Subscribe<PickupResultDto>(value =>
                    {
                        var result = (PickupResultDto)value;
                        if (!ReferenceEquals(Connection, boundConnection) || State != RoomConnectionState.Connected || !CanInitializeEntities ||
                            lastPlayerInitializationSeq == 0 || result.roomSessionId != RoomSessionId) return;
                        PickupResultReceived?.Invoke(result);
                    });
                    handshakePeer.Subscribe<EquipmentChangeResultDto>(value =>
                    {
                        var result = (EquipmentChangeResultDto)value;
                        if (!ReferenceEquals(Connection, boundConnection) || State != RoomConnectionState.Connected || !CanInitializeEntities ||
                            lastPlayerInitializationSeq == 0 || result.roomSessionId != RoomSessionId) return;
                        EquipmentChangeResultReceived?.Invoke(result);
                    });
                    handshakePeer.Subscribe<CraftResultDto>(value =>
                    {
                        var result = (CraftResultDto)value;
                        if (!ReferenceEquals(Connection, boundConnection) || State != RoomConnectionState.Connected || !CanInitializeEntities ||
                            lastPlayerInitializationSeq == 0 || result.roomSessionId != RoomSessionId) return;
                        CraftResultReceived?.Invoke(result);
                    });
                    handshakePeer.Subscribe<SkillUnlockResultDto>(value =>
                    {
                        var result = (SkillUnlockResultDto)value;
                        if (!ReferenceEquals(Connection, boundConnection) || State != RoomConnectionState.Connected || !CanInitializeEntities ||
                            lastPlayerInitializationSeq == 0 || result.roomSessionId != RoomSessionId) return;
                        SkillUnlockResultReceived?.Invoke(result);
                    });
                    handshakePeer.Subscribe<FlaskUseResultDto>(value =>
                    {
                        var result = (FlaskUseResultDto)value;
                        if (!ReferenceEquals(Connection, boundConnection) || State != RoomConnectionState.Connected || !CanInitializeEntities ||
                            lastPlayerInitializationSeq == 0 || result.roomSessionId != RoomSessionId) return;
                        FlaskUseResultReceived?.Invoke(result);
                    });
                    SubscribeSkillStates<SwordStatesDto>(boundConnection, result => SwordStatesReceived?.Invoke(result));
                    SubscribeSkillStates<CrystalStatesDto>(boundConnection, result => CrystalStatesReceived?.Invoke(result));
                    SubscribeSkillStates<CloneStatesDto>(boundConnection, result => CloneStatesReceived?.Invoke(result));
                    SubscribeSkillStates<BlackholeStatesDto>(boundConnection, result => BlackholeStatesReceived?.Invoke(result));
                    handshakePeer.Subscribe<MonsterStatesDto>(value =>
                    {
                        var states = (MonsterStatesDto)value;
                        if (!ReferenceEquals(Connection, boundConnection) || State != RoomConnectionState.Connected || !CanInitializeEntities ||
                            lastMonsterSeq == 0 || states.roomSessionId != RoomSessionId || states.loadId != SceneLoadId) return;
                        MonsterStatesReceived?.Invoke(states); // Per-monster position streams handle tick/sequence freshness.
                    });
                    handshakePeer.Subscribe<PlayerPositionsDto>(value =>
                    {
                        var positions = (PlayerPositionsDto)value;
                        if (!ReferenceEquals(Connection, boundConnection) || State != RoomConnectionState.Connected ||
                            !CanInitializeEntities || lastPlayerInitializationSeq == 0 || positions.roomSessionId != RoomSessionId ||
                            positions.loadId != SceneLoadId || HostIdentity == null || LocalIdentity == null ||
                            ProtocolCodec.FindPosition(positions, HostIdentity.EntityId) == null ||
                            ProtocolCodec.FindPosition(positions, LocalIdentity.EntityId) == null) return;
                        // No global sequence shared with health/HUD/input; display sorts per entity/position stream.
                        PlayerPositionsReceived?.Invoke(positions);
                    });
                    handshakePeer.Disconnected += value =>
                    {
                        if (!ReferenceEquals(Connection, boundConnection) || State != RoomConnectionState.Connected) return;
                        Close();
                        State = RoomConnectionState.Disconnected;
                        FailureMessage = value.Detail ?? "Host disconnected";
                        StateChanged?.Invoke();
                        Disconnected?.Invoke(value);
                    };
                    State = RoomConnectionState.Handshaking;
                    handshakeClock = Stopwatch.StartNew();
                    Connection.Send(new HandshakeHelloDto { schemaVersion = ProtocolSchema.SchemaVersion });
                }
                catch (Exception error) { current.Client.Close(); FailHandshake(Describe(error)); return; }
            }
            StateChanged?.Invoke();
        }

        public void CancelConnect()
        {
            if (disposed || (State != RoomConnectionState.Connecting && State != RoomConnectionState.Handshaking)) return;
            Close();
            State = RoomConnectionState.Cancelled;
            FailureMessage = null;
            StateChanged?.Invoke();
        }

        private void ApplyHandshake(HandshakeResultDto result)
        {
            if (State != RoomConnectionState.Handshaking) return;
            if (result.schemaVersion != ProtocolSchema.SchemaVersion)
            {
                FailHandshake("Protocol version mismatch");
                return;
            }
            if (!result.accepted)
            {
                FailHandshake(result.rejection == HandshakeRejection.RoomFull ? "Room is full" :
                    result.rejection == HandshakeRejection.VersionMismatch ? "Protocol version mismatch" : "Handshake rejected");
                return;
            }
            RoomSessionId = result.roomSessionId;
            HostIdentity = new RoomPlayerIdentity(RoomSessionId, result.hostPlayerId, result.hostEntityId);
            LocalIdentity = new RoomPlayerIdentity(RoomSessionId, result.guestPlayerId, result.guestEntityId);
            handshakePeer.ConfirmRoom(RoomSessionId);
            State = RoomConnectionState.Connected;
            GameSimulationClock.Acquire(this, false);
            StateChanged?.Invoke();
        }

        private void FailHandshake(string message)
        {
            Close();
            Fail(message);
        }

        private void InvalidateAttempt()
        {
            ++generation;
            Attempt previous = attempt;
            attempt = null;
            previous?.Client.Close();
        }

        private void Fail(string message)
        {
            FailureMessage = message;
            State = RoomConnectionState.Failed;
            StateChanged?.Invoke();
        }

        private static string Describe(Exception error)
        {
            if (error is SocketException socket)
            {
                if (socket.SocketErrorCode == SocketError.ConnectionRefused) return "Connection refused";
                if (socket.SocketErrorCode == SocketError.TimedOut) return "Connection timed out";
                return "Connection failed: " + socket.SocketErrorCode;
            }
            return "Connection failed: " + error.Message;
        }

        public void Close()
        {
            InvalidateAttempt();
            handshakePeer?.Dispose();
            handshakePeer = null;
            Connection?.Dispose();
            Connection = null;
            RoomSessionId = null;
            HostIdentity = LocalIdentity = null;
            handshakeClock = null;
            SceneLoadId = 0;
            lastPlayerInitializationSeq = 0;
            lastPlayerInitializationTick = 0;
            lastMonsterSeq = 0; lastMonsterTick = 0;
            LocalSceneReady = CanInitializeEntities = false;
            movementSender.Reset();
            LastSentActionSeq = 0;
            LastSentPrimaryAttackRequestSeq = 0;
            inputLockSeq = 0; sentInputLock = null;
            FailureMessage = null;
            State = RoomConnectionState.Idle;
            GameSimulationClock.Release(this);
        }

        public void MarkSceneReady(string actualScene)
        {
            if (State != RoomConnectionState.Connected || SceneLoadId <= 0 || actualScene != RoomScene.Name)
                throw new InvalidOperationException("B must initialize the requested MainScene first.");
            if (LocalSceneReady) return;
            LocalSceneReady = true;
            Connection.Send(new SceneReadyDto { roomSessionId = RoomSessionId, loadId = SceneLoadId, sceneName = RoomScene.Name }, true);
        }

        // Called after this frame's input collection. Timer uses the non-scaled monotonic clock.
        public bool SendMovementInput(float currentAxis, bool slideDown = false)
        {
            if (State != RoomConnectionState.Connected || !CanInitializeEntities || lastPlayerInitializationSeq == 0 ||
                LocalIdentity == null || Connection == null || !Connection.IsOpen) return false;
            return movementSender.TrySend(currentAxis, MonotonicClock.Now, value => Connection.Send(value, true), slideDown);
        }

        public bool SendPrimaryAttackRequest(long requestSeq)
        {
            if (requestSeq <= 0 || requestSeq <= LastSentPrimaryAttackRequestSeq || State != RoomConnectionState.Connected ||
                !CanInitializeEntities || lastPlayerInitializationSeq == 0 || LocalIdentity == null ||
                Connection == null || !Connection.IsOpen) return false;
            try
            {
                Connection.Send(new PrimaryAttackRequestDto { requestSeq = requestSeq }, true);
                LastSentPrimaryAttackRequestSeq = requestSeq;
                return true;
            }
            catch (Exception error) when (error is IOException || error is SocketException ||
                error is ObjectDisposedException || error is InvalidOperationException) { return false; }
        }

        public bool SendEquipmentChangeRequest(EquipmentChangeRequestDto request)
        {
            if (!ProtocolCodec.ValidEquipmentRequest(request) || request.roomSessionId != RoomSessionId ||
                State != RoomConnectionState.Connected || !CanInitializeEntities || lastPlayerInitializationSeq == 0 ||
                LocalIdentity == null || Connection == null || !Connection.IsOpen) return false;
            try { Connection.Send(request, true); return true; }
            catch (Exception error) when (error is IOException || error is SocketException || error is ObjectDisposedException || error is InvalidOperationException) { return false; }
        }
        public bool SendCraftRequest(CraftRequestDto request)
        {
            if (!ProtocolCodec.ValidCraftRequest(request) || request.roomSessionId != RoomSessionId ||
                State != RoomConnectionState.Connected || !CanInitializeEntities || lastPlayerInitializationSeq == 0 ||
                LocalIdentity == null || Connection == null || !Connection.IsOpen) return false;
            try { Connection.Send(request, true); return true; }
            catch (Exception error) when (error is IOException || error is SocketException || error is ObjectDisposedException || error is InvalidOperationException) { return false; }
        }
        public bool SendPickupRequest(PickupRequestDto request)
        {
            if (!ProtocolCodec.ValidPickupRequest(request) || request.roomSessionId != RoomSessionId ||
                State != RoomConnectionState.Connected || !CanInitializeEntities || lastPlayerInitializationSeq == 0 ||
                LocalIdentity == null || Connection == null || !Connection.IsOpen) return false;
            try { Connection.Send(request, true); return true; }
            catch (Exception error) when (error is IOException || error is SocketException || error is ObjectDisposedException || error is InvalidOperationException) { return false; }
        }
        public bool SendRetryReady(RoomRetryReadyDto ready)
        {
            if (!ProtocolCodec.ValidRetryReady(ready) || ready.roomSessionId != RoomSessionId || ready.loadId != SceneLoadId ||
                State != RoomConnectionState.Connected || !CanInitializeEntities || lastPlayerInitializationSeq == 0 ||
                LocalIdentity == null || Connection == null || !Connection.IsOpen) return false;
            try { Connection.Send(ready, true); return true; }
            catch (Exception error) when (error is IOException || error is SocketException || error is ObjectDisposedException || error is InvalidOperationException) { return false; }
        }
        public bool SendSkillUnlockRequest(SkillUnlockRequestDto request)
        {
            if (!ProtocolCodec.ValidSkillUnlockRequest(request) || request.roomSessionId != RoomSessionId ||
                State != RoomConnectionState.Connected || !CanInitializeEntities || lastPlayerInitializationSeq == 0 ||
                LocalIdentity == null || Connection == null || !Connection.IsOpen) return false;
            try { Connection.Send(request, true); return true; }
            catch (Exception error) when (error is IOException || error is SocketException || error is ObjectDisposedException || error is InvalidOperationException) { return false; }
        }
        public bool SendFlaskUseRequest(FlaskUseRequestDto request)
        {
            if (!ProtocolCodec.ValidFlaskRequest(request) || request.roomSessionId != RoomSessionId ||
                State != RoomConnectionState.Connected || !CanInitializeEntities || lastPlayerInitializationSeq == 0 ||
                LocalIdentity == null || Connection == null || !Connection.IsOpen) return false;
            try { Connection.Send(request, true); return true; }
            catch (Exception error) when (error is IOException || error is SocketException || error is ObjectDisposedException || error is InvalidOperationException) { return false; }
        }

        public bool SendSkillInput<T>(T input) where T : RoomSkillInputDto
        {
            if (input.roomSessionId != RoomSessionId || State != RoomConnectionState.Connected || !CanInitializeEntities ||
                lastPlayerInitializationSeq == 0 || Connection == null || !Connection.IsOpen) return false;
            try { Connection.Send(input, true); return true; }
            catch (Exception error) when (error is IOException || error is SocketException || error is ObjectDisposedException || error is InvalidOperationException) { return false; }
        }
        private void SubscribeSkillStates<T>(NetConnection boundConnection, Action<T> handler) where T : RoomSkillStatesDto
        {
            handshakePeer.Subscribe<T>(value =>
            {
                var result = (T)value;
                if (ReferenceEquals(Connection, boundConnection) && State == RoomConnectionState.Connected && CanInitializeEntities &&
                    lastPlayerInitializationSeq > 0 && result.roomSessionId == RoomSessionId) handler(result);
            });
        }


        public bool SendJumpRequest(long actionSeq)
        {
            return SendMovementAction(actionSeq, false, 0);
        }

        public bool SendLocalInputLock(bool locked)
        {
            if (locked && sentInputLock != true) RequestMovementStop();
            if (State != RoomConnectionState.Connected || !CanInitializeEntities || lastPlayerInitializationSeq == 0 ||
                Connection == null || !Connection.IsOpen) return false;
            if (sentInputLock == locked) return true;
            try
            {
                Connection.Send(new LocalInputLockDto { lockSeq = checked(inputLockSeq + 1), locked = locked }, true);
                inputLockSeq++; sentInputLock = locked;
                return true;
            }
            catch (Exception error) when (error is IOException || error is SocketException ||
                error is ObjectDisposedException || error is InvalidOperationException) { return false; }
        }

        public bool SendDashRequest(long actionSeq, float axis)
        {
            return SendMovementAction(actionSeq, true, axis);
        }

        private bool SendMovementAction(long actionSeq, bool dash, float axis)
        {
            if (actionSeq <= 0 || actionSeq <= LastSentActionSeq ||
                State != RoomConnectionState.Connected || !CanInitializeEntities || lastPlayerInitializationSeq == 0 ||
                LocalIdentity == null || Connection == null || !Connection.IsOpen) return false;
            if (dash) Connection.Send(new DashRequestDto { actionSeq = actionSeq, currentAxis = axis }, true);
            else Connection.Send(new JumpRequestDto { actionSeq = actionSeq }, true);
            LastSentActionSeq = actionSeq;
            return true;
        }

        public void ReportSceneFailure()
        {
            try
            {
                if (State == RoomConnectionState.Connected && SceneLoadId > 0)
                    Connection.Send(new SceneFailedDto { roomSessionId = RoomSessionId, loadId = SceneLoadId, sceneName = RoomScene.Name }, true);
            }
            finally { LeaveRoom(); }
        }

        public void LeaveRoom()
        {
            if (State != RoomConnectionState.Connected || Connection == null) { Close(); return; }
            var exit = new RoomExitEvent(RoomSessionId, Connection.SessionId, RoomExitReason.GuestLeft, true);
            try
            {
                if (Connection.IsOpen) Connection.Send(new GuestLeaveDto { roomSessionId = RoomSessionId }, true);
            }
            catch (Exception error) when (error is IOException || error is SocketException ||
                error is ObjectDisposedException || error is InvalidOperationException) { }
            finally { Close(); }
            StateChanged?.Invoke();
            RoomExited?.Invoke(exit);
        }

        public void Dispose()
        {
            if (disposed) return;
            LeaveRoom();
            disposed = true;
            StateChanged = null;
            Disconnected = null;
            RoomExited = null;
            SceneLoadRequested = null;
            SceneInitializationAllowed = null;
            PlayerInitializationReceived = null;
            PlayerPositionsReceived = null;
            MonsterInitializationReceived = null;
            MonsterStatesReceived = null;
            DamageResultReceived = null;
            MonsterDeathReceived = null;
            PlayerDeathReceived = null;
            DropSpawnReceived = null;
            PickupResultReceived = null;
            EquipmentChangeResultReceived = null;
            CraftResultReceived = null;
            SkillUnlockResultReceived = null;
            FlaskUseResultReceived = null;
            SwordStatesReceived = null;
            CrystalStatesReceived = null;
            CloneStatesReceived = null;
            BlackholeStatesReceived = null;
        }
    }
}
