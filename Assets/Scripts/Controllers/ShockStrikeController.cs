using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Unity.VisualScripting.Antlr3.Runtime;
using UnityEngine;

public class ShockStrikeController : MonoBehaviour, ITemporaryEffectPlayback
{
    [SerializeField] private CharaterStats targetStats;
    [SerializeField] private float speed;
    public float TravelSpeed => speed;
    private int damage;

    private Animator anim;
    private bool isTriggered = false;
    private bool networkView, initialized;
    private Vector2 networkGoal;
    private Vector3 originalScale, childPosition;
    private Quaternion childRotation;
    private bool originalEvents, originalRootMotion;
    private void Awake() => Initialize();
    private void Initialize()
    {
        if (initialized) return;
        anim = GetComponentInChildren<Animator>(true);
        originalScale = transform.localScale;
        if (anim != null) { childPosition = anim.transform.localPosition; childRotation = anim.transform.localRotation; originalEvents = anim.fireEvents; originalRootMotion = anim.applyRootMotion; }
        initialized = true;
    }
    public void PlayConfirmedChain(Rpg.Networking.Protocol.ChainStrikeDto state)
    {
        Initialize();
        if (!networkView)
        {
            CancelInvoke(); StopAllCoroutines(); networkView = true; targetStats = null; isTriggered = false;
            transform.localScale = originalScale;
            transform.position = new Vector3(state.position[0], state.position[1], transform.position.z);
            if (anim != null)
            {
                anim.transform.localPosition = childPosition; anim.transform.localRotation = childRotation;
                anim.fireEvents = false; anim.applyRootMotion = false; anim.updateMode = AnimatorUpdateMode.UnscaledTime;
                anim.Rebind(); anim.Play(0, 0, 0); anim.Update(0);
            }
        }
        networkGoal = new Vector2(state.position[0], state.position[1]);
        if (!isTriggered && state.phase == Rpg.Networking.Protocol.ChainStrikePhase.Impact)
        {
            transform.position = new Vector3(networkGoal.x, networkGoal.y, transform.position.z);
            isTriggered = true; transform.localRotation = Quaternion.identity; transform.localScale = new Vector3(3, 3, 3);
            if (anim != null) { anim.transform.localPosition = new Vector3(0, .5f); anim.transform.localRotation = Quaternion.identity; anim.SetTrigger("Hit"); }
        }
        else if (!isTriggered && new Vector2(state.direction[0], state.direction[1]).sqrMagnitude > 0)
            transform.right = new Vector3(state.direction[0], state.direction[1], 0);
    }
    public void ReturnNetworkView() => GetComponent<PooledTemporaryEffect>()?.ReturnToPool();
    public void StopPlayback()
    {
        Initialize(); CancelInvoke(); StopAllCoroutines(); networkView = false; isTriggered = false; targetStats = null;
        transform.localScale = originalScale;
        if (anim != null) { anim.transform.localPosition = childPosition; anim.transform.localRotation = childRotation; anim.fireEvents = originalEvents; anim.applyRootMotion = originalRootMotion; }
    }

    private void Start()
    {
        Initialize();
    }

    public void SetUp(int _damage, CharaterStats _targetStats)
    {
        if (Rpg.Networking.EntityRuntimePermissions.NetworkActive) return;
        damage = _damage;
        targetStats = _targetStats;
    }

    private void Update()
    {
        if (networkView)
        {
            if (!isTriggered) transform.position = Vector2.MoveTowards(transform.position, networkGoal, speed * GameSimulationClock.DeltaTime);
            return; // Display goal only. No target search, arrival judgement, damage or local delayed callback.
        }
        if (Rpg.Networking.EntityRuntimePermissions.NetworkActive) return;
        if (!targetStats || isTriggered)
            return;

        transform.position = Vector2.MoveTowards(transform.position, targetStats.transform.position, speed * GameSimulationClock.DeltaTime);
        transform.right = transform.position - targetStats.transform.position;

        if (Vector2.Distance(transform.position, targetStats.transform.position) < 0.1f)
        {
            anim.transform.localPosition = new Vector3(0, 0.5f);
            anim.transform.localRotation = Quaternion.identity;
            transform.localRotation = Quaternion.identity;
            transform.localScale = new Vector3(3, 3);

            isTriggered = true;
            anim.SetTrigger("Hit");
            GameSimulationClock.Schedule(this, DamageAndSelfDestroy, nameof(DamageAndSelfDestroy), 0.2f);
        }
    }

    private void DamageAndSelfDestroy()
    {
        if (networkView || Rpg.Networking.EntityRuntimePermissions.NetworkActive || targetStats == null) return;
        targetStats.ApplyShock(true);
        targetStats.TakeDamage(damage);
        GameSimulationClock.DestroyAfter(gameObject, 0.4f);
    }
}
