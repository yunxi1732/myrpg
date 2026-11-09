using UnityEngine;

/// <summary>Replay entry for the two sprite-animated hit prefabs. Normal and critical hits use separate prefab pools.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator), typeof(SpriteRenderer))]
public sealed class HitEffectFX : MonoBehaviour, ITemporaryEffectPlayback
{
    public const float DisplayDuration = 0.5f;
    private Animator animator;
    private PooledTemporaryEffect pooledEffect;
    private SpriteRenderer spriteRenderer;
    private Vector3 initialScale;
    private Color initialColor;
    private float initialAnimatorSpeed;
    private bool initialized;
    private float remainingTime;
    public bool IsPlaying { get; private set; }

    private void Awake() { Initialize(); }

    private void Initialize()
    {
        if (initialized) return;
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        initialScale = transform.localScale;
        initialColor = spriteRenderer.color;
        initialAnimatorSpeed = animator.speed;
        initialized = true;
    }

    public void Play(Vector3 position, Quaternion rotation)
    {
        Initialize();
        pooledEffect = GetComponent<PooledTemporaryEffect>();
        CancelInvoke();
        StopAllCoroutines();
        gameObject.SetActive(true);
        animator.enabled = true;
        animator.updateMode = GameSimulationClock.IsNetwork ? AnimatorUpdateMode.UnscaledTime : AnimatorUpdateMode.Normal;
        animator.speed = initialAnimatorSpeed;
        animator.Rebind();
        animator.Play(0, 0, 0f);
        animator.Update(0f);
        // These prefabs have a single Animator and SpriteRenderer, no particle systems.
        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = initialScale;
        spriteRenderer.enabled = true;
        spriteRenderer.color = initialColor;
        remainingTime = DisplayDuration;
        IsPlaying = true;
    }

    private void Update()
    {
        // Pausing must not advance or complete this game-time playback.
        if (!IsPlaying || GameSimulationClock.DeltaTime <= 0f) return;
        remainingTime -= GameSimulationClock.DeltaTime;
        if (remainingTime <= 0f)
        {
            IsPlaying = false;
            if (pooledEffect != null) pooledEffect.ReturnToPool();
            else gameObject.SetActive(false);
        }
    }

    private void OnDisable() { StopPlayback(); }

    public void StopPlayback()
    {
        IsPlaying = false;
        remainingTime = 0f;
        CancelInvoke();
        StopAllCoroutines();
    }
}
