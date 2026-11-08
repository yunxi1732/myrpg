using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Rpg.Networking;
using Rpg.Networking.Protocol;
using UnityEditor;
using UnityEngine;

public static class HeartbeatDisconnectVerification
{
    private static TcpRoomHost host;
    private static TcpRoomClient client;
    private static Stopwatch timer;
    private static int stage, hostEvents, clientEvents;
    private static float previousScale;

    [MenuItem("Tools/RPG/Verify Heartbeat Disconnect")]
    public static void Verify()
    {
        if (Application.isPlaying || timer != null) throw new InvalidOperationException("Run once outside Play mode.");
        previousScale = Time.timeScale;
        try
        {
            stage = hostEvents = clientEvents = 0;
            host = new TcpRoomHost();
            client = new TcpRoomClient();
            host.Init(); client.Init();
            host.Disconnected += value =>
            {
                Require(value.Reason == NetworkDisconnectReason.Timeout, "Silent peer produces timeout");
                hostEvents++;
            };
            client.Disconnected += value => clientEvents++;
            var reservation = new TcpListener(IPAddress.Loopback, 0);
            int port;
            try { reservation.Start(); port = ((IPEndPoint)reservation.LocalEndpoint).Port; }
            finally { reservation.Stop(); }
            host.StartListening(port);
            client.Connect("127.0.0.1", port);
            Time.timeScale = 0;
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
            Require(timer.Elapsed.TotalSeconds < 15, "Verification timed out");
            host.Update();
            if (stage < 2) client.Update();
            if (stage == 0)
            {
                if (client.State != RoomConnectionState.Connected) return;
                stage = 1;
                timer.Restart();
            }
            else if (stage == 1)
            {
                if (timer.Elapsed.TotalSeconds < 2.2) return;
                Require(hostEvents == 0 && clientEvents == 0 && Time.timeScale == 0,
                    "Heartbeats survive timeScale zero");
                stage = 2;
                timer.Restart(); // Stop driving B: socket stays open, no heartbeat is generated.
            }
            else if (stage == 2)
            {
                if (hostEvents == 0)
                {
                    // Valid-shaped heartbeat for a previous room must not keep the connection alive.
                    bool replay = (int)(timer.Elapsed.TotalSeconds * 10) % 2 == 0;
                    client.Connection.Send(new HeartbeatDto
                    {
                        roomSessionId = replay ? client.RoomSessionId : Guid.NewGuid().ToString("N"),
                        heartbeatSeq = replay ? 1 : 999
                    }, true);
                    return;
                }
                stage = 3;
                timer.Restart();
            }
            else
            {
                client.Update();
                if (clientEvents == 0) return; // EOF can arrive on the receive thread after A's close has returned.
                Require(hostEvents == 1 && clientEvents == 1 && host.IsListening && host.Guest == null &&
                    client.State == RoomConnectionState.Disconnected, "One disconnect each; A remains open, B clears session");
                client.Connect("bad address", 7777);
                Require(client.State == RoomConnectionState.Failed && clientEvents == 1,
                    "Connection failure is distinct from joined-room disconnect");
                for (int i = 0; i < 3; i++) { host.Update(); client.Update(); }
                Require(hostEvents == 1 && clientEvents == 1, "No repeated disconnect notifications");
                UnityEngine.Debug.Log("[HeartbeatDisconnect] PASS: real monotonic heartbeat/timeout at timeScale=0, stale-room heartbeat rejection, single disconnect events and connection-failure distinction.");
                Cleanup();
            }
        }
        catch (Exception error) { Cleanup(); UnityEngine.Debug.LogException(error); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[HeartbeatDisconnect] FAIL: " + message);
    }

    private static void Cleanup()
    {
        EditorApplication.update -= Check;
        AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
        EditorApplication.quitting -= Cleanup;
        try { client?.Dispose(); host?.Dispose(); }
        finally { client = null; host = null; timer = null; Time.timeScale = previousScale; }
    }
}
