using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Rpg.Networking;
using Rpg.Networking.Protocol;
using UnityEditor;
using UnityEngine;

public static class NetConnectionVerification
{
    private const int MessageCount = 64;
    private static TcpListener listener;
    private static NetConnection active;
    private static NetConnection accepted;
    private static Stopwatch timeout;

    [MenuItem("Tools/RPG/Verify TCP Connection")]
    public static void Verify()
    {
        if (timeout != null) throw new InvalidOperationException("TCP verification is already running.");
        if (Application.isPlaying) throw new InvalidOperationException("Run this verification outside Play mode.");
        try
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            active = NetConnection.Connect("127.0.0.1", port);
            accepted = NetConnection.Adopt(listener.AcceptTcpClient());
            listener.Stop();
            listener = null;
            // All serialization happens on the editor thread. Receive threads only queue bytes.
            for (int i = 0; i < MessageCount; i++)
            {
                active.Send(new VerificationEchoDto { text = "主动端→接管端 中文 " + i });
                accepted.Send(new VerificationEchoDto { text = "接管端→主动端 中文 " + i });
            }
            timeout = Stopwatch.StartNew();
            EditorApplication.update += CheckQueues;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.quitting += Cleanup;
        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    private static void CheckQueues()
    {
        try
        {
            if (active.LastError != null) throw active.LastError;
            if (accepted.LastError != null) throw accepted.LastError;
            if (active.PendingCount < MessageCount || accepted.PendingCount < MessageCount)
            {
                if (timeout.Elapsed.TotalSeconds > 5) throw new TimeoutException("TCP packets did not reach both queues.");
                return;
            }
            // Queues stay full until this explicit dequeue; there is no receive-thread dispatch.
            CheckMessages(active, "接管端→主动端 中文 ");
            CheckMessages(accepted, "主动端→接管端 中文 ");
            UnityEngine.Debug.Log("[NetConnection] PASS: loopback connect/adopt, 64 messages each way, ordered isolated queues; explicit editor-thread decode only.");
            Cleanup();
        }
        catch (Exception error)
        {
            Cleanup();
            UnityEngine.Debug.LogException(error);
        }
    }

    private static void CheckMessages(NetConnection connection, string prefix)
    {
        for (int i = 0; i < MessageCount; i++)
        {
            if (!connection.TryDequeue(out NetConnection.ReceivedPacket packet))
                throw new InvalidOperationException("Missing queued packet.");
            var dto = ProtocolCodec.Decode(packet.ProtocolId, packet.Payload) as VerificationEchoDto;
            if (dto == null || dto.text != prefix + i) throw new InvalidOperationException("Incorrect packet content/order.");
        }
        if (connection.TryDequeue(out _)) throw new InvalidOperationException("Unexpected extra packet.");
    }

    private static void Cleanup()
    {
        EditorApplication.update -= CheckQueues;
        AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
        EditorApplication.quitting -= Cleanup;
        active?.Dispose();
        accepted?.Dispose();
        listener?.Stop();
        active = null;
        accepted = null;
        listener = null;
        timeout = null;
    }
}
