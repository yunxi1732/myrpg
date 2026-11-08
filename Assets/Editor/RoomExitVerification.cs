using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Rpg.Networking;
using UnityEditor;
using UnityEngine;

public static class RoomExitVerification
{
    private static TcpRoomHost host;
    private static TcpRoomClient guest;
    private static NetConnection oldHostConnection, oldClientConnection;
    private static Stopwatch timer;
    private static string room;
    private static int stage, port, oldPlayer, oldEntity;
    private static int hostGuestLeft, hostClosed, guestLeft, guestRoomClosed, disconnects;

    [MenuItem("Tools/RPG/Verify Room Exit")]
    public static void Verify()
    {
        if (Application.isPlaying || timer != null) throw new InvalidOperationException("Run once outside Play mode.");
        try
        {
            stage = hostGuestLeft = hostClosed = guestLeft = guestRoomClosed = disconnects = 0;
            host = new TcpRoomHost();
            guest = new TcpRoomClient();
            host.Init(); guest.Init();
            host.RoomExited += value =>
            {
                Require(value.RoomSessionId == room, "Host exit notification session");
                if (value.Reason == RoomExitReason.GuestLeft && !value.IsLocal) hostGuestLeft++;
                else if (value.Reason == RoomExitReason.HostClosed && value.IsLocal) hostClosed++;
                else throw new InvalidOperationException("Unexpected host exit notification");
            };
            guest.RoomExited += value =>
            {
                Require(value.RoomSessionId == room, "Client exit notification session");
                if (value.Reason == RoomExitReason.GuestLeft && value.IsLocal) guestLeft++;
                else if (value.Reason == RoomExitReason.HostClosed && !value.IsLocal) guestRoomClosed++;
                else throw new InvalidOperationException("Unexpected client exit notification");
            };
            host.Disconnected += _ => disconnects++;
            guest.Disconnected += _ => disconnects++;
            var reservation = new TcpListener(IPAddress.Any, 0);
            try { reservation.Start(); port = ((IPEndPoint)reservation.LocalEndpoint).Port; }
            finally { reservation.Stop(); }
            host.StartListening(port);
            room = host.RoomSessionId;
            guest.Connect("127.0.0.1", port);
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
            Require(timer.Elapsed.TotalSeconds < 15, "Room exit verification timed out");
            host.Update();
            guest.Update();
            if (stage == 0)
            {
                if (Pending()) return;
                Require(guest.State == RoomConnectionState.Connected, "Initial guest joins");
                oldHostConnection = host.Guest;
                oldClientConnection = guest.Connection;
                oldPlayer = guest.LocalIdentity.PlayerId;
                oldEntity = guest.LocalIdentity.EntityId;
                guest.LeaveRoom();
                guest.LeaveRoom();
                Require(guestLeft == 1 && guest.State == RoomConnectionState.Idle && guest.LocalIdentity == null &&
                    guest.RoomSessionId == null && guest.Connection == null &&
                    !oldClientConnection.IsReceiveThreadAlive && oldClientConnection.PendingCount == 0,
                    "B clears its resources once");
                stage = 1;
            }
            else if (stage == 1)
            {
                if (hostGuestLeft == 0) return;
                Require(hostGuestLeft == 1 && host.IsListening && host.RoomSessionId == room && host.Guest == null &&
                    host.GuestIdentity == null && host.HostIdentity != null && !oldHostConnection.IsReceiveThreadAlive &&
                    oldHostConnection.PendingCount == 0, "A releases B but retains the room");
                guest.Connect("127.0.0.1", port);
                stage = 2;
            }
            else if (stage == 2)
            {
                if (Pending()) return;
                Require(guest.State == RoomConnectionState.Connected && host.RoomSessionId == room &&
                    guest.LocalIdentity.PlayerId != oldPlayer && guest.LocalIdentity.EntityId != oldEntity &&
                    guest.Connection.SessionId != oldClientConnection.SessionId && host.Guest.SessionId != oldHostConnection.SessionId &&
                    !host.TryResolvePlayer(oldHostConnection, out _), "Rejoin has fresh ownership and connection sessions");
                oldHostConnection = host.Guest;
                oldClientConnection = guest.Connection;
                host.CloseRoom();
                host.CloseRoom();
                Require(hostClosed == 1 && !host.IsListening && host.Guest == null && host.RoomSessionId == null &&
                    host.HostIdentity == null && !oldHostConnection.IsReceiveThreadAlive && oldHostConnection.PendingCount == 0,
                    "A stops listening and clears room resources once");
                stage = 3;
            }
            else
            {
                if (guestRoomClosed == 0) return;
                Require(guestRoomClosed == 1 && guestLeft == 1 && hostGuestLeft == 1 && disconnects == 0 &&
                    guest.State == RoomConnectionState.Idle && guest.Connection == null && guest.LocalIdentity == null &&
                    guest.RoomSessionId == null && !oldClientConnection.IsReceiveThreadAlive && oldClientConnection.PendingCount == 0,
                    "B consumes host-close notice and clears its session without duplicate disconnect");
                for (int i = 0; i < 3; i++) { host.Update(); guest.Update(); }
                Require(guestRoomClosed == 1 && hostClosed == 1 && disconnects == 0, "No late duplicate callbacks");
                UnityEngine.Debug.Log("[RoomExit] PASS: B leave preserves A, fresh rejoin, A close notifies B, idempotent exits and thread/queue/identity cleanup. No scenes or entities changed.");
                Cleanup();
            }
        }
        catch (Exception error) { Cleanup(); UnityEngine.Debug.LogException(error); }
    }

    private static bool Pending() => guest.State == RoomConnectionState.Connecting || guest.State == RoomConnectionState.Handshaking;

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("[RoomExit] FAIL: " + message);
    }

    private static void Cleanup()
    {
        EditorApplication.update -= Check;
        AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
        EditorApplication.quitting -= Cleanup;
        try { guest?.Dispose(); }
        finally
        {
            host?.Dispose();
            guest = null; host = null; timer = null;
            oldHostConnection = oldClientConnection = null;
        }
    }
}
