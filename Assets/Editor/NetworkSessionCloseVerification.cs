using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Rpg.Networking;
using Rpg.Networking.Protocol;
using UnityEditor;
using UnityEngine;

public static class NetworkSessionCloseVerification
{
    private static Architecture architecture;
    private static NetworkSystem system;
    private static NetConnection connection;
    private static TcpClient sender;
    private static NetConnection.ReceivedPacket oldPacket;
    private static Stopwatch timeout;
    private static readonly HashSet<long> sessionIds = new HashSet<long>();
    private static int round, stage, callbacks;

    [MenuItem("Tools/RPG/Verify Network Session Close")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
        if (timeout != null) throw new InvalidOperationException("Verification already running.");
        try
        {
            round = stage = callbacks = 0;
            oldPacket = null;
            sessionIds.Clear();
            architecture = new Architecture();
            system = new NetworkSystem(architecture, 1);
            architecture.RegistSystem(system);
            architecture.InitAllModules();
            timeout = Stopwatch.StartNew();
            OpenRound();
            EditorApplication.update += Check;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.quitting += Cleanup;
        }
        catch { Cleanup(); throw; }
    }

    private static void OpenRound()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        sender = new TcpClient();
        try
        {
            listener.Start(1);
            sender.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            connection = NetConnection.Adopt(listener.AcceptTcpClient());
        }
        finally { listener.Stop(); }
        system.AttachConnection(connection);
        Require(sessionIds.Add(system.SessionId), "Session token must not be reused");
        int subscribedRound = round;
        system.RegistSessionEvent<VerificationEchoDto>(value =>
        {
            Require(round == subscribedRound, "Previous session callback leaked");
            Require(((VerificationEchoDto)value).text == "current", "Old message was dispatched");
            callbacks++;
        });
        if (oldPacket != null) InjectOldPacket(connection, oldPacket);
        SendEcho("current");
        SendEcho("retained-old");
        stage = 0;
    }

    private static void Check()
    {
        try
        {
            Require(timeout.Elapsed.TotalSeconds < 20, "Session close verification timed out");
            if (stage == 0)
            {
                int expected = oldPacket == null ? 2 : 3;
                if (connection.PendingCount < expected) return;
                if (oldPacket != null) architecture.Update(); // stale packet consumes its frame budget
                architecture.Update();
                Require(callbacks == round + 1 && system.ProcessedCount == 1,
                    "Exactly one callback, without duplicate subscriptions");
                Require(system.StalePacketCount == (oldPacket == null ? 0 : 1), "Old packet rejected by session token");
                Require(connection.TryDequeue(out oldPacket), "Retain an old packet for the next session");
                SendEcho("queued-at-close");
                // Leave the receiver blocked inside a partial next header when closing.
                sender.GetStream().Write(new byte[] { 32, 0 }, 0, 2);
                stage = 1;
            }
            else
            {
                if (connection.PendingCount != 1) return;
                system.CloseSession();
                system.CloseSession();
                connection.Dispose();
                connection.Dispose();
                Require(!connection.IsReceiveThreadAlive && !connection.IsOpen, "Receiver thread terminated");
                Require(connection.PendingCount == 0 && connection.PendingBytes == 0 && system.SessionId == 0,
                    "Queue and active session cleared");
                bool reuseRejected = false;
                try { system.AttachConnection(connection); }
                catch (InvalidOperationException) { reuseRejected = true; }
                Require(reuseRejected, "A closed connection cannot reuse its old session token");
                int before = callbacks;
                architecture.SendEvent<VerificationEchoDto>(new VerificationEchoDto { text = "after-close" });
                architecture.Update();
                Require(callbacks == before, "Closed session subscriptions removed");
                sender.Close();
                connection = null;
                sender = null;
                round++;
                if (round < 10) OpenRound();
                else
                {
                    VerifyCloseInsideCallback();
                    UnityEngine.Debug.Log("[NetworkSessionClose] PASS: 10 reconnects, terminated receivers, empty queues, removed subscriptions, stale-session rejection and repeatable close.");
                    Cleanup();
                }
            }
        }
        catch (Exception error) { Cleanup(); UnityEngine.Debug.LogException(error); }
    }

    private static void VerifyCloseInsideCallback()
    {
        oldPacket = null;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        sender = new TcpClient();
        try
        {
            listener.Start(1);
            sender.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            connection = NetConnection.Adopt(listener.AcceptTcpClient());
        }
        finally { listener.Stop(); }
        system.AttachConnection(connection);
        int lateCallbacks = 0;
        system.RegistSessionEvent<VerificationEchoDto>(_ => system.CloseSession());
        system.RegistSessionEvent<VerificationEchoDto>(_ => lateCallbacks++);
        architecture.SendEvent<VerificationEchoDto>(new VerificationEchoDto { text = "close-in-callback" });
        Require(lateCallbacks == 0 && !connection.IsReceiveThreadAlive, "Snapshot callbacks guarded after close");
    }

    private static void SendEcho(string text)
    {
        byte[] bytes = ProtocolCodec.Encode(new VerificationEchoDto { text = text });
        sender.GetStream().Write(bytes, 0, bytes.Length);
    }

    // Fault injection only: place a retained old packet in the new queue to exercise the dispatch guard.
    private static void InjectOldPacket(NetConnection target, NetConnection.ReceivedPacket packet)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(NetConnection);
        object queueLock = type.GetField("queueLock", flags).GetValue(target);
        lock (queueLock)
        {
            var queue = (Queue<NetConnection.ReceivedPacket>)type.GetField("receiveQueue", flags).GetValue(target);
            queue.Enqueue(packet);
            type.GetField("pendingBytes", flags).SetValue(target, packet.Payload.Length + NetPacket.ProtocolIdSize);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[NetworkSessionClose] FAIL: " + message);
    }

    private static void Cleanup()
    {
        EditorApplication.update -= Check;
        AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
        EditorApplication.quitting -= Cleanup;
        try { architecture?.Dispose(); }
        finally
        {
            connection?.Dispose();
            sender?.Close();
            architecture = null;
            system = null;
            connection = null;
            sender = null;
            oldPacket = null;
            timeout = null;
            sessionIds.Clear();
        }
    }
}
