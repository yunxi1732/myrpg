using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Rpg.Networking;
using Rpg.Networking.Protocol;
using UnityEditor;
using UnityEngine;

public static class RoomClientVerification
{
    private static Architecture architecture;
    private static TcpRoomClient client;
    private static TcpListener listener;
    private static TcpClient peer;
    private static Stopwatch timeout;
    private static int stage, port, mainThread, connectedEvents;

    [MenuItem("Tools/RPG/Verify Room Client")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
        if (timeout != null) throw new InvalidOperationException("Verification already running.");
        try
        {
            stage = connectedEvents = 0;
            mainThread = Thread.CurrentThread.ManagedThreadId;
            architecture = new Architecture();
            client = new TcpRoomClient(1000);
            architecture.RegistSystem(client);
            architecture.InitAllModules();
            client.StateChanged += () =>
            {
                Require(Thread.CurrentThread.ManagedThreadId == mainThread, "State callback must be on main thread");
                if (client.State == RoomConnectionState.Connected) connectedEvents++;
            };
            client.Connect("invalid IP", 7777);
            Require(client.State == RoomConnectionState.Failed, "Invalid address");
            client.Connect("127.0.0.1", 0);
            Require(client.State == RoomConnectionState.Failed, "Invalid port");
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(8);
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
            client.Connect("127.0.0.1", port);
            client.CancelConnect(); // Cancel before Update can apply even an already completed TCP success.
            Require(client.State == RoomConnectionState.Cancelled && client.Connection == null, "Cancellation invalidates completion");
            client.Connect("127.0.0.1", port);
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
            Require(timeout.Elapsed.TotalSeconds < 10, "Verification timeout");
            // Drain both the cancelled attempt and the new connection from the listener's backlog.
            while (listener != null && listener.Pending())
            {
                peer?.Close();
                peer = listener.AcceptTcpClient();
                if (peer.Client.Poll(0, SelectMode.SelectRead) && peer.Available == 0)
                {
                    peer.Close();
                    peer = null;
                    continue;
                }
                // Minimal A fixture now confirms admission; TCP success alone is no longer "Connected".
                byte[] reply = ProtocolCodec.Encode(new HandshakeResultDto
                {
                    schemaVersion = ProtocolSchema.SchemaVersion, accepted = true,
                    roomSessionId = Guid.NewGuid().ToString("N"),
                    hostPlayerId = 1, hostEntityId = 1, guestPlayerId = 2, guestEntityId = 2
                });
                peer.GetStream().Write(reply, 0, reply.Length);
            }
            architecture.Update();
            if (stage == 0)
            {
                if (IsPending()) return;
                Require(client.State == RoomConnectionState.Connected && connectedEvents == 1,
                    "Only current attempt connects; late cancelled attempt cannot attach");
                client.Close();
                peer?.Close();
                peer = null;
                listener.Stop();
                listener = null;
                client.Connect("127.0.0.1", port);
                stage = 1;
            }
            else if (stage == 1)
            {
                if (IsPending()) return;
                Require(client.State == RoomConnectionState.Failed, "Refused connection restores failed state");
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start(8);
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
                client.Connect("127.0.0.1", port);
                // Fault injection: hold completion pending to test timeout independently of external routing/firewalls.
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                object attempt = typeof(TcpRoomClient).GetField("attempt", flags).GetValue(client);
                attempt.GetType().GetField("Task", flags).SetValue(attempt, new TaskCompletionSource<bool>().Task);
                stage = 2;
            }
            else if (stage == 2)
            {
                if (IsPending()) return;
                Require(client.State == RoomConnectionState.Failed && client.FailureMessage == "Connection timed out",
                    "Unscaled timeout terminates pending attempt");
                client.Connect("127.0.0.1", port);
                stage = 3;
            }
            else
            {
                if (IsPending()) return;
                Require(client.State == RoomConnectionState.Connected && connectedEvents == 2,
                    "Retry after failure/timeout succeeds");
                UnityEngine.Debug.Log("[RoomClient] PASS: async loopback, invalid IP/port, refusal, cancellation/late completion, injected pending timeout, retry and main-thread state callbacks. Bootstrap reply fixture used; no LAN acceptance.");
                Cleanup();
            }
        }
        catch (Exception error) { Cleanup(); UnityEngine.Debug.LogException(error); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[RoomClient] FAIL: " + message);
    }

    private static bool IsPending() => client.State == RoomConnectionState.Connecting || client.State == RoomConnectionState.Handshaking;

    private static void Cleanup()
    {
        EditorApplication.update -= Check;
        AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
        EditorApplication.quitting -= Cleanup;
        try { architecture?.Dispose(); }
        finally
        {
            peer?.Close();
            listener?.Stop();
            architecture = null;
            client = null;
            peer = null;
            listener = null;
            timeout = null;
        }
    }
}
