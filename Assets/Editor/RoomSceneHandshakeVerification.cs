using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Rpg.Networking;
using Rpg.Networking.Protocol;
using UnityEditor;
using UnityEngine;

public static class RoomSceneHandshakeVerification
{
    private static TcpRoomHost host;
    private static TcpRoomClient client;
    private static Stopwatch timer;
    private static int stage, requests, hostStarts, clientStarts, initializations;
    private static int movements;
    private static int wallSlideInputs;
    private static bool slideDown;
    private static int jumpRequests;
    private static int dashRequests;
    private static int primaryAttackRequests;
    private static int damageResults;
    private static int inputLocks;
    private static bool inputLocked;
    private static int positionSnapshots;
    private static float movementAxis;
    private static double waitingSince;

    [MenuItem("Tools/RPG/Verify Room Scene Handshake")]
    public static void Verify()
    {
        if (Application.isPlaying || timer != null) throw new InvalidOperationException("Run once outside Play mode.");
        try
        {
            stage = requests = hostStarts = clientStarts = initializations = 0;
            movements = 0; movementAxis = 0;
            wallSlideInputs = 0; slideDown = false;
            jumpRequests = 0;
            dashRequests = 0;
            primaryAttackRequests = 0;
            damageResults = 0;
            inputLocks = 0; inputLocked = false;
            positionSnapshots = 0;
            host = new TcpRoomHost(); client = new TcpRoomClient();
            host.Init(); client.Init();
            host.SceneInitializationAllowed += () => hostStarts++;
            host.InputLockReceived += (actor, message) =>
            {
                Require(actor == host.GuestIdentity, "Input lock belongs to the admitted guest connection");
                inputLocks++; inputLocked = message.locked;
            };
            host.WallSlideInputReceived += (actor, input) =>
            {
                Require(actor == host.GuestIdentity && actor.PlayerId != host.HostIdentity.PlayerId,
                    "Wall-slide input uses actual connection ownership");
                wallSlideInputs++; slideDown = input.slideDown;
            };
            host.JumpRequested += (actor, request) =>
            {
                Require(actor == host.GuestIdentity && actor.PlayerId != host.HostIdentity.PlayerId,
                    "The admitted connection determines the jump actor");
                jumpRequests++;
            };
            host.DashRequested += (actor, request) =>
            {
                Require(actor == host.GuestIdentity && actor.PlayerId != host.HostIdentity.PlayerId,
                    "Dash actor is resolved from the admitted connection");
                dashRequests++;
            };
            host.PrimaryAttackRequested += (actor, request) =>
            {
                Require(ReferenceEquals(actor, host.GuestIdentity) && actor.PlayerId != host.HostIdentity.PlayerId,
                    "Primary attack operator comes from its actual guest connection");
                primaryAttackRequests++;
            };
            host.MovementInputReceived += (actor, input) =>
            {
                Require(actor == host.GuestIdentity && actor.PlayerId != host.HostIdentity.PlayerId,
                    "Actual guest connection determines movement ownership");
                movements++;
                movementAxis = input.currentAxis;
            };
            client.SceneInitializationAllowed += () => clientStarts++;
            client.DamageResultReceived += result =>
            {
                Require(result.roomSessionId == host.RoomSessionId && result.sourceEntityId == host.HostIdentity.EntityId,
                    "Damage results arrive from A's bound connection with the room envelope");
                damageResults++;
            };
            client.PlayerPositionsReceived += value =>
            {
                Require(value.roomSessionId == client.RoomSessionId && value.loadId == client.SceneLoadId &&
                    ProtocolCodec.FindPosition(value, client.LocalIdentity.EntityId) != null && value.authorityTick == 200,
                    "Position snapshot uses the admitted room, load and two assigned entities");
                positionSnapshots++;
            };
            client.PlayerInitializationReceived += value =>
            {
                Require(value.roomSessionId == client.RoomSessionId && value.loadId == client.SceneLoadId &&
                    ProtocolCodec.FindPlayer(value, client.LocalIdentity.PlayerId, client.LocalIdentity.EntityId) != null,
                    "Initial state is tied to the admitted player and scene load");
                initializations++;
            };
            client.SceneLoadRequested += request =>
            {
                Require(request.sceneName == RoomScene.Name && request.roomSessionId == host.RoomSessionId, "A selects the fixed room scene");
                requests++;
            };
            var reservation = new TcpListener(IPAddress.Any, 0);
            int port;
            try { reservation.Start(); port = ((IPEndPoint)reservation.LocalEndpoint).Port; }
            finally { reservation.Stop(); }
            host.StartListening(port);
            client.Connect("127.0.0.1", port);
            timer = Stopwatch.StartNew();
            EditorApplication.update += Check;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.quitting += Cleanup;
        }
        catch { Cleanup(); throw; }
    }

