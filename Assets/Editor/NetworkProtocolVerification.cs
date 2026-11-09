using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using Rpg.Networking;
using Rpg.Networking.Protocol;
using UnityEditor;
using UnityEngine;

public static class NetworkProtocolVerification
{
    [MenuItem("Tools/RPG/Verify Network Packet and Protocol")]
    public static void Verify()
    {
        NetPacketVerificationCases.Run();
        string text = "中文验证🙂\n引号\"与反斜杠\\";
        var echo = (VerificationEchoDto)RoundTrip(new VerificationEchoDto { text = text });
        NetPacketVerificationCases.Require(echo.text == text, "UTF-8 JSON DTO round trip");
        var hello = (HandshakeHelloDto)RoundTrip(new HandshakeHelloDto { schemaVersion = ProtocolSchema.SchemaVersion });
        NetPacketVerificationCases.Require(hello.schemaVersion == ProtocolSchema.SchemaVersion, "Single schema version round trip");
        // Codec validates the bootstrap shape; version compatibility is decided by 4-08.
        hello = (HandshakeHelloDto)RoundTrip(new HandshakeHelloDto { schemaVersion = ProtocolSchema.SchemaVersion + 1 });
        NetPacketVerificationCases.Require(hello.schemaVersion == ProtocolSchema.SchemaVersion + 1, "Incompatible version reaches handshake handler");

        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(new object()));
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(new HandshakeHelloDto()));
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(new VerificationEchoDto()));
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(new VerificationEchoDto { text = new string('中', NetPacket.MaxBodyLength / 3) }));
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Decode(32000, Encoding.UTF8.GetBytes("{}")));
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Decode(32000, Encoding.UTF8.GetBytes("{}"), true));
        foreach (string json in new[] { "", "[]", "null", "{}", "{\"schemaVersion\":0}", "{\"schemaVersion\":-1}", "{broken}" })
            NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Decode((short)ProtocolId.HandshakeHello, Encoding.UTF8.GetBytes(json)));
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Decode((short)ProtocolId.VerificationEcho, Encoding.UTF8.GetBytes("{\"schemaVersion\":1}")));
        NetPacketVerificationCases.Expect<DecoderFallbackException>(() => ProtocolCodec.Decode((short)ProtocolId.VerificationEcho, new byte[] { 0xC3, 0x28 }));
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Decode((short)ProtocolId.VerificationEcho, new byte[NetPacket.MaxBodyLength - 1]));
        VerifyInitialPlayers();
        VerifyMovementInput();
        VerifyPlayerPositions();
        VerifyJumpRequest();
        VerifyWallSlideInput();
        VerifyDashRequest();
        VerifyPrimaryAttackRequest();
        VerifyLocalInputLock();
        VerifyMonsterInitialization();
        VerifyMonsterStates();
        Debug.Log("[NetworkProtocol] PASS: framing, JSON, DTO/schema validation, movement input timing and two-player position snapshots. No sockets or scenes used.");
    }

    private static void VerifyPrimaryAttackRequest()
    {
        var request = new PrimaryAttackRequestDto { requestSeq = 42 };
        var copy = (PrimaryAttackRequestDto)RoundTrip(request, true);
        RequireSend(copy.requestSeq == 42, "Ordinary attack request uses the sole DTO registry");
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(request));
        foreach (long seq in new long[] { 0, -1 })
            NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(new PrimaryAttackRequestDto { requestSeq = seq }, true));
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Decode((short)ProtocolId.PrimaryAttackRequest, Encoding.UTF8.GetBytes("{}"), true));
    }

    private static void VerifyMonsterStates()
    {
        var value = new MonsterStatesDto
        {
            roomSessionId = Guid.NewGuid().ToString("N"), loadId = 1, authorityTick = 105, stateSeq = 2,
            monsters = new[] {
                new MonsterStateDto { entityId = 3, position = new[] { 1f, 2f }, facingDir = -1, aiState = MonsterAiState.Patrol, moving = true, active = true },
                new MonsterStateDto { entityId = 4, position = new[] { 3f, 2f }, facingDir = 1, aiState = MonsterAiState.Chase, active = true }
            }
        };
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value));
        var copy = (MonsterStatesDto)RoundTrip(value, true);
        RequireSend(copy.authorityTick == 105 && copy.stateSeq == 2 && copy.monsters[0].moving && copy.monsters[1].aiState == MonsterAiState.Chase,
            "Monster AI snapshots carry a common authority tick/sequence and independent per-monster animation state");
        var attacking = value.monsters[1];
        attacking.aiState = MonsterAiState.Attack;
        attacking.attack = new MonsterAttackDto { actionId = 7, actionNumber = 1, startTick = 100, durationTicks = 57, facingDir = 1 };
        copy = (MonsterStatesDto)RoundTrip(value, true);
        RequireSend(copy.monsters[1].attack.actionId == 7 && copy.monsters[1].attack.actionNumber == 1 && copy.monsters[1].attack.startTick == 100 &&
            copy.monsters[1].attack.durationTicks == 57, "Attack identity, animation number, facing and simulation timing survive the sole codec");
        attacking.attack.startTick = 106;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
        attacking.attack.startTick = 100; attacking.attack.actionNumber = 2;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
        attacking.attack.actionNumber = 1; attacking.attack.facingDir = -1;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
        attacking.attack.facingDir = 1; attacking.attack.actionId = 0;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
        attacking.attack.actionId = 7; attacking.moving = true;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
        attacking.moving = false;
        value.monsters[0].aiState = (MonsterAiState)99;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
        value.monsters[0].aiState = MonsterAiState.Idle; value.monsters[1].entityId = 3;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
    }

    private static void VerifyMonsterInitialization()
    {
        var value = new MonsterInitializationDto
        {
            roomSessionId = Guid.NewGuid().ToString("N"), loadId = 1, authorityTick = 100, stateSeq = 1,
            monsters = new[]
            {
                new InitialMonsterDto { entityId = 3, sceneKey = "Skeleton[0]", monsterType = "EnemySkeleton", position = new[] { 1f, 2f }, facingDir = 1, active = true, health = 100, maxHealth = 100, healthVersion = 1 },
                new InitialMonsterDto { entityId = 4, sceneKey = "Skeleton[1]", monsterType = "EnemySkeleton", position = new[] { 3f, 2f }, facingDir = -1, health = 100, maxHealth = 100, healthVersion = 1 }
            }
        };
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value));
        var copy = (MonsterInitializationDto)RoundTrip(value, true);
        RequireSend(copy.monsters.Length == 2 && copy.monsters[1].entityId == 4 && copy.monsters[1].sceneKey == "Skeleton[1]",
            "Monster manifest shares the sole protocol mapping and authority metadata");
        value.monsters[0].aiState = MonsterAiState.Attack;
        value.monsters[0].attack = new MonsterAttackDto { actionId = 8, actionNumber = 1, startTick = 80, durationTicks = 57, facingDir = 1 };
        copy = (MonsterInitializationDto)RoundTrip(value, true);
        RequireSend(copy.monsters[0].attack.actionId == 8 && copy.monsters[0].attack.startTick == 80,
            "Joining during an attack retains its original identity and start tick rather than starting a new action");
        value.monsters[1].entityId = 3;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
        value.monsters[1].entityId = 4; value.monsters[1].sceneKey = "Skeleton[0]";
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
        value.monsters = new InitialMonsterDto[0];
        RequireSend(((MonsterInitializationDto)RoundTrip(value, true)).monsters.Length == 0, "An empty monster scene has an explicit empty manifest");
    }

    private static void VerifyLocalInputLock()
    {
        var message = new LocalInputLockDto { lockSeq = 1, locked = true };
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(message));
        var copy = (LocalInputLockDto)RoundTrip(message, true);
        RequireSend(copy.lockSeq == 1 && copy.locked, "Local lock round trip uses connection-scoped dispatch");
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(new LocalInputLockDto(), true));
        var sender = new MovementInputSender();
        var states = new List<MovementInputDto>();
        RequireSend(sender.TrySend(1, 0, states.Add), "Moving before opening a UI");
        sender.RequestStop();
        RequireSend(!sender.TrySend(1, .02, states.Add), "Lock zero respects the 40ms minimum");
        RequireSend(sender.TrySend(-1, .04, states.Add) && states[1].currentAxis == 0 && states[1].inputSeq == 2,
            "Even a quickly closed UI must send its zero before old or newly sampled direction");
        RequireSend(!sender.TrySend(-1, .05, states.Add) && sender.TrySend(-1, .08, states.Add) && states[2].currentAxis == -1,
            "Movement resumes only after the latched stop has been sent");
    }

    private static void VerifyDashRequest()
    {
        foreach (float axis in new[] { -1f, 0f, 1f })
        {
            var request = new DashRequestDto { actionSeq = 1, currentAxis = axis };
            NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(request));
            var copy = (DashRequestDto)RoundTrip(request, true);
            RequireSend(copy.actionSeq == 1 && copy.currentAxis == axis, "Dash sends one press and its current input direction");
        }
        foreach (float axis in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1.01f, 1.01f })
            NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(new DashRequestDto { actionSeq = 1, currentAxis = axis }, true));
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(new DashRequestDto(), true));
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(new DashRequestDto { actionSeq = -1 }, true));
    }

    private static void VerifyWallSlideInput()
    {
        foreach (bool held in new[] { false, true })
        {
            var input = new WallSlideInputDto { inputSeq = 1, slideDown = held };
            NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(input));
            var copy = (WallSlideInputDto)RoundTrip(input, true);
            RequireSend(copy.inputSeq == 1 && copy.slideDown == held, "Wall-slide down and release round trip as current state");
        }
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(new WallSlideInputDto(), true));
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(new WallSlideInputDto { inputSeq = -1 }, true));
        var sender = new MovementInputSender();
        var sent = new List<WallSlideInputDto>();
        Action<MovementInputDto> send = value => sent.Add(new WallSlideInputDto { inputSeq = value.inputSeq, slideDown = value.currentAxis > 0 });
        RequireSend(sender.TrySend(1, 0, send) && !sender.TrySend(0, .02, send) && sender.TrySend(0, .04, send),
            "Wall-slide release merges and sends after the 40ms minimum");
        RequireSend(!sender.TrySend(0, .139, send) && sender.TrySend(0, .14, send) &&
            sent.Count == 3 && !sent[1].slideDown && !sent[2].slideDown && sent[2].inputSeq == 3,
            "Released state resends at 100ms without retaining the hold");
    }

    private static void VerifyJumpRequest()
    {
        var request = new JumpRequestDto { actionSeq = 1 };
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(request));
        RequireSend(((JumpRequestDto)RoundTrip(request, true)).actionSeq == 1, "Jump press round trip uses the sole protocol mapping");
        request.actionSeq = 0;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(request, true));
        request.actionSeq = -1;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(request, true));
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Decode((short)ProtocolId.JumpRequest, Encoding.UTF8.GetBytes("{}"), true));
    }

    private static void VerifyPlayerPositions()
    {
        var value = new PlayerPositionsDto
        {
            roomSessionId = Guid.NewGuid().ToString("N"), loadId = 1, authorityTick = 200, stateSeq = 8,
            players = new[]
            {
                new PlayerPositionDto { facingDir = -1, entityId = 20, position = new[] { -2.5f, 3f }, movementState = MovementAnimationState.Fall, yVelocity = -4 },
                new PlayerPositionDto { facingDir = 1, entityId = 10, position = new[] { 1f, -4f } }
            }
        };
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value));
        var copy = (PlayerPositionsDto)RoundTrip(value, true);
        RequireSend(copy.authorityTick == 200 && copy.stateSeq == 8 && copy.loadId == 1 &&
            ProtocolCodec.FindPosition(copy, 10).position[1] == -4, "Two entity positions share one snapshot tick/sequence, independent of array order");
        RequireSend(ProtocolCodec.FindPosition(copy, 20).facingDir == -1 && ProtocolCodec.FindPosition(copy, 10).facingDir == 1,
            "Each entity carries its own authoritative facing");
        RequireSend(ProtocolCodec.FindPosition(copy, 20).movementState == MovementAnimationState.Fall &&
            ProtocolCodec.FindPosition(copy, 20).yVelocity == -4, "Movement animation and vertical velocity share the authoritative snapshot");
        value.players[0].movementState = (MovementAnimationState)99;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
        value.players[0].movementState = MovementAnimationState.Fall;
        foreach (float velocity in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            value.players[0].yVelocity = velocity;
            NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
        }
        value.players[0].yVelocity = -4;
        foreach (int facing in new[] { 0, -2, 2 })
        {
            value.players[0].facingDir = facing;
            NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
        }
        value.players[0].facingDir = -1;
        value.players[1].entityId = 20;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
        value.players[1].entityId = 10;
        foreach (float coordinate in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            value.players[0].position[0] = coordinate;
            NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
        }
        value.players[0].position = new[] { 0f, 0f, 0f };
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
        value.players[0].position = new[] { 0f, 0f };
        value.authorityTick = 0;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
        value.authorityTick = 200; value.stateSeq = 0;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
        value.stateSeq = 8; value.players = new[] { value.players[0] };
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value, true));
    }

    private static void VerifyMovementInput()
    {
        var input = new MovementInputDto { inputSeq = 1, currentAxis = -1 };
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(input));
        var copy = (MovementInputDto)RoundTrip(input, true);
        NetPacketVerificationCases.Require(copy.inputSeq == 1 && copy.currentAxis == -1, "Movement state round trip");
        foreach (float axis in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1.01f, 1.01f })
        {
            input.currentAxis = axis;
            NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(input, true));
        }
        input.currentAxis = 0; input.inputSeq = 0;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(input, true));
        input.inputSeq = -1;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(input, true));
        foreach (string axis in new[] { "1.01", "-1.01" })
            NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Decode((short)ProtocolId.MovementInput,
                Encoding.UTF8.GetBytes("{\"inputSeq\":1,\"currentAxis\":" + axis + "}"), true));

        foreach (int interval in new[] { 30, 40, 50 })
        {
            var sender = new MovementInputSender { MinimumIntervalMilliseconds = interval };
            var sent = new List<MovementInputDto>();
            double minimum = interval / 1000.0;
            RequireSend(sender.TrySend(0, 0, sent.Add), "First state sends immediately");
            RequireSend(!sender.TrySend(1, minimum / 3, sent.Add) &&
                !sender.TrySend(-1, minimum * 2 / 3, sent.Add), "Intermediate axis changes are coalesced");
            RequireSend(sender.TrySend(1, minimum, sent.Add) && sent[1].currentAxis == 1, "Newest state wins at minimum interval");
            RequireSend(!sender.TrySend(1, minimum + 0.099, sent.Add) &&
                sender.TrySend(1, minimum + 0.1, sent.Add), "Unchanged state resends at 100ms");
            double stopTime = minimum + 0.1;
            RequireSend(!sender.TrySend(-1, stopTime + minimum / 3, sent.Add) &&
                !sender.TrySend(0, stopTime + minimum * 2 / 3, sent.Add) &&
                sender.TrySend(0, stopTime + minimum, sent.Add) && sent[3].currentAxis == 0,
                "Key release zero replaces pending direction");
            for (int i = 0; i < sent.Count; i++) RequireSend(sent[i].inputSeq == i + 1, "Only actual sends advance sequence");
            sender.Reset();
            RequireSend(sender.TrySend(0, 10, sent.Add) && sent[sent.Count - 1].inputSeq == 1, "New connection starts fresh");
        }
        var limits = new MovementInputSender();
        RequireSend(limits.MinimumIntervalMilliseconds == 40, "Default minimum is 40ms");
        limits.MinimumIntervalMilliseconds = 1;
        RequireSend(limits.MinimumIntervalMilliseconds == 30, "Minimum clamps to 30ms");
        limits.MinimumIntervalMilliseconds = 100;
        RequireSend(limits.MinimumIntervalMilliseconds == 50, "Maximum clamps to 50ms");
    }

    private static void RequireSend(bool condition, string message) => NetPacketVerificationCases.Require(condition, message);

    private static void VerifyInitialPlayers()
    {
        var initial = new PlayerInitializationDto
        {
            roomSessionId = Guid.NewGuid().ToString("N"), loadId = 1, authorityTick = 100, stateSeq = 1,
            players = new[]
            {
                new InitialPlayerDto { facingDir = -1, playerId = 2, entityId = 20, position = new[] { 2.5f, -1.75f }, health = 125, maxHealth = 180, healthVersion = 1,
                    combat = Rpg.Networking.RoomCombatConfiguration.CreateTestProfile(false),
                    movementState = MovementAnimationState.Move },
                new InitialPlayerDto { facingDir = 1, playerId = 1, entityId = 10, position = new[] { 0f, -1.75f }, health = 125, maxHealth = 140, healthVersion = 1,
                    combat = Rpg.Networking.RoomCombatConfiguration.CreateTestProfile(true) }
            }
        };
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(initial));
        var copy = (PlayerInitializationDto)RoundTrip(initial, true);
        RequireSend(copy.players[0].combat.stats[(int)CombatStatId.Damage] == 28 &&
            copy.players[1].combat.equipment[0].configurationId == "test-weapon-a-v1" &&
            copy.players[0].combat.skills[(int)RoomSkillId.Dash].cooldown == 2.5f &&
            copy.players[0].combat.skills[(int)RoomSkillId.Crystal].unlocked, "A-confirmed combat configuration round trip");
        var validCombat = initial.players[0].combat;
        initial.players[0].combat = null;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(initial, true));
        initial.players[0].combat = validCombat;
        initial.players[0].maxHealth = 179;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(initial, true));
        initial.players[0].maxHealth = 180;
        RequireSend(ProtocolCodec.FindPlayer(copy, 2, 20).facingDir == -1, "Initialization also restores confirmed facing");
        RequireSend(ProtocolCodec.FindPlayer(copy, 2, 20).movementState == MovementAnimationState.Move, "Initialization restores the current movement animation");
        initial.players[0].facingDir = 0;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(initial, true));
        initial.players[0].facingDir = -1;
        NetPacketVerificationCases.Require(copy.authorityTick == 100 && ProtocolCodec.FindPlayer(copy, 2, 20).position[0] == 2.5f,
            "Initial state uses the shared mapping, tick and identity lookup independent of array order");
        initial.players[1].entityId = 20;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(initial, true));
        initial.players[1].entityId = 10;
        initial.players[0].position = null;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(initial, true));
        initial.players[0].position = new[] { float.NaN, 0 };
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(initial, true));
        initial.players[0].position = new[] { 0f, 0f };
        initial.players[0].health = 181;
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(initial, true));
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Decode((short)ProtocolId.PlayerInitialization, Encoding.UTF8.GetBytes("{}"), true));
    }

    private static object RoundTrip(object dto, bool handshakeCompleted = false)
    {
        using (var stream = new MemoryStream(ProtocolCodec.Encode(dto, handshakeCompleted)))
        {
            NetPacketVerificationCases.Require(NetPacket.TryRead(stream, out short id, out byte[] payload), "Read encoded DTO");
            return ProtocolCodec.Decode(id, payload, handshakeCompleted);
        }
    }
}
