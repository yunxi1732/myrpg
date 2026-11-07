using UnityEngine;

public class AfterImageFx : MonoBehaviour, ITemporaryEffectPlayback
{
    private SpriteRenderer sr;
    private PooledTemporaryEffect pooledEffect;
    private Color initialColor;
    private Vector3 initialScale;
    private bool initialized;
    public bool IsPlaying { get; private set; }
    private float colorLoseRate;

    private void Awake() { Initialize(); }

    private void Initialize()
    {
        if (initialized) return;
        sr = GetComponent<SpriteRenderer>();
        initialColor = sr.color;
        initialScale = transform.localScale;
        initialized = true;
    }

    /// <summary>Prepare every frame snapshot using the prefab's original tint, alpha and scale.</summary>
    public void Play(float fadeSpeed, Sprite image, Vector3 position, Quaternion rotation)
    {
        // Supports preparation while inactive, before Awake has run.
        Initialize();
        StopPlayback();
        pooledEffect = GetComponent<PooledTemporaryEffect>();
        sr.sprite = image;
        sr.color = initialColor;
        colorLoseRate = fadeSpeed;
        IsPlaying = true;
        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = initialScale;
    }

    // Keep the existing entry available for callers that already set the spawn pose.
    public void SetupAfterImage(float _speed, Sprite _iamge)
    {
        Play(_speed, _iamge, transform.position, transform.rotation);
    }

    private void OnDisable() { StopPlayback(); }

    public void StopPlayback()
    {
        IsPlaying = false;
        colorLoseRate = 0f;
        // Only cancel work owned by this residual image, never the character's PlayerFX/EntityFX.
        CancelInvoke();
        StopAllCoroutines();
    }

    private void Update()
    {
        // Pausing must not advance or complete this game-time playback.
        if (!IsPlaying || Time.deltaTime <= 0f) return;
        float alpha = sr.color.a - colorLoseRate * Time.deltaTime;
        sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, alpha);
        if (sr.color.a <= 0 && pooledEffect != null)
            pooledEffect.ReturnToPool();
    }
}
