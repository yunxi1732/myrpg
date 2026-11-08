namespace Rpg.Networking
{
    public enum RoomExitReason { HostClosed, GuestLeft }

    public sealed class RoomExitEvent
    {
        public string RoomSessionId { get; }
        public long ConnectionSessionId { get; }
        public RoomExitReason Reason { get; }
        public bool IsLocal { get; }

        internal RoomExitEvent(string room, long connection, RoomExitReason reason, bool isLocal)
        {
            RoomSessionId = room; ConnectionSessionId = connection; Reason = reason; IsLocal = isLocal;
        }
    }
}
