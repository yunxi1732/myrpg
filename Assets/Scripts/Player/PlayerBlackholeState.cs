using System.Collections;
using System.Collections.Generic;
using UnityEditor.TextCore.Text;
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

        PlayerManager.instance.player.MakeTransparent(false);
    }

    public override void Update()
    {
        base.Update();
        if (stateTimer > 0) rb.velocity = new Vector2(0, 15);
        else
        {
            rb.velocity = new Vector2(0, -0.1f);
            if (!skillUsed)
            {
                if (player.skill.blackhole.CanUseSkill()) skillUsed = true;
            }
        }
        
        if (player.skill.blackhole.SkillCompleted()) stateMachine.ChangeState(player.airState);
    }
}
