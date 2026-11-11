using UnityEngine;

public class PlayerBlackholeState : PlayerState
{
    private bool skillUsed;
    private float flyTime = 0.4f;
    private float defaultGravity;

    public PlayerBlackholeState(Player _player, PlayerStateMachine _stateMachine, string _animBoolName) : base(_player, _stateMachine, _animBoolName)
    {
    }

    public override void AnimationFinishTrigger()
    {
        base.AnimationFinishTrigger();
    }

    public override void Enter()
    {
        base.Enter();
        defaultGravity = player.rb.gravityScale;
        rb.gravityScale = 0;
        stateTimer = flyTime;
        skillUsed = false;
    }

    public override void Exit()
    {
        base.Exit();
        player.rb.gravityScale = defaultGravity;

        player.fx.MakeTransparent(false);
    }

    public override void Update()
    {
        base.Update();
        if (stateTimer > 0) rb.linearVelocity = new Vector2(0, 15);
        else
        {
            rb.linearVelocity = new Vector2(0, -0.1f);
            if (!skillUsed)
            {
                if (GameSimulationClock.IsNetwork) { player.RoomBlackhole.Spawn(player); skillUsed = true; }
                else if (player.skill.blackhole.CanUseSkill()) skillUsed = true;
            }
        }

        if (GameSimulationClock.IsNetwork ? player.RoomBlackhole.Completed(player) : player.skill.blackhole.SkillCompleted())
            stateMachine.ChangeState(player.airState);
    }
}
