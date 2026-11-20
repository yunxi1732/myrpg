using System;
using System.Collections.Generic;
using UnityEngine;
public class BossSpell_Controller : MonoBehaviour
{
    [SerializeField] private Transform check;
    [SerializeField] private Vector2 boxSize;
    [SerializeField] private LayerMask whatIsPlayer;

    private CharaterStats myStats;
    private Action<Player> networkHit;
    public void SetupSpell(CharaterStats _stats) => myStats = _stats;
    public void SetupNetworkSpell(Action<Player> hit) => networkHit = hit;
    public void SetupVisualSpell() { myStats = null; networkHit = null; }

    private void AnimationTrigger()
    {
        Collider2D[] colliders = Physics2D.OverlapBoxAll(check.position, boxSize, 0, whatIsPlayer);
        var seen = new HashSet<Player>();
        foreach (var hit in colliders)
        {
            Player target = hit.GetComponentInParent<Player>();
            if (target != null && seen.Add(target))
            {
                if (networkHit != null) { networkHit(target); continue; }
                if (myStats == null) continue;
                hit.GetComponent<Entity>().SetupKonckbackDir(transform);
                myStats.DoDamage(hit.GetComponent<CharaterStats>());
            }
        }
    }

    private void OnDrawGizmos() => Gizmos.DrawWireCube(check.position, boxSize);

    private void SelfDestroy() => Destroy(gameObject);
}
