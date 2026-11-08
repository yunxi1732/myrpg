using System;
using System.Collections.Generic;
using System.Threading;
using Rpg.Networking.Protocol;

namespace Rpg.Networking
{
    /// <summary>Register before InitAllModules; ArchitectureHost drives this system on Unity Update.</summary>
    public sealed class NetworkSystem : ISystem, IUpdateModule, IDisposable
    {
        private readonly IArchitecture architecture;
        private readonly int maxPacketsPerUpdate;
        private NetConnection connection;
        private NetConnection closingConnection;
        private int mainThreadId;
        private bool disposed;
        private readonly List<Action> sessionUnsubscribers = new List<Action>();

        public long SessionId { get; private set; }
        public int StalePacketCount { get; private set; }
        public string RoomSessionId { get; private set; }

        public int ProcessedCount { get; private set; }
        public int RejectedCount { get; private set; }
        public Exception LastMessageError { get; private set; }

        public NetworkSystem(IArchitecture architecture, int maxPacketsPerUpdate = 32)
        {
            this.architecture = architecture ?? throw new ArgumentNullException(nameof(architecture));
            if (maxPacketsPerUpdate < 1) throw new ArgumentOutOfRangeException(nameof(maxPacketsPerUpdate));
            this.maxPacketsPerUpdate = maxPacketsPerUpdate;
        }

        public void Init()
        {
            if (disposed) throw new ObjectDisposedException(nameof(NetworkSystem));
            if (mainThreadId != 0) throw new InvalidOperationException("Network system already initialized.");
            // Architecture initialization is performed by the Unity owner on its main thread.
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>Transfers ownership. This step supports one connection, bootstrap messages only.</summary>
        public void AttachConnection(NetConnection value)
        {
            RequireMainThread();
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (!value.IsOpen) throw new InvalidOperationException("Attach a new open connection for each session.");
            if (connection != null || closingConnection != null)
                throw new InvalidOperationException("Close the previous connection completely before attaching another.");
            connection = value;
            SessionId = value.SessionId;
            ProcessedCount = RejectedCount = StalePacketCount = 0;
            LastMessageError = null;
        }

        // Called only by the local handshake handler after A's confirmation, never copied from gameplay input.
        internal void ConfirmRoom(string roomSessionId)
        {
            RequireMainThread();
            if (connection == null || !Guid.TryParseExact(roomSessionId, "N", out _))
                throw new InvalidOperationException("A valid connected room is required.");
            RoomSessionId = roomSessionId;
        }

        /// <summary>Session-owned subscription. Closing removes it without touching unrelated game events.</summary>
        public void RegistSessionEvent<T>(Action<object> callback)
        {
            RequireMainThread();
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (connection == null) throw new InvalidOperationException("Attach a session before subscribing.");
            long subscribedSession = SessionId;
            Action<object> guarded = dto =>
            {
                // Also guards callbacks already snapshotted by the event system when a handler closes/reopens.
                if (!disposed && SessionId == subscribedSession) callback(dto);
            };
            architecture.RegistEvent<T>(guarded);
            sessionUnsubscribers.Add(() => architecture.UnRegistEvent<T>(guarded));
        }

        public void CloseSession()
        {
            RequireMainThread();
            // Invalidate first, before waiting for the old receiver or touching subscriptions.
            SessionId = 0;
            RoomSessionId = null;
            NetConnection previous = connection ?? closingConnection;
            connection = null;
            closingConnection = previous;
            foreach (Action unsubscribe in sessionUnsubscribers) unsubscribe();
            sessionUnsubscribers.Clear();
            LastMessageError = null;
            previous?.Dispose();
            closingConnection = null;
        }

        public void Update()
        {
            if (disposed) return;
            RequireMainThread();
            NetConnection updatingConnection = connection;
            long updatingSession = SessionId;
            for (int i = 0; i < maxPacketsPerUpdate && !disposed && updatingConnection != null &&
                ReferenceEquals(connection, updatingConnection) && SessionId == updatingSession; i++)
            {
                if (!updatingConnection.TryDequeue(out NetConnection.ReceivedPacket packet)) break;
                if (packet.SessionId != updatingSession)
                {
                    StalePacketCount++;
                    continue;
                }
                try
                {
                    ProtocolCodec.DecodeAndDispatch(packet.ProtocolId, packet.Payload, architecture,
                        !string.IsNullOrEmpty(RoomSessionId));
                    if (SessionId == updatingSession) ProcessedCount++;
                }
                catch (Exception error)
                {
                    // Unknown IDs, invalid JSON/fields and handler failures cannot stop later packets.
                    if (SessionId == updatingSession)
                    {
                        RejectedCount++;
                        LastMessageError = error;
                    }
                }
            }
        }

        private void RequireMainThread()
        {
            if (disposed) throw new ObjectDisposedException(nameof(NetworkSystem));
            if (mainThreadId == 0 || mainThreadId != Thread.CurrentThread.ManagedThreadId)
                throw new InvalidOperationException("Network operations require the architecture's Unity initialization thread.");
        }

        public void Dispose()
        {
            if (disposed) return;
            RequireMainThread();
            CloseSession();
            disposed = true;
        }
    }
}
