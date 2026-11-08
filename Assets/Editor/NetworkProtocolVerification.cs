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
        Debug.Log("[NetworkProtocol] PASS: framing, JSON, DTO/schema validation, movement input timing and two-player position snapshots. No sockets or scenes used.");
    }

    private static void VerifyPlayerPositions()
    {
        var value = new PlayerPositionsDto
        {
            roomSessionId = Guid.NewGuid().ToString("N"), loadId = 1, authorityTick = 200, stateSeq = 8,
            players = new[]
            {
                new PlayerPositionDto { entityId = 20, position = new[] { -2.5f, 3f } },
                new PlayerPositionDto { entityId = 10, position = new[] { 1f, -4f } }
            }
        };
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(value));
        var copy = (PlayerPositionsDto)RoundTrip(value, true);
        RequireSend(copy.authorityTick == 200 && copy.stateSeq == 8 && copy.loadId == 1 &&
            ProtocolCodec.FindPosition(copy, 10).position[1] == -4, "Two entity positions share one snapshot tick/sequence, independent of array order");
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
                new InitialPlayerDto { playerId = 2, entityId = 20, position = new[] { 2.5f, -1.75f }, health = 125, maxHealth = 125 },
                new InitialPlayerDto { playerId = 1, entityId = 10, position = new[] { 0f, -1.75f }, health = 125, maxHealth = 125 }
            }
        };
        NetPacketVerificationCases.Expect<InvalidDataException>(() => ProtocolCodec.Encode(initial));
        var copy = (PlayerInitializationDto)RoundTrip(initial, true);
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
        initial.players[0].health = 126;
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
