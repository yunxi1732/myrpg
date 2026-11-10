using UnityEngine;
using Rpg.Networking.Protocol;

namespace Rpg.Networking
{
    // Shared identity/source and authority-tick lifetime for room skill objects.
    public sealed class NetworkSkillObject : MonoBehaviour
    {
        public int EntityId { get; internal set; }
        public int SourcePlayerId { get; internal set; }
        public int SourceEntityId { get; internal set; }
        public long SkillInstanceId { get; internal set; }
        public ulong StartTick { get; internal set; }
        public ulong EndTick { get; internal set; }
        public bool IsAuthoritative => GetComponent<NetworkEntityIdentity>().IsAuthoritative;
        internal SwordPhase Phase;
        internal RoomSwordNetwork Room;
        internal PrimaryAttackInstance Attack;
        internal RoomNetworkObjects.Registration Lifetime;
        private readonly AuthorityPositionBuffer positions = new AuthorityPositionBuffer();

        private void FixedUpdate()
        {
            if (IsAuthoritative && GameSimulationClock.AuthorityTick >= EndTick) Lifetime.Dispose();
        }
        internal void Apply(SwordObjectDto value, ulong tick, long seq)
        {
            bool changed = Phase != value.phase;
            if (!positions.Add(tick, seq, new Vector2(value.position[0], value.position[1]),
                .1 / GameSimulationClock.FixedDeltaTime, 5, changed)) return;
            if (positions.Snapped) transform.position = positions.Position;
            transform.right = new Vector2(value.direction[0], value.direction[1]);
            GetComponent<SwordSkillController>().ShowNetworkPhase(value.phase, changed);
            Phase = value.phase;
        }
        private void LateUpdate()
        {
            if (!IsAuthoritative && positions.Advance(GameSimulationClock.DeltaTime, GameSimulationClock.FixedDeltaTime))
                transform.position = positions.Position;
        }
    }
}
