using UnityEngine;

public class BossAttackTrigger : EnemyAnimatorTriggers
{
    private Enemy_Boss enemyBoss => GetComponentInParent<Enemy_Boss>();

    public void Relocate() => enemyBoss.FindPosition();

    private void MakeInvisible() => enemyBoss.fx.MakeTransparent(true);
    private void MakeVisible() => enemyBoss.fx.MakeTransparent(false);
}
