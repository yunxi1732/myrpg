using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Rpg.Networking;
using UnityEditor;
using UnityEngine;

public static class RoomListenerVerification
{
    private static Architecture architecture;
    private static TcpRoomHost room;
    private static TcpClient first, second;
    private static Stopwatch timeout;

    [MenuItem("Tools/RPG/Verify Room Listener")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
        if (timeout != null) throw new InvalidOperationException("Verification already running.");
        try
        {
            architecture = new Architecture();
            room = new TcpRoomHost();
            architecture.RegistSystem(room);
            architecture.InitAllModules();
            var blocker = new TcpListener(IPAddress.Any, 0);
            int port;
            try
            {
                blocker.ExclusiveAddressUse = true;
                blocker.Start();
                port = ((IPEndPoint)blocker.LocalEndpoint).Port;
                bool refused = false;
                try { room.StartListening(port); }
                catch (SocketException) { refused = true; }
                Require(refused && !room.IsListening, "Occupied port fails without leaving listener state");
            }
            finally { blocker.Stop(); }
            room.StartListening(port);
            room.StartListening(port == 65535 ? port - 1 : port + 1);
            Require(room.IsListening && room.Port == port, "Repeat create preserves the original listener");
            first = new TcpClient();
            first.Connect(IPAddress.Loopback, port);
            second = new TcpClient();
            second.Connect(IPAddress.Loopback, port);
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
            architecture.Update();
            if (room.Guest == null || room.RejectedConnections != 1)
            {
                Require(timeout.Elapsed.TotalSeconds < 5, "Accept/reject timed out");
                return;
            }
            NetConnection guest = room.Guest;
            room.StopListening();
            room.StopListening();
            Require(!room.IsListening && room.Guest == null && !guest.IsReceiveThreadAlive,
                "Stop releases listener and receiving thread");
            UnityEngine.Debug.Log("[RoomListener] PASS: occupied-port retry, idempotent create, one B accepted, extra client rejected and resource cleanup. No scene loading or entities.");
            Cleanup();
        }
        catch (Exception error) { Cleanup(); UnityEngine.Debug.LogException(error); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[RoomListener] FAIL: " + message);
    }

    private static void Cleanup()
    {
        EditorApplication.update -= Check;
        AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
        EditorApplication.quitting -= Cleanup;
        try { architecture?.Dispose(); }
        finally
        {
            first?.Close();
            second?.Close();
            architecture = null;
            room = null;
            first = second = null;
            timeout = null;
        }
    }
}