    private static void Check()
    {
        try
        {
            Require(timer.Elapsed.TotalSeconds < 12, "Scene handshake timed out");
            host.Update(); client.Update();
            if (stage == 0)
            {
                if (client.State == RoomConnectionState.Connecting || client.State == RoomConnectionState.Handshaking) return;
                Require(client.State == RoomConnectionState.Connected, "Admitted before scene messages");
                host.BeginSceneLoad();
                long id = host.SceneLoadId;
                host.BeginSceneLoad();
                Require(host.SceneLoadId == id && !host.CanInitializeEntities && !client.CanInitializeEntities,
                    "Duplicate load does not start an entity phase");
                stage = 1;
            }
            else if (stage == 1)
            {
                if (requests == 0) return;
                Require(requests == 1 && client.SceneLoadId == host.SceneLoadId, "Scene epoch agreement");
                bool wrongSceneRejected = false;
                try { client.MarkSceneReady("WrongScene"); }
                catch (InvalidOperationException) { wrongSceneRejected = true; }
                Require(wrongSceneRejected && !client.LocalSceneReady, "Wrong scene cannot report ready");
                host.MarkSceneReady(RoomScene.Name);
                bool earlyInitializationRejected = false;
                try { host.SendPlayerInitialization(MakeInitialization(1, 100)); }
                catch (InvalidOperationException) { earlyInitializationRejected = true; }
                Require(earlyInitializationRejected, "A cannot publish players before the scene-ready barrier");
                host.Guest.Send(MakeInitialization(1, 100), true);
                host.Guest.Send(MakePositions(), true);
                client.Connection.Send(new SceneReadyDto
                {
                    roomSessionId = Guid.NewGuid().ToString("N"), loadId = host.SceneLoadId, sceneName = RoomScene.Name
                }, true);
                client.Connection.Send(new MovementInputDto { inputSeq = 100, currentAxis = 1 }, true);
                client.Connection.Send(new JumpRequestDto { actionSeq = 100 }, true);
                client.Connection.Send(new DashRequestDto { actionSeq = 101, currentAxis = 1 }, true);
                client.Connection.Send(new LocalInputLockDto { lockSeq = 100, locked = true }, true);
                client.Connection.Send(new WallSlideInputDto { inputSeq = 100, slideDown = true }, true);
                waitingSince = MonotonicClock.Now;
                stage = 2;
            }
            else if (stage == 2)
            {
                Require(!host.CanInitializeEntities && !client.CanInitializeEntities && hostStarts == 0 && clientStarts == 0 && initializations == 0,
                    "A ready alone and stale-room B ready cannot open initialization");
                Require(movements == 0 && host.LastMovementInputSeq == 0, "Input before player initialization is ignored without consuming sequence");
                Require(jumpRequests == 0 && host.LastActionSeq == 0, "Jump cannot enter gameplay before initialization");
                Require(dashRequests == 0, "Dash cannot enter before initialization");
                Require(inputLocks == 0 && host.LastInputLockSeq == 0, "Input lock cannot enter before initialization");
                Require(wallSlideInputs == 0 && host.LastWallSlideInputSeq == 0, "Wall-slide input cannot enter before initialization");
                Require(positionSnapshots == 0, "Positions before initialization cannot reach display handlers");
                if (MonotonicClock.Now - waitingSince < 0.4) return;
                client.MarkSceneReady(RoomScene.Name);
                client.MarkSceneReady(RoomScene.Name);
                stage = 3;
            }
            else if (stage == 3)
            {
                if (!host.CanInitializeEntities || !client.CanInitializeEntities) return;
                host.MarkSceneReady(RoomScene.Name);
                Require(hostStarts == 1 && clientStarts == 1 && host.LocalSceneReady && host.GuestSceneReady,
                    "Exactly one initialization permission after both ready");
                var initial = MakeInitialization(1, 100);
                host.SendPlayerInitialization(initial);
                host.Guest.Send(initial, true); // Duplicate baseline must not dispatch twice.
                initial = MakeInitialization(2, 99);
                host.Guest.Send(initial, true); // New sequence with an older authority tick is still stale.
                initial = MakeInitialization(3, 101);
                initial.roomSessionId = Guid.NewGuid().ToString("N");
                host.Guest.Send(initial, true);
                initial = MakeInitialization(4, 102);
                initial.loadId++;
                host.Guest.Send(initial, true);
                initial = MakeInitialization(5, 103);
                initial.players[0].playerId += 100;
                host.Guest.Send(initial, true); // A valid JSON shape cannot replace admission identity.
                host.SendPlayerInitialization(MakeInitialization(6, 104));
                var positions = MakePositions();
                positions.roomSessionId = Guid.NewGuid().ToString("N");
                host.Guest.Send(positions, true);
                positions = MakePositions(); positions.loadId++;
                host.Guest.Send(positions, true);
                positions = MakePositions(); positions.players[0].entityId += 100;
                host.Guest.Send(positions, true);
                Require(host.SendPlayerPositions(MakePositions()), "A publishes both initialized authoritative positions");
                waitingSince = MonotonicClock.Now;
                stage = 4;
            }
            else if (stage == 4)
            {
                if (MonotonicClock.Now - waitingSince < .3) return;
                Require(initializations == 2, "Only the two valid new baselines dispatch; duplicates, stale ticks, sessions, loads and identities are isolated");
                Require(positionSnapshots == 1, "Wrong-room, old-load and unknown-entity position snapshots are isolated");
                long lateResult = host.Results.Allocate();
                host.Results.Allocate(); // Private future result: B must not wait for this missing number.
                var damage = new DamageResultDto
                {
                    roomSessionId = host.RoomSessionId, resultId = host.Results.Allocate(), authorityTick = 105,
                    sourcePlayerId = host.HostIdentity.PlayerId, sourceEntityId = host.HostIdentity.EntityId,
                    targetEntityId = 333, attackInstanceId = 1, hitSegmentId = 1, actualDamage = 10,
                    finalHealth = 80, maxHealth = 100, targetStateVersion = 3
                };
                Require(!host.SendDamageResult(damage), "A waits for the monster baseline before publishing damage");
                host.SendMonsterInitialization(new MonsterInitializationDto
                {
                    roomSessionId = host.RoomSessionId, loadId = host.SceneLoadId, authorityTick = 104, stateSeq = 1,
                    monsters = new[] { new InitialMonsterDto { entityId = 333, sceneKey = "Fixture[0]", monsterType = "EnemySkeleton",
                        position = new[] { 0f, 0f }, facingDir = 1, active = true, health = 100, maxHealth = 100, healthVersion = 1 } }
                });
                Require(host.SendDamageResult(damage), "Damage result publishes after all target identities/health baselines");
                host.Guest.Send(damage, true); // Exact replay is delivered to the result receipt gate.
                damage.resultId = lateResult; damage.authorityTick = 104; damage.targetStateVersion = 2; damage.finalHealth = 90;
                host.Guest.Send(damage, true); // Smaller, previously unsent ID is not filtered by the client connection.
                foreach (var input in new[]
                {
                    new MovementInputDto { inputSeq = 10, currentAxis = .5f },
                    new MovementInputDto { inputSeq = 10, currentAxis = -1 },
                    new MovementInputDto { inputSeq = 9, currentAxis = -1 },
                    new MovementInputDto { inputSeq = 12, currentAxis = 0 },
                    new MovementInputDto { inputSeq = 11, currentAxis = 1 },
                    new MovementInputDto { inputSeq = 15, currentAxis = .5f }
                }) client.Connection.Send(input, true);
                foreach (long actionSeq in new long[] { 1, 1, 3, 2, 4 })
                    client.Connection.Send(new JumpRequestDto { actionSeq = actionSeq }, true);
                Require(client.SendDashRequest(5, -1) && !client.SendDashRequest(5, 1), "Client sends a dash press only once");
                client.Connection.Send(new DashRequestDto { actionSeq = 3, currentAxis = 1 }, true);
                Require(client.SendDashRequest(7, 0), "A later dash sequence may contain gaps");
                Require(client.SendPrimaryAttackRequest(1) && !client.SendPrimaryAttackRequest(1), "Attack press sends once, separate from jump/dash sequences");
                foreach (long seq in new long[] { 1, 3, 2, 5 })
                    client.Connection.Send(new PrimaryAttackRequestDto { requestSeq = seq }, true);
                foreach (var inputLock in new[]
                {
                    new LocalInputLockDto { lockSeq = 1, locked = true },
                    new LocalInputLockDto { lockSeq = 1, locked = false },
                    new LocalInputLockDto { lockSeq = 3, locked = false },
                    new LocalInputLockDto { lockSeq = 2, locked = true }
                }) client.Connection.Send(inputLock, true);
                foreach (var input in new[]
                {
                    new WallSlideInputDto { inputSeq = 1, slideDown = true },
                    new WallSlideInputDto { inputSeq = 1, slideDown = false },
                    new WallSlideInputDto { inputSeq = 3, slideDown = false },
                    new WallSlideInputDto { inputSeq = 2, slideDown = true }
                }) client.Connection.Send(input, true);
                waitingSince = MonotonicClock.Now;
                stage = 5;
            }
            else
            {
                if (MonotonicClock.Now - waitingSince < .3) return;
                Require(movements == 3 && host.LastMovementInputSeq == 15 && movementAxis == .5f,
                    "Only newer guest states dispatch; sequence gaps are allowed, stale/duplicate inputs cannot overwrite");
                Require(jumpRequests == 3 && dashRequests == 2 && host.LastActionSeq == 7,
                    "Jump actions deduplicate independently of movement input sequences");
                Require(wallSlideInputs == 2 && host.LastWallSlideInputSeq == 3 && !slideDown,
                    "Wall-slide release replaces hold; old/duplicate inputs and other input streams cannot override it");
                Require(inputLocks == 2 && host.LastInputLockSeq == 3 && !inputLocked, "Old lock notifications cannot undo a newer unlock");
                Require(damageResults == 3, "Client routing preserves duplicate and out-of-order result IDs for exact receipt/state handling");
                Require(primaryAttackRequests == 3 && host.LastPrimaryAttackRequestSeq == 5 && host.LastActionSeq == 7,
                    "Attack requests deduplicate in their own stream without consuming jump/dash sequences");
                client.LeaveRoom();
                Require(client.LastSentActionSeq == 0, "Leaving clears the client's action send state");
                Require(client.LastSentPrimaryAttackRequestSeq == 0,
                    "Leaving clears the client's attack request stream");
                Require(!client.CanInitializeEntities && !client.LocalSceneReady, "Leaving clears scene readiness");
                UnityEngine.Debug.Log("[RoomSceneHandshake] PASS: scene/player baseline and position room/load/entity isolation; movement ownership and sequence rejection. Actual movement/position display needs dual-instance acceptance.");
                Cleanup();
            }
        }
        catch (Exception error) { Cleanup(); UnityEngine.Debug.LogException(error); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[RoomSceneHandshake] FAIL: " + message);
    }

    private static PlayerInitializationDto MakeInitialization(long seq, ulong tick) => new PlayerInitializationDto
    {
        roomSessionId = host.RoomSessionId, loadId = host.SceneLoadId, stateSeq = seq, authorityTick = tick,
        players = new[]
        {
            new InitialPlayerDto { facingDir = 1, playerId = host.HostIdentity.PlayerId, entityId = host.HostIdentity.EntityId, position = new[] { -9.96f, -1.75f }, health = 125, maxHealth = 140, healthVersion = 1,
                combat = RoomCombatConfiguration.CreateTestProfile(true) },
            new InitialPlayerDto { facingDir = 1, playerId = host.GuestIdentity.PlayerId, entityId = host.GuestIdentity.EntityId, position = new[] { -7.46f, -1.75f }, health = 125, maxHealth = 180, healthVersion = 1,
                combat = RoomCombatConfiguration.CreateTestProfile(false) }
        }
    };

    private static PlayerPositionsDto MakePositions() => new PlayerPositionsDto
    {
        roomSessionId = host.RoomSessionId, loadId = host.SceneLoadId, authorityTick = 200, stateSeq = 7,
        players = new[]
        {
            new PlayerPositionDto { facingDir = 1, entityId = host.GuestIdentity.EntityId, position = new[] { 2.5f, -1f } },
            new PlayerPositionDto { facingDir = 1, entityId = host.HostIdentity.EntityId, position = new[] { 0f, -1f } }
        }
    };

    private static void Cleanup()
    {
        EditorApplication.update -= Check;
        AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
        EditorApplication.quitting -= Cleanup;
        try { client?.Dispose(); }
        finally { host?.Dispose(); host = null; client = null; timer = null; }
    }
}
