using UnityEngine;

namespace Rpg.Networking
{
    /// <summary>Minimal 2D display view. A/standalone movement never runs through this component.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(9000)]
    [RequireComponent(typeof(NetworkEntityIdentity))]
    public sealed class RemotePlayerView : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float teleportDistance = 5f;
        [SerializeField, Range(0, 0.5f)] private float interpolationDelaySeconds = 0.1f;
        private NetworkEntityIdentity identity;
        private readonly AuthorityPositionBuffer positions = new AuthorityPositionBuffer();
        public float TeleportDistance { get => teleportDistance; set => teleportDistance = Mathf.Max(0.01f, value); }
        public int BufferedStateCount => positions.Count;

        internal void Bind(NetworkEntityIdentity owner)
        {
            if (!ReferenceEquals(identity, owner)) positions.Clear();
            identity = owner;
        }

        public bool ApplyState(ulong tick, long sequence, Vector2 position, bool reset = false)
        {
            if (identity == null || identity.Role != NetworkEntityRole.Display) return false;
            float tickSeconds = GameSimulationClock.FixedDeltaTime; // Shared project's fixed simulation step.
            if (!positions.Add(tick, sequence, position, interpolationDelaySeconds / tickSeconds, teleportDistance, reset)) return false;
            if (positions.Snapped) Place(positions.Position);
            return true;
        }

        private void LateUpdate() => AdvanceDisplay(GameSimulationClock.DeltaTime);

        public void AdvanceDisplay(float unscaledDeltaTime)
        {
            if (identity == null || identity.Role != NetworkEntityRole.Display)
            {
                positions.Clear();
                return;
            }
            if (positions.Advance(unscaledDeltaTime, GameSimulationClock.FixedDeltaTime)) Place(positions.Position);
        }

        private void Place(Vector2 position) => transform.position = new Vector3(position.x, position.y, transform.position.z);
        private void OnDisable() => positions.Clear();
    }
}
