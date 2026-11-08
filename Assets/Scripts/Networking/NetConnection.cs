using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Rpg.Networking.Protocol;

namespace Rpg.Networking
{
    /// <summary>One TCP connection. The receive thread only frames bytes and queues packets.</summary>
    public sealed class NetConnection : IDisposable
    {
        public sealed class ReceivedPacket
        {
            public long SessionId { get; }
            public short ProtocolId { get; }
            public byte[] Payload { get; }

            internal ReceivedPacket(long sessionId, short protocolId, byte[] payload)
            {
                SessionId = sessionId;
                ProtocolId = protocolId;
                Payload = payload;
            }
        }

        private readonly TcpClient client;
        private readonly NetworkStream stream;
        private readonly Thread receiveThread;
        private readonly object sendLock = new object();
        private readonly object queueLock = new object();
        private readonly object disposeLock = new object();
        private readonly Queue<ReceivedPacket> receiveQueue = new Queue<ReceivedPacket>();
        private volatile bool running;
        private int disposed;
        private Exception lastError;
        private readonly int maxPendingPackets;
        private readonly int maxPendingBytes;
        private int pendingBytes;
        private static long nextSessionId;

        // Local lifecycle token, not the future wire-level room/handshake ID. Never reused on reconnect.
        public long SessionId { get; } = Interlocked.Increment(ref nextSessionId);
        public bool IsReceiveThreadAlive => receiveThread.IsAlive;

        public const int DefaultMaxPendingPackets = 256;
        public const int DefaultMaxPendingBytes = 4 * 1024 * 1024;

        // Local state, not a remote-liveness guarantee. No heartbeat is introduced here.
        public bool IsOpen => running && Volatile.Read(ref disposed) == 0;
        public int PendingCount { get { lock (queueLock) return receiveQueue.Count; } }
        public int PendingBytes { get { lock (queueLock) return pendingBytes; } }
        public Exception LastError { get { lock (queueLock) return lastError; } }

        private NetConnection(TcpClient connectedClient, int maxPendingPackets, int maxPendingBytes)
        {
            client = connectedClient;
            try
            {
                if (maxPendingPackets < 1) throw new ArgumentOutOfRangeException(nameof(maxPendingPackets));
                if (maxPendingBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxPendingBytes));
                this.maxPendingPackets = maxPendingPackets;
                this.maxPendingBytes = maxPendingBytes;
                client.NoDelay = true;
                stream = client.GetStream();
                receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "RpgNetReceive" };
                running = true;
                receiveThread.Start();
            }
            catch
            {
                running = false;
                client.Close();
                throw;
            }
        }

        /// <summary>Blocking connect. Future UI callers must run connection establishment off the main thread.</summary>
        public static NetConnection Connect(string host, int port,
            int maxPendingPackets = DefaultMaxPendingPackets, int maxPendingBytes = DefaultMaxPendingBytes)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host is required.", nameof(host));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            var client = new TcpClient();
            try
            {
                client.Connect(host, port);
                return new NetConnection(client, maxPendingPackets, maxPendingBytes);
            }
            catch
            {
                client.Close();
                throw;
            }
        }

        /// <summary>Transfers ownership of an already accepted client, including on initialization failure.</summary>
        public static NetConnection Adopt(TcpClient acceptedClient,
            int maxPendingPackets = DefaultMaxPendingPackets, int maxPendingBytes = DefaultMaxPendingBytes)
        {
            if (acceptedClient == null) throw new ArgumentNullException(nameof(acceptedClient));
            return new NetConnection(acceptedClient, maxPendingPackets, maxPendingBytes);
        }

        /// <summary>Call on the Unity main thread: the sole protocol codec serializes before writing.</summary>
        public void Send(object dto, bool handshakeCompleted = false)
        {
            if (!IsOpen) throw new InvalidOperationException("Connection is closed.");
            byte[] packet = ProtocolCodec.Encode(dto, handshakeCompleted);
            lock (sendLock)
            {
                if (!IsOpen) throw new InvalidOperationException("Connection is closed.");
                try { stream.Write(packet, 0, packet.Length); }
                catch (Exception error) when (IsTransportError(error))
                {
                    Stop(error);
                    throw;
                }
            }
        }

        // No automatic drain, JSON parsing, Unity access or business callbacks.
        public bool TryDequeue(out ReceivedPacket packet)
        {
            lock (queueLock)
            {
                packet = receiveQueue.Count == 0 ? null : receiveQueue.Dequeue();
                if (packet != null) pendingBytes -= packet.Payload.Length + NetPacket.ProtocolIdSize;
                return packet != null;
            }
        }

        private void ReceiveLoop()
        {
            long receivingSession = SessionId;
            try
            {
                while (running && NetPacket.TryRead(stream, out short id, out byte[] payload))
                {
                    lock (queueLock)
                    {
                        if (!IsOpen || receivingSession != SessionId) break;
                        int packetBytes = payload.Length + NetPacket.ProtocolIdSize;
                        // Overflow closes the connection instead of silently dropping ordered requests/results.
                        if (receiveQueue.Count >= maxPendingPackets || packetBytes > maxPendingBytes - pendingBytes)
                            throw new InvalidDataException("Receive queue limit exceeded.");
                        receiveQueue.Enqueue(new ReceivedPacket(receivingSession, id, payload));
                        pendingBytes += packetBytes;
                    }
                }
            }
            catch (Exception error) when (IsTransportError(error))
            {
                if (IsOpen) { lock (queueLock) lastError = error; }
            }
            finally { Stop(null); }
        }

        private static bool IsTransportError(Exception error)
        {
            // InvalidDataException derives from SystemException, not IOException.
            // Both invalid frame lengths and queue overflow must be contained by the receive loop.
            return error is IOException || error is InvalidDataException ||
                error is SocketException || error is ObjectDisposedException;
        }

        private void Stop(Exception error)
        {
            if (error != null) { lock (queueLock) lastError = error; }
            running = false;
            // Closing the socket interrupts a blocking read/write without taking sendLock.
            try { stream.Close(); }
            finally { client.Close(); }
        }

        public void Dispose()
        {
            lock (disposeLock)
            {
                Interlocked.Exchange(ref disposed, 1);
                try
                {
                    Stop(null);
                    // Do not silently report a successful close while a receiving thread is still alive.
                    if (Thread.CurrentThread != receiveThread && !receiveThread.Join(2000))
                        throw new TimeoutException("Receive thread did not terminate after closing the socket.");
                }
                finally
                {
                    lock (queueLock)
                    {
                        receiveQueue.Clear();
                        pendingBytes = 0;
                    }
                }
            }
        }
    }
}
