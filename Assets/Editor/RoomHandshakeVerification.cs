using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Rpg.Networking;
using Rpg.Networking.Protocol;
using UnityEditor;
using UnityEngine;

public static class RoomHandshakeVerification
{
    private static Architecture architecture;
    private static TcpRoomHost host;
    private static TcpRoomClient guest, extra;
    private static NetConnection probe;
    private static Stopwatch timeout;
    private static string firstSession;
    private static int stage, port;

    [MenuItem("Tools/RPG/Verify Room Handshake")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
        if (timeout != null) throw new InvalidOperationException("Verification already running.");
        try
        {
            stage = 0;
            architecture = new Architecture();
            host = new TcpRoomHost();
            guest = new TcpRoomClient();
            extra = new TcpRoomClient();
            architecture.RegistSystem(host);
            // Distinct interface types are unnecessary: clients are explicitly driven below on this same editor thread.
            architecture.InitAllModules();
            guest.Init();
            extra.Init();
            port = FreePort();
            host.StartListening(port);
            firstSession = host.RoomSessionId;
            probe = NetConnection.Connect("127.0.0.1", port);
            probe.Send(new HandshakeHelloDto { schemaVersion = ProtocolSchema.SchemaVersion + 1 });
            timeout = Stopwatch.StartNew();
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
            Require(timeout.Elapsed.TotalSeconds < 20, "Handshake verification timed out");
            architecture.Update();
            guest.Update();
            extra.Update();
            if (stage == 0)
            {
                if (!probe.TryDequeue(out var packet)) return;
                var result = (HandshakeResultDto)ProtocolCodec.Decode(packet.ProtocolId, packet.Payload);
                Require(!result.accepted && result.rejection == HandshakeRejection.VersionMismatch &&
                    host.GuestIdentity == null && host.HostIdentity.PlayerId > 0, "Version mismatch rejected without identity assignment");
                probe.Dispose();
                probe = null;
                guest.Connect("127.0.0.1", port);
                stage = 1;
            }
            else if (stage == 1)
            {
                if (Pending(guest)) return;
                Require(guest.State == RoomConnectionState.Connected, "Matching version admitted");
                Require(guest.RoomSessionId == host.RoomSessionId && host.GuestIdentity.RoomSessionId == firstSession,
                    "A and B agree on current room session");
                Require(guest.HostIdentity.PlayerId == host.HostIdentity.PlayerId &&
                    guest.LocalIdentity.PlayerId == host.GuestIdentity.PlayerId &&
                    guest.LocalIdentity.EntityId == host.GuestIdentity.EntityId &&
                    guest.LocalIdentity.PlayerId != host.HostIdentity.PlayerId &&
                    guest.LocalIdentity.EntityId != host.HostIdentity.EntityId, "Distinct A-assigned player/entity identities");
                Require(host.TryResolvePlayer(host.Guest, out var identity) && identity.PlayerId == guest.LocalIdentity.PlayerId &&
                    !host.TryResolvePlayer(guest.Connection, out _) && !host.TryResolvePlayer(null, out _), "Ownership follows server connection");
                // Inject spoofed identity fields and a replayed hello on B's actual socket. A must retain its binding.
                var socket = (TcpClient)typeof(NetConnection).GetField("client", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(guest.Connection);
                string json = "{\"schemaVersion\":" + ProtocolSchema.SchemaVersion + ",\"playerId\":1,\"entityId\":1}";
                byte[] bytes = NetPacket.Build((short)ProtocolId.HandshakeHello, Encoding.UTF8.GetBytes(json));
                socket.GetStream().Write(bytes, 0, bytes.Length);
                extra.Connect("127.0.0.1", port);
                stage = 2;
            }
            else if (stage == 2)
            {
                if (Pending(extra)) return;
                object dispatchPeer = typeof(TcpRoomHost).GetField("guestPeer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(host);
                var network = (NetworkSystem)dispatchPeer.GetType().GetField("Network").GetValue(dispatchPeer);
                if (network.ProcessedCount < 2) return; // Ensure the spoofed/replayed hello actually reached dispatch.
                Require(extra.State == RoomConnectionState.Failed && extra.FailureMessage == "Room is full" &&
                    extra.LocalIdentity == null && extra.RoomSessionId == null, "Third client rejected without identity");
                Require(host.GuestIdentity.PlayerId == guest.LocalIdentity.PlayerId && host.GuestIdentity.PlayerId != 1,
                    "Spoof/replayed hello cannot change player ownership");
                guest.Close();
                host.StopListening();
                Require(guest.LocalIdentity == null && guest.RoomSessionId == null && host.RoomSessionId == null,
                    "Closing clears old identity/session");
                port = FreePort();
                host.StartListening(port);
                Require(host.RoomSessionId != firstSession, "New room uses a new session namespace");
                guest.Connect("127.0.0.1", port);
                stage = 3;
            }
            else
            {
                if (Pending(guest)) return;
                Require(guest.State == RoomConnectionState.Connected && guest.RoomSessionId == host.RoomSessionId &&
                    guest.RoomSessionId != firstSession, "Fresh handshake cannot retain the old room session");
                UnityEngine.Debug.Log("[RoomHandshake] PASS: shared schema, version/full rejection, A-assigned identities, connection ownership, spoof/replay isolation and fresh room session. No entities created.");
                Cleanup();
            }
        }
        catch (Exception error) { Cleanup(); UnityEngine.Debug.LogException(error); }
    }

    private static bool Pending(TcpRoomClient client) => client.State == RoomConnectionState.Connecting ||
        client.State == RoomConnectionState.Handshaking;

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Any, 0);
        try { listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("[RoomHandshake] FAIL: " + message);
    }

    private static void Cleanup()
    {
        EditorApplication.update -= Check;
        AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
        EditorApplication.quitting -= Cleanup;
        try { guest?.Dispose(); extra?.Dispose(); probe?.Dispose(); }
        finally
        {
            architecture?.Dispose();
            guest = extra = null;
            probe = null;
            host = null;
            architecture = null;
            timeout = null;
        }
    }
}
