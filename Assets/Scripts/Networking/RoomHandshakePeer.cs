using System;

namespace Rpg.Networking
{
    /// <summary>Connection-scoped dispatch using the existing architecture and sole protocol registry.</summary>
    internal sealed class RoomHandshakePeer : IDisposable
    {
        private readonly Architecture architecture = new Architecture();
        public readonly NetworkSystem Network;
        private readonly NetConnection connection;
        private string roomSessionId;
        private double lastReceive, lastSend;
        private long sentSequence, receivedSequence;
        private bool disconnected;
        public event Action<NetworkDisconnectedEvent> Disconnected;

        public RoomHandshakePeer(NetConnection connection)
        {
            this.connection = connection;
            Network = new NetworkSystem(architecture);
            architecture.RegistSystem(Network);
            architecture.InitAllModules();
            try { Network.AttachConnection(connection); }
            catch { architecture.Dispose(); throw; }
            Subscribe<Protocol.HeartbeatDto>(value =>
            {
                var heartbeat = (Protocol.HeartbeatDto)value;
                if (roomSessionId == heartbeat.roomSessionId && heartbeat.heartbeatSeq > receivedSequence)
                {
                    receivedSequence = heartbeat.heartbeatSeq;
                    lastReceive = MonotonicClock.Now;
                }
            });
        }

        public void ConfirmRoom(string room)
        {
            Network.ConfirmRoom(room);
            roomSessionId = room;
            lastReceive = lastSend = MonotonicClock.Now;
        }

        public void Subscribe<T>(Action<object> callback) => Network.RegistSessionEvent<T>(callback);
        public void Update()
        {
            architecture.Update();
            if (roomSessionId == null || disconnected) return;
            double now = MonotonicClock.Now;
            if (!connection.IsOpen)
                Signal(connection.LastError == null ? NetworkDisconnectReason.RemoteClosed : NetworkDisconnectReason.TransportError,
                    connection.LastError?.Message);
            else if (now - lastReceive >= 5)
                Signal(NetworkDisconnectReason.Timeout, "No fresh heartbeat for 5 seconds");
            else if (now - lastSend >= 1)
            {
                try
                {
                    connection.Send(new Protocol.HeartbeatDto { roomSessionId = roomSessionId, heartbeatSeq = ++sentSequence }, true);
                    lastSend = now;
                }
                catch (Exception error) { Signal(NetworkDisconnectReason.TransportError, error.Message); }
            }
        }

        private void Signal(NetworkDisconnectReason reason, string detail)
        {
            if (disconnected) return;
            disconnected = true;
            Disconnected?.Invoke(new NetworkDisconnectedEvent(roomSessionId, connection.SessionId, reason, detail));
        }

        public void Dispose()
        {
            disconnected = true;
            roomSessionId = null;
            Disconnected = null;
            architecture.Dispose();
        }
    }

    public sealed class RoomPlayerIdentity
    {
        public string RoomSessionId { get; }
        public int PlayerId { get; }
        public int EntityId { get; }
        internal RoomPlayerIdentity(string session, int player, int entity)
        {
            RoomSessionId = session;
            PlayerId = player;
            EntityId = entity;
        }
    }
}
