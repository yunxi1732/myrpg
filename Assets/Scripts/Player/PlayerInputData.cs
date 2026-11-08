using UnityEngine;
using Rpg.Networking.Protocol;

/// <summary>Latest input state for one player. Reading it never consumes or accumulates movement.</summary>
public sealed class PlayerInputData
{
    public float CurrentAxis { get; private set; }
    public long InputSeq { get; private set; }

    internal void SetCurrentAxis(float axis) => CurrentAxis = Mathf.Clamp(axis, -1f, 1f);
    internal void Clear() => CurrentAxis = 0;

    internal bool TryApplyNetworkInput(MovementInputDto input)
    {
        if (input == null || input.inputSeq <= InputSeq || !ProtocolCodec.IsValidMovementAxis(input.currentAxis)) return false;
        InputSeq = input.inputSeq;
        CurrentAxis = input.currentAxis; // Absolute input state, never displacement and never +=.
        return true;
    }
}
