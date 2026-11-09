using UnityEngine;
using Rpg.Networking.Protocol;

/// <summary>Latest input state for one player. Reading it never consumes or accumulates movement.</summary>
public sealed class PlayerInputData
{
    public float CurrentAxis { get; private set; }
    public long InputSeq { get; private set; }
    public bool WallSlideDown { get; private set; }
    public long WallSlideInputSeq { get; private set; }

    internal void SetCurrentAxis(float axis, bool slideDown = false)
    {
        CurrentAxis = Mathf.Clamp(axis, -1f, 1f);
        WallSlideDown = slideDown;
    }
    internal void Clear() { CurrentAxis = 0; WallSlideDown = false; }

    internal bool TryApplyWallSlideInput(WallSlideInputDto input)
    {
        if (input == null || input.inputSeq <= WallSlideInputSeq) return false;
        WallSlideInputSeq = input.inputSeq;
        WallSlideDown = input.slideDown;
        return true;
    }

    internal bool TryApplyNetworkInput(MovementInputDto input)
    {
        if (input == null || input.inputSeq <= InputSeq || !ProtocolCodec.IsValidMovementAxis(input.currentAxis)) return false;
        InputSeq = input.inputSeq;
        CurrentAxis = input.currentAxis; // Absolute input state, never displacement and never +=.
        return true;
    }
}
