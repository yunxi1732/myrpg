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
        public bool LastSentSlideDown { get; private set; }
        public int MinimumIntervalMilliseconds
        {
            get => minimumIntervalMilliseconds;
            set => minimumIntervalMilliseconds = Math.Max(30, Math.Min(50, value));
        }

        public bool TrySend(float currentAxis, double now, Action<MovementInputDto> send, bool slideDown = false)
        {
            if (stopPending) { currentAxis = 0; slideDown = false; } // A lock's neutral state precedes new input.
            if (SentSequence > 0)
            {
                double elapsed = now - lastSendTime;
                if (elapsed + 1e-9 < minimumIntervalMilliseconds / 1000.0 ||
                    !stopPending && currentAxis == LastSentAxis && slideDown == LastSentSlideDown && elapsed + 1e-9 < ResendInterval) return false;
            }
            var message = new MovementInputDto { inputSeq = checked(SentSequence + 1), currentAxis = currentAxis, slideDown = slideDown };
            send(message);
            SentSequence = message.inputSeq;
            LastSentAxis = currentAxis;
            LastSentSlideDown = slideDown;
            lastSendTime = now;
            stopPending = false;
            return true;
        }

        public void Reset()
        {
            SentSequence = 0;
            LastSentAxis = 0;
            LastSentSlideDown = false;
            lastSendTime = 0;
            stopPending = false;
        }
    }
}
