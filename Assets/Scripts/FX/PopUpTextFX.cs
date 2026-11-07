using TMPro;
using UnityEngine;

public class PopUpTextFX : MonoBehaviour, ITemporaryEffectPlayback
{
    private TextMeshPro myText;
    private PooledTemporaryEffect pooledEffect;
    private Color initialColor;
    private Vector3 initialScale;
    private bool initialized;
    public bool IsPlaying { get; private set; }

    [SerializeField] private float speed;
    [SerializeField] private float desapearanceSpeed;
    [SerializeField] private float colorDesSpeed;
    [SerializeField] private float lifeTime;

    private float currentSpeed;
    private float textTimer;

    private void Awake()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (initialized) return;
        myText = GetComponent<TextMeshPro>();
        pooledEffect = GetComponent<PooledTemporaryEffect>();
        initialColor = myText.color;
        initialScale = transform.localScale;
        currentSpeed = speed;
        textTimer = lifeTime;
        initialized = true;
    }

    /// <summary>Reset all per-play presentation and timing, including the first play.</summary>
    public void Play(string text)
    {
        // Also supports preparation while inactive, before Awake has run.
        Initialize();
        StopPlayback();
        if (pooledEffect == null) pooledEffect = GetComponent<PooledTemporaryEffect>();
        myText.text = text;
        myText.color = initialColor;
        transform.localScale = initialScale;
        currentSpeed = speed;
        textTimer = lifeTime;
        IsPlaying = true;
    }

    private void OnDisable() { StopPlayback(); }

    public void StopPlayback()
    {
        IsPlaying = false;
        textTimer = 0f;
        currentSpeed = 0f;
        // Only this pop up component: never cancel work on the character's EntityFX.
        CancelInvoke();
        StopAllCoroutines();
    }

    private void Update()
    {
        // Pausing must not advance or complete this game-time playback.
        if (!IsPlaying || Time.deltaTime <= 0f) return;
        transform.position = Vector2.MoveTowards(transform.position, new Vector2(transform.position.x, transform.position.y + 1), currentSpeed * Time.deltaTime);
        textTimer -= Time.deltaTime;

        if (textTimer < 0)
        {
            float a = myText.color.a - colorDesSpeed * Time.deltaTime;
            myText.color = new Color(myText.color.r, myText.color.g, myText.color.b, a);
            // Preserve the recorded baseline; any threshold correction is a separate change.
            if (myText.color.a < 50)
                currentSpeed = desapearanceSpeed;
            if (myText.color.a < 0)
                pooledEffect?.ReturnToPool();
        }
    }
}
