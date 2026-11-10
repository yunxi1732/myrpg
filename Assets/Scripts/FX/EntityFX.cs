using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cinemachine;
using TMPro;
using Action = System.Action;

public class EntityFX : MonoBehaviour
{
    private Coroutine networkBurnColor;
    private Rpg.Networking.Protocol.ElementEffectKind? networkElement;
    public void SetConfirmedElement(Rpg.Networking.Protocol.ElementEffectKind? kind)
    {
        if (networkElement == kind) return;
        networkElement = kind;
        if (!Application.isPlaying || kind.HasValue && !isActiveAndEnabled) return;
        if (sr == null) sr = GetComponentInChildren<SpriteRenderer>(true);
        if (networkBurnColor != null) StopCoroutine(networkBurnColor);
        networkBurnColor = null;
        if (igniteFX != null) igniteFX.Stop();
        if (chillFX != null) chillFX.Stop();
        if (shockFX != null) shockFX.Stop();
        if (sr != null) sr.color = Color.white;
        bool burn = kind == Rpg.Networking.Protocol.ElementEffectKind.Burn;
        bool chill = kind == Rpg.Networking.Protocol.ElementEffectKind.Chill;
        bool shock = kind == Rpg.Networking.Protocol.ElementEffectKind.Shock;
        if (burn && igniteFX != null) igniteFX.Play();
        if (chill && chillFX != null) chillFX.Play();
        if (shock && shockFX != null) shockFX.Play();
        if (sr != null && burn && igniteColor != null && igniteColor.Length >= 2) networkBurnColor = StartCoroutine(PulseColor(IgniteColorFx, .3f));
        if (sr != null && chill && chillColor != null && chillColor.Length >= 2) networkBurnColor = StartCoroutine(PulseColor(ChillColorFx, .3f));
        if (sr != null && shock && shockColor != null && shockColor.Length >= 2) networkBurnColor = StartCoroutine(PulseColor(ShockColorFx, .3f));
    }
    private readonly List<Coroutine> colorRoutines = new List<Coroutine>();

    private void StartColorEffect(Action pulse, string legacyMethod, float interval, float duration = -1)
    {
        if (!GameSimulationClock.IsNetwork)
        {
            InvokeRepeating(legacyMethod, 0, interval);
            if (duration >= 0) Invoke(nameof(CancelColorChange), duration);
            return;
        }
        colorRoutines.Add(StartCoroutine(PulseColor(pulse, interval)));
        if (duration >= 0) colorRoutines.Add(StartCoroutine(EndColorAfter(duration)));
    }

    private IEnumerator PulseColor(Action pulse, float interval)
    {
        while (true) { pulse(); yield return GameSimulationClock.Wait(interval); }
    }

    private IEnumerator EndColorAfter(float duration)
    {
        yield return GameSimulationClock.Wait(duration);
        CancelColorChange();
    }

    public void BeginStunBlink() => StartColorEffect(RedColorBlink, nameof(RedColorBlink), 0.1f);
    public void EndStunBlink()
    {
        if (GameSimulationClock.IsNetwork) CancelColorChange();
        else Invoke(nameof(CancelColorChange), 0);
    }

    protected virtual void OnDisable()
    {
        SetConfirmedElement(null);
        if (networkBurnColor != null) StopCoroutine(networkBurnColor);
        networkBurnColor = null;
        if (confirmedFlash != null) StopCoroutine(confirmedFlash);
        confirmedFlash = null;
        if (sr != null && flashMaterialSaved) sr.material = originalMat;
        foreach (Coroutine routine in colorRoutines) if (routine != null) StopCoroutine(routine);
        colorRoutines.Clear();
        CancelInvoke();
    }
    protected SpriteRenderer sr;
    protected Player player;

    [Header("Pop Up Text FX")]
    [SerializeField] private GameObject popUpTextPrefab;

    [Header("Flash FX")]
    [SerializeField] private Material hitMat;
    private Material originalMat;
    private bool flashMaterialSaved;
    private Coroutine confirmedFlash;

    [Header("Aliment Colors")]
    [SerializeField] private Color[] chillColor;
    [SerializeField] private Color[] igniteColor;
    [SerializeField] private Color[] shockColor;

    [Header("Aliment particles")]
    [SerializeField] private ParticleSystem igniteFX;
    [SerializeField] private ParticleSystem shockFX;
    [SerializeField] private ParticleSystem chillFX;

    [Header("Hit FX")]
    [SerializeField] private GameObject hitFx;
    [SerializeField] private GameObject criticalHitFx;

    protected virtual void Start()
    {
        sr = GetComponentInChildren<SpriteRenderer>();
        player = GetComponent<Player>();
        if (player == null && PlayerManager.instance != null) player = PlayerManager.instance.player;
        if (!flashMaterialSaved && sr != null) { originalMat = sr.material; flashMaterialSaved = true; }
    }

