using Rpg.Networking.Protocol;

/// <summary>Latest input state for one player. Reading it never consumes or accumulates movement.</summary>
public sealed class PlayerInputData
{
    public float CurrentAxis { get; private set; }
    public long InputSeq { get; private set; }
    public bool WallSlideDown { get; private set; }

    internal void SetCurrentAxis(float axis, bool slideDown = false)
    {
        CurrentAxis = axis;
        WallSlideDown = slideDown;
    }
    internal void Clear() { CurrentAxis = 0; WallSlideDown = false; }

    internal bool TryApplyNetworkInput(MovementInputDto input)
    {
        if (input.inputSeq <= InputSeq) return false;
        InputSeq = input.inputSeq;
        CurrentAxis = input.currentAxis; // Absolute input state, never displacement and never +=.
        WallSlideDown = input.slideDown;
        return true;
    }
}
