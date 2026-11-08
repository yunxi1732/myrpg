using System.Diagnostics;

namespace Rpg.Networking
{
    public static class MonotonicClock
    {
        public static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    }

    public enum NetworkDisconnectReason { RemoteClosed, Timeout, TransportError }

    public sealed class NetworkDisconnectedEvent
    {
        public string RoomSessionId { get; }
        public long ConnectionSessionId { get; }
        public NetworkDisconnectReason Reason { get; }
        public string Detail { get; }
        internal NetworkDisconnectedEvent(string room, long connection, NetworkDisconnectReason reason, string detail)
        {
            RoomSessionId = room; ConnectionSessionId = connection; Reason = reason; Detail = detail;
        }
    }
}
