using UnityEngine;

public class BossCastState : EnemyState
{
    private Enemy_Boss enemy;
    private int amountOfSpells;
    private float spellCooldown;
    private float spellTimer;

    public BossCastState(Enemy _enemyBase, EnemyStateMachine _stateMachine, string _animBoolName, Enemy_Boss _enemy) : base(_enemyBase, _stateMachine, _animBoolName)
    {
        this.enemy = _enemy;
    }

    public override void Enter()
    {
        base.Enter();
        amountOfSpells = enemy.amountOfSpells;
        spellTimer = 0.5f;
    }

    public override void Update()
    {
        base.Update();
        spellTimer -= Time.deltaTime;
        if (CanCast())
        {
            enemy.CastSpell();
            amountOfSpells--;
        }
        if(amountOfSpells <= 0) stateMachine.ChangeState(enemy.teleportState);
    }

    public override void Exit()
    {
        base.Exit();
    }

    private bool CanCast()
    {
        if (amountOfSpells > 0 && spellTimer < 0)
        {
            spellTimer = enemy.spellCooldown;
            return true;
        }
        return false;
    }
}
