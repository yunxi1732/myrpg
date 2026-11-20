using System;
using System.Collections.Generic;
using System.IO;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;

namespace Rpg.Networking
{
    // One main-thread Unity Transport driver for a room listener or a joining client.
    internal sealed class UtpRoomTransport : IDisposable
    {
        private NetworkDriver driver;
        private readonly NetworkPipeline reliable;
        private readonly Dictionary<NetworkConnection, NetConnection> connections = new Dictionary<NetworkConnection, NetConnection>();
        private readonly Queue<NetConnection> accepted = new Queue<NetConnection>();
        private readonly bool isHost;
        public NetConnection ClientConnection { get; private set; }
        public bool ConnectFailed { get; private set; }

        private UtpRoomTransport(bool isHost, int sendDelayMs, int sendJitterMs, float sendLossPercent)
        {
            this.isHost = isHost;
            var settings = new NetworkSettings();
            try
            {
                settings.WithFragmentationStageParameters(payloadCapacity: 32 * 1024);
                settings.WithNetworkSimulatorParameters(sendDelayMS: (uint)sendDelayMs,
                    sendJitterMS: (uint)sendJitterMs, sendPacketLossPercent: sendLossPercent);
                driver = NetworkDriver.Create(settings);
            }
            finally { settings.Dispose(); }
            reliable = driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
        }

        public static UtpRoomTransport Listen(int port, int sendDelayMs = 0, int sendJitterMs = 0, float sendLossPercent = 0)
        {
            var transport = new UtpRoomTransport(true, sendDelayMs, sendJitterMs, sendLossPercent);
            try
            {
                int status = transport.driver.Bind(NetworkEndpoint.AnyIpv4.WithPort((ushort)port));
                if (status != 0) throw new IOException("UDP bind failed: " + status);
                status = transport.driver.Listen();
                if (status != 0) throw new IOException("UDP listen failed: " + status);
                return transport;
            }
            catch { transport.Dispose(); throw; }
        }

        public static UtpRoomTransport Connect(string address, int port, int sendDelayMs = 0, int sendJitterMs = 0, float sendLossPercent = 0)
        {
            if (!NetworkEndpoint.TryParse(address, (ushort)port, out var endpoint, NetworkFamily.Ipv4))
                throw new ArgumentException("Invalid IPv4 address.", nameof(address));
            var transport = new UtpRoomTransport(false, sendDelayMs, sendJitterMs, sendLossPercent);
            try
            {
                NetworkConnection handle = transport.driver.Connect(endpoint);
                if (!handle.IsCreated) throw new IOException("UDP connect could not start.");
                transport.ClientConnection = new NetConnection(transport, handle, false);
                transport.connections.Add(handle, transport.ClientConnection);
                return transport;
            }
            catch { transport.Dispose(); throw; }
        }

        public bool TryAccept(out NetConnection connection)
        {
            connection = accepted.Count > 0 ? accepted.Dequeue() : null;
            return connection != null;
        }

        public void Update()
        {
            driver.ScheduleUpdate().Complete();
            if (isHost)
            {
                NetworkConnection handle;
                while ((handle = driver.Accept()).IsCreated)
                {
                    var connection = new NetConnection(this, handle, true);
                    connections.Add(handle, connection);
                    accepted.Enqueue(connection);
                }
            }
            foreach (var pair in connections)
            {
                NetworkEvent.Type eventType;
                while ((eventType = driver.PopEventForConnection(pair.Key, out var reader)) != NetworkEvent.Type.Empty)
                {
                    if (eventType == NetworkEvent.Type.Connect) pair.Value.UtpConnected();
                    else if (eventType == NetworkEvent.Type.Disconnect)
                    {
                        pair.Value.UtpDisconnected();
                        if (!isHost) ConnectFailed = true;
                    }
                    else if (eventType == NetworkEvent.Type.Data)
                    {
                        try
                        {
                            if (reader.Length < 4 + NetPacket.ProtocolIdSize)
                                throw new InvalidDataException("Invalid UDP room packet.");
                            int length = reader.ReadByte() | reader.ReadByte() << 8 |
                                reader.ReadByte() << 16 | reader.ReadByte() << 24;
                            if (length < NetPacket.ProtocolIdSize || length > NetPacket.MaxBodyLength ||
                                reader.Length != 4 + length)
                                throw new InvalidDataException("Invalid UDP room packet.");
                            short id = unchecked((short)(reader.ReadByte() | reader.ReadByte() << 8));
                            var payload = new byte[length - NetPacket.ProtocolIdSize];
                            for (int i = 0; i < payload.Length; i++) payload[i] = reader.ReadByte();
                            pair.Value.UtpReceived(id, payload);
                        }
                        catch (IOException error) { pair.Value.UtpFailed(error); driver.Disconnect(pair.Key); break; }
                    }
                }
            }
        }

        public int UnreliablePayloadCapacity(NetworkConnection connection)
            => driver.GetMaxSupportedPayloadSize(connection, NetworkPipeline.Null);

        public void SetTestNetwork(int sendDelayMs, int sendJitterMs, float sendLossPercent)
        {
            driver.ModifyNetworkSimulatorParameters(new NetworkSimulatorParameter
            {
                SendDelayMS = (uint)sendDelayMs,
                SendJitterMS = (uint)sendJitterMs,
                SendPacketLossPercent = sendLossPercent
            });
        }

        public void Send(NetworkConnection connection, byte[] packet, bool unreliable)
        {
            NetworkPipeline pipeline = unreliable ? NetworkPipeline.Null : reliable;
            int status = driver.BeginSend(pipeline, connection, out var writer, packet.Length);
            if (status < 0) throw new IOException("UDP BeginSend failed: " + status);
            for (int i = 0; i < packet.Length; i++) writer.WriteByte(packet[i]);
            status = driver.EndSend(writer);
            if (status == -5) // Unity Transport 2.6: NetworkSendQueueFull.
                throw new IOException("UDP send queue is full.");
            if (status < 0) throw new IOException("UDP EndSend failed: " + status);
            if (status != packet.Length) throw new IOException("UDP EndSend wrote " + status + " of " + packet.Length + " bytes.");
        }

        public void Flush() => driver.ScheduleFlushSend().Complete();

        public void Disconnect(NetworkConnection connection)
        {
            if (driver.IsCreated && driver.GetConnectionState(connection) != NetworkConnection.State.Disconnected)
                driver.Disconnect(connection);
            connections.Remove(connection);
        }

        public void Dispose()
        {
            foreach (var connection in connections.Values) connection.UtpDisconnected();
            connections.Clear();
            accepted.Clear();
            if (driver.IsCreated) driver.Dispose();
        }
    }
}
