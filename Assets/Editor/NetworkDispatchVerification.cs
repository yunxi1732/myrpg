using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Rpg.Networking;
using Rpg.Networking.Protocol;
using UnityEditor;
using UnityEngine;

public static class NetworkDispatchVerification
{
    private static GameObject owner;
    private static NetworkSystem system;
    private static NetConnection connection;
    private static TcpClient sender;
    private static Stopwatch timer;
    private static int stage, mainThread, delivered, helloCount;
    private const int PacketCount = 8;

    [MenuItem("Tools/RPG/Verify Network Dispatch (Play Mode)")]
    public static void Verify()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        if (timer != null) throw new InvalidOperationException("Verification already running.");
        try
        {
            mainThread = Thread.CurrentThread.ManagedThreadId;
            stage = delivered = helloCount = 0;
            owner = new GameObject("NetworkDispatchVerification");
            var host = owner.AddComponent<ArchitectureHost>();
            system = new NetworkSystem(host.Current, 2);
            host.Current.RegistSystem(system);
            host.Current.RegistEvent<HandshakeHelloDto>(_ =>
            {
                Require(Thread.CurrentThread.ManagedThreadId == mainThread, "Handshake callback thread");
                helloCount++;
            });
            host.Current.RegistEvent<VerificationEchoDto>(value =>
            {
                Require(Thread.CurrentThread.ManagedThreadId == mainThread, "Echo callback thread");
                var dto = (VerificationEchoDto)value;
                if (dto.text == "throw") throw new InvalidOperationException("Expected isolated handler error");
                Require(dto.text == "after", "DTO mapping/content");
                delivered++;
            });
            host.Current.InitAllModules();
            connection = Open(out sender, 32, 4096);
            using (var batch = new MemoryStream())
            {
                Add(batch, 32000, "{}");
                Add(batch, (short)ProtocolId.HandshakeHello, "{\"schemaVersion\":0}");
                Add(batch, (short)ProtocolId.HandshakeHello, "{\"schemaVersion\":-1}");
                Add(batch, (short)ProtocolId.VerificationEcho, "{broken}");
                Add(batch, (short)ProtocolId.VerificationEcho, "{}");
                byte[] hello = ProtocolCodec.Encode(new HandshakeHelloDto { schemaVersion = ProtocolSchema.SchemaVersion });
                batch.Write(hello, 0, hello.Length);
                Add(batch, (short)ProtocolId.VerificationEcho, "{\"text\":\"throw\"}");
                Add(batch, (short)ProtocolId.VerificationEcho, "{\"text\":\"after\"}");
                byte[] bytes = batch.ToArray();
                sender.GetStream().Write(bytes, 0, bytes.Length);
            }
            timer = Stopwatch.StartNew();
            EditorApplication.update += Check;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }
        catch { Cleanup(); throw; }
    }

    private static void Check()
    {
        try
        {
            Require(timer.Elapsed.TotalSeconds < 10, "Timed out");
            if (stage == 0)
            {
                if (connection.PendingCount < PacketCount) return;
                Require(delivered == 0 && helloCount == 0, "Receive thread only queues");
                system.AttachConnection(connection);
                // One explicit architecture tick tests its budget; subsequent ticks come from the host.
                owner.GetComponent<ArchitectureHost>().Current.Update();
                Require(connection.PendingCount == PacketCount - 2 && system.RejectedCount == 2,
                    "Two-packet update budget includes bad messages");
                stage = 1;
            }
            else if (stage == 1)
            {
                if (connection.PendingCount != 0) return;
                Require(delivered == 1 && helloCount == 1 && system.ProcessedCount == 2 && system.RejectedCount == 6,
                    "Bad packets/handler do not stop later valid dispatch");
                sender.Close();
                connection = Open(out sender, 4, 128);
                Flood(sender, "{}", 8);
                stage = 2;
            }
            else if (stage == 2)
            {
                if (connection.IsOpen) return;
                Require(connection.LastError is InvalidDataException && connection.PendingCount == 4 && connection.PendingBytes <= 128,
                    "Packet-count limit closes overflow connection");
                connection.Dispose();
                sender.Close();
                connection = Open(out sender, 32, 16);
                Flood(sender, "{\"text\":\"exceeds byte limit\"}", 1);
                stage = 3;
            }
            else
            {
                if (connection.IsOpen) return;
                Require(connection.LastError is InvalidDataException && connection.PendingCount == 0 && connection.PendingBytes == 0,
                    "Byte limit closes overflow connection before enqueue");
                UnityEngine.Debug.Log("[NetworkDispatch] PASS: architecture/host Update, main-thread callbacks, shared codec validation, bad-message isolation, frame budget and packet/byte queue limits.");
                Cleanup();
            }
        }
        catch (Exception error) { Cleanup(); UnityEngine.Debug.LogException(error); }
    }

    private static NetConnection Open(out TcpClient source, int countLimit, int byteLimit)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        source = new TcpClient();
        try
        {
            listener.Start(1);
            source.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            return NetConnection.Adopt(listener.AcceptTcpClient(), countLimit, byteLimit);
        }
        catch { source.Close(); throw; }
        finally { listener.Stop(); }
    }

    private static void Add(Stream batch, short id, string json)
    {
        byte[] frame = NetPacket.Build(id, Encoding.UTF8.GetBytes(json));
        batch.Write(frame, 0, frame.Length);
    }

    private static void Flood(TcpClient source, string json, int count)
    {
        using (var batch = new MemoryStream())
        {
            for (int i = 0; i < count; i++) Add(batch, (short)ProtocolId.VerificationEcho, json);
            byte[] bytes = batch.ToArray();
            source.GetStream().Write(bytes, 0, bytes.Length);
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("[NetworkDispatch] FAIL: " + message);
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode) Cleanup();
    }

    private static void Cleanup()
    {
        EditorApplication.update -= Check;
        AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        system?.Dispose();
        connection?.Dispose();
        sender?.Close();
        if (owner != null) UnityEngine.Object.Destroy(owner);
        owner = null;
        system = null;
        connection = null;
        sender = null;
        timer = null;
    }
}
