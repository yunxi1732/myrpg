using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Rpg.Networking;

public class PlayerPrimaryAttack : PlayerState
{
    public PlayerPrimaryAttack(Player _player, PlayerStateMachine _stateMachine, string _animBoolName) : base(_player, _stateMachine, _animBoolName)
    {
    }

    public int comboCounter { get; private set; }
    private float lastTimeAttacked;
    private float comboWindow = 1.5f;
    public const float NetworkComboWindowSeconds = 1.5f;
    public static int NetworkComboWindowTicks => Mathf.Max(1, Mathf.CeilToInt(NetworkComboWindowSeconds / GameSimulationClock.FixedDeltaTime));
    private PrimaryAttackInstance networkInstance;
    private bool networkCompleted;

    // First-strike compatibility helpers for the 4-33a verification entry.
    public static int NetworkDurationTicks(Animator animator)
    {
        return TryNetworkTiming(animator, out int duration, out _, out _) ? duration : 0;
    }

    public static bool TryNetworkTiming(Animator animator, out int duration, out int hitStart, out int hitEnd)
    {
        duration = hitStart = hitEnd = 0;
        if (!TryNetworkTiming(animator, 0, out var timing)) return false;
        duration = timing.DurationTicks;
        hitStart = timing.Segments[0].StartOffsetTicks; hitEnd = timing.Segments[0].EndOffsetTicks;
        return true;
    }

    public static bool TryNetworkTiming(Animator animator, int comboIndex, out PrimaryAttackTiming timing)
    {
        timing = null;
        if (animator == null || animator.runtimeAnimatorController == null || comboIndex < 0 || comboIndex > 2) return false;
        foreach (var clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip.name != "PlayerAttack" + (comboIndex + 1)) continue;
            float finish = clip.length;
            var hits = new List<float>();
            foreach (var marker in clip.events)
            {
                if (marker.functionName == "AnimationTrigger") finish = Mathf.Min(finish, marker.time);
                if (marker.functionName == "AttackTrigger") hits.Add(marker.time);
            }
            return TryCreateNetworkTiming(comboIndex, finish, clip.frameRate, GameSimulationClock.FixedDeltaTime, hits.ToArray(), out timing);
        }
        return false;
    }

    public static bool TryCreateNetworkTiming(int comboIndex, float finish, float frameRate, float step, float[] hitTimes, out PrimaryAttackTiming timing)
        => PrimaryAttackTiming.TryCreate(comboIndex, finish, frameRate, step, hitTimes, out timing);

    public override void Enter()
    {
        base.Enter();
        if (GameSimulationClock.IsNetwork)
        {
            player.anim.speed = 1; // Attack windows/playback use authority ticks, independently of chilled movement animation.
            networkInstance = player.Combat.PrimaryAttack.Current;
            networkCompleted = false;
            comboCounter = networkInstance.ComboIndex;
            player.anim.SetInteger("comboCounter", comboCounter);
            stateTimer = 0.1f;
            player.SetVelocity(player.attackMovement[comboCounter] * player.facingDir, 2);
            player.FlipController(networkInstance.FacingDir);
            return;
        }

        if (GameSimulationClock.Time > lastTimeAttacked + comboWindow || comboCounter > 2) comboCounter = 0;
        AudioManager.instance.PlaySFX(comboCounter, null);
        player.anim.SetInteger("comboCounter", comboCounter);
        stateTimer = 0.1f;
        player.SetVelocity(player.attackMovement[comboCounter] * player.facingDir, 2);
    }

    public override void Exit()
    {
        base.Exit();
        if (GameSimulationClock.IsNetwork)
        {
            player.Combat.PrimaryAttack?.FinishCurrent(networkInstance, GameSimulationClock.AuthorityTick, networkCompleted);
            player.anim.speed = player.GetComponent<NetworkElementState>()?.NormalAnimationSpeed ?? 1;
            networkInstance = null;
            return;
        }
        comboCounter++;
        lastTimeAttacked = GameSimulationClock.Time;

    }

    public override void Update()
    {
        base.Update();
        if (GameSimulationClock.IsNetwork)
        {
            var attack = networkInstance;
            if (attack != null && !attack.IsClosed) player.FlipController(attack.FacingDir);
            if (attack == null || attack.IsClosed || GameSimulationClock.AuthorityTick >= attack.EndTick)
            {
                networkCompleted = attack != null && !attack.IsClosed && GameSimulationClock.AuthorityTick >= attack.EndTick;
                stateMachine.ChangeState(player.IsGroundDetected() ? (PlayerState)player.idleState : player.airState);
                return;
            }
            if (stateTimer <= 0) player.SetVelocity(0, 0);
            return;
        }
        if (triggerCalled) stateMachine.ChangeState(player.idleState);
        if (stateTimer < 0) player.SetVelocity(0, 0);
    }
}
