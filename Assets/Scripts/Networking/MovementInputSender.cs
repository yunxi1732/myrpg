using System;
using Rpg.Networking.Protocol;

namespace Rpg.Networking
{
    /// <summary>Poll the latest state each frame. Coalesces changes, never queues movement deltas or actions.</summary>
    public sealed class MovementInputSender
    {
        private const double ResendInterval = 0.1;
        private int minimumIntervalMilliseconds = 40;
        private double lastSendTime;
        private bool stopPending;
        public void RequestStop() => stopPending = true;
        public long SentSequence { get; private set; }
        public float LastSentAxis { get; private set; }
        public int MinimumIntervalMilliseconds
        {
            get => minimumIntervalMilliseconds;
            set => minimumIntervalMilliseconds = Math.Max(30, Math.Min(50, value));
        }

        public bool TrySend(float currentAxis, double now, Action<MovementInputDto> send)
        {
            if (!ProtocolCodec.IsValidMovementAxis(currentAxis)) throw new ArgumentOutOfRangeException(nameof(currentAxis));
            if (send == null) throw new ArgumentNullException(nameof(send));
            if (stopPending) currentAxis = 0; // A lock's zero must be sent before any subsequently sampled direction.
            if (SentSequence > 0)
            {
                double elapsed = now - lastSendTime;
                if (elapsed + 1e-9 < minimumIntervalMilliseconds / 1000.0 ||
                    !stopPending && currentAxis == LastSentAxis && elapsed + 1e-9 < ResendInterval) return false;
            }
            var message = new MovementInputDto { inputSeq = checked(SentSequence + 1), currentAxis = currentAxis };
            send(message);
            SentSequence = message.inputSeq;
            LastSentAxis = currentAxis;
            lastSendTime = now;
            stopPending = false;
            return true;
        }

        public void Reset()
        {
            SentSequence = 0;
            LastSentAxis = 0;
            lastSendTime = 0;
            stopPending = false;
        }
    }
}
