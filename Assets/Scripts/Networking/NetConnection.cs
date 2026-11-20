using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Rpg.Networking.Protocol;
using Unity.Networking.Transport;

namespace Rpg.Networking
{
    /// <summary>One UDP room connection with a bounded main-thread receive queue.</summary>
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

        private readonly UtpRoomTransport utpTransport;
        private readonly NetworkConnection utpHandle;
        private bool utpConnected;
        private readonly object queueLock = new object();
        private readonly Queue<ReceivedPacket> receiveQueue = new Queue<ReceivedPacket>();
        private int disposed;
        private Exception lastError;
        private readonly int maxPendingPackets;
        private readonly int maxPendingBytes;
        private int pendingBytes;
        private static long nextSessionId;

        // Local lifecycle token, not the future wire-level room/handshake ID. Never reused on reconnect.
        public long SessionId { get; } = Interlocked.Increment(ref nextSessionId);
        public const int DefaultMaxPendingPackets = 256;
        public const int DefaultMaxPendingBytes = 4 * 1024 * 1024;

        public bool IsOpen => utpConnected && Volatile.Read(ref disposed) == 0;
        public int PendingCount { get { lock (queueLock) return receiveQueue.Count; } }
        public int PendingBytes { get { lock (queueLock) return pendingBytes; } }
        public Exception LastError { get { lock (queueLock) return lastError; } }
        public int UnreliablePayloadCapacity => utpTransport.UnreliablePayloadCapacity(utpHandle);

        internal NetConnection(UtpRoomTransport transport, NetworkConnection handle, bool connected)
        {
            utpTransport = transport;
            utpHandle = handle;
            utpConnected = connected;
            maxPendingPackets = DefaultMaxPendingPackets;
            maxPendingBytes = DefaultMaxPendingBytes;
        }

        internal void UtpConnected() => utpConnected = true;

        internal void UtpDisconnected() => utpConnected = false;

        internal void UtpFailed(IOException error)
        {
            lock (queueLock) lastError = error;
            utpConnected = false;
        }

        internal void UtpReceived(short protocolId, byte[] payload)
        {
            lock (queueLock)
            {
                int packetBytes = payload.Length + NetPacket.ProtocolIdSize;
                if (receiveQueue.Count >= maxPendingPackets || packetBytes > maxPendingBytes - pendingBytes)
                {
                    lastError = new InvalidDataException("Receive queue limit exceeded.");
                    utpConnected = false;
                    return;
                }
                receiveQueue.Enqueue(new ReceivedPacket(SessionId, protocolId, payload));
                pendingBytes += packetBytes;
            }
        }

        /// <summary>Call on the Unity main thread: the sole protocol codec serializes before writing.</summary>
        public void Send(object dto, bool handshakeCompleted = false)
        {
            if (!IsOpen) throw new InvalidOperationException("Connection is closed.");
            SendEncoded(dto, ProtocolCodec.Encode(dto, handshakeCompleted));
        }

        internal void SendEncoded(object dto, byte[] packet)
        {
            if (!IsOpen) throw new InvalidOperationException("Connection is closed.");
            if ((dto is MovementInputDto || dto is PlayerPositionsDto || dto is MonsterStatesDto) &&
                packet.Length > UnreliablePayloadCapacity)
                throw new InvalidDataException("UDP snapshot/input exceeds payload capacity: " + packet.Length);
            try
            {
                utpTransport.Send(utpHandle, packet,
                    dto is MovementInputDto || dto is PlayerPositionsDto || dto is MonsterStatesDto);
            }
            catch (IOException error)
            {
                UnityEngine.Debug.LogError($"[RoomTransportError] conn={SessionId} message={dto.GetType().Name} {error.Message}");
                throw;
            }
            RoomMessageMetrics.Record(SessionId, true, ProtocolCodec.MessageId(dto.GetType()),
                packet.Length - 4 - NetPacket.ProtocolIdSize, dto);
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

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            utpConnected = false;
            utpTransport.Disconnect(utpHandle);
            lock (queueLock) { receiveQueue.Clear(); pendingBytes = 0; }
        }
    }
}