    /// <summary>Data-only room feedback: no RNG, collision queries, gameplay animation events or damage calls.</summary>
    public void PlayConfirmedDamage(Rpg.Networking.Protocol.DamageResultDto result)
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        var impact = result.impact;
        Vector3 position = impact == null ? transform.position : new Vector3(impact.position[0], impact.position[1], transform.position.z);
        var pool = TemporaryEffectPool.ForScene(gameObject.scene);
        // Stable cosmetic variation from the result ID. Neither peer consumes combat RNG for display.
        float offset = (result.resultId % 7 - 3) * .12f;
        if (popUpTextPrefab != null && (result.evaded || result.actualDamage > 0 || impact != null))
            pool.Take(popUpTextPrefab, position + new Vector3(offset, 3.5f), Quaternion.identity)
                .GetComponent<PopUpTextFX>().Play(result.evaded ? "Miss" : result.actualDamage.ToString());
        if (impact == null) return;
        var prefab = result.critical ? criticalHitFx : hitFx;
        if (prefab != null)
        {
            Quaternion rotation = Quaternion.Euler(0, impact.facingDir < 0 ? 180 : 0, offset * 40);
            pool.Take(prefab, position, rotation).GetComponent<HitEffectFX>().Play(position, rotation);
        }
        if (sr == null) sr = GetComponentInChildren<SpriteRenderer>(true);
        if (sr == null || hitMat == null) return;
        if (!flashMaterialSaved) { originalMat = sr.material; flashMaterialSaved = true; }
        if (confirmedFlash != null) StopCoroutine(confirmedFlash);
        confirmedFlash = StartCoroutine(ConfirmedFlash());
    }

    private IEnumerator ConfirmedFlash()
    {
        sr.material = hitMat;
        yield return GameSimulationClock.Wait(.2f);
        if (sr != null) sr.material = originalMat;
        confirmedFlash = null;
    }

    public void CreatePopUpText(string _t)
    {
        float randomX = Random.Range(-1, 1);
        float randomY = Random.Range(3, 5);
        Vector3 posOffset = new Vector3(randomX, randomY, 0);
        var newText = TemporaryEffectPool.ForScene(gameObject.scene).Take(popUpTextPrefab, transform.position + posOffset, Quaternion.identity);
        newText.GetComponent<PopUpTextFX>().Play(_t);
    }

    public void MakeTransparent(bool _isTransparent)
    {
        if (_isTransparent) sr.color = Color.clear;
        else sr.color = Color.white;
    }

    private IEnumerator FlashFX()
    {
        sr.material = hitMat;
        Color currentColor = sr.color;
        sr.color = Color.white;
        yield return GameSimulationClock.Wait(.2f);

        sr.color = currentColor;
        sr.material = originalMat;
    }

    public void AttackedFlashFX() => StartCoroutine(FlashFX());

    private void RedColorBlink()
    {
        if (sr.color != Color.white) sr.color = Color.white;
        else sr.color = Color.red;
    }
    private void CancelColorChange()
    {
        foreach (Coroutine routine in colorRoutines) if (routine != null) StopCoroutine(routine);
        colorRoutines.Clear();
        CancelInvoke();
        sr.color = Color.white;

        igniteFX.Stop();
        chillFX.Stop();
        shockFX.Stop();
    }

    public void ChillFxFor(float _seconds)
    {
        chillFX.Play();
        StartColorEffect(ChillColorFx, nameof(ChillColorFx), 0.3f, _seconds);
    }

    private void ChillColorFx()
    {
        if (sr.color != chillColor[0])
            sr.color = chillColor[0];
        else sr.color = chillColor[1];
    }

    public void IgniteFxFor(float _seconds)
    {
        igniteFX.Play();
        StartColorEffect(IgniteColorFx, nameof(IgniteColorFx), 0.3f, _seconds);
    }

    private void IgniteColorFx()
    {
        if (sr.color != igniteColor[0])
            sr.color = igniteColor[0];
        else sr.color = igniteColor[1];
    }

    public void ShockFxFor(float _seconds)
    {
        shockFX.Play();
        StartColorEffect(ShockColorFx, nameof(ShockColorFx), 0.3f, _seconds);
    }

    private void ShockColorFx()
    {
        if (sr.color != shockColor[0])
            sr.color = shockColor[0];
        else sr.color = shockColor[1];
    }

    public void CreatedHitFx(Transform _t, bool _critical)
    {
        float zRotation = Random.Range(-90, 90);
        float xPos = Random.Range(-0.5f, 0.5f);
        float yPos = Random.Range(-0.5f, 0.5f);
        Vector3 hitFxRotation = new Vector3(0, 0, zRotation);

        GameObject hitPrefab = hitFx;
        if (_critical)
        {
            hitPrefab = criticalHitFx;
            float yRotation = 0;
            zRotation = Random.Range(-45, 45);
            if (GetComponent<Entity>().facingDir == -1)
                yRotation = 180;
            hitFxRotation = new Vector3(0, yRotation, zRotation);
        }

        Vector3 position = _t.position + new Vector3(xPos, yPos);
        Quaternion rotation = Quaternion.Euler(hitFxRotation);
        var instance = TemporaryEffectPool.ForScene(_t.gameObject.scene).Take(hitPrefab, position, rotation);
        instance.GetComponent<HitEffectFX>().Play(position, rotation);
    }
}
