using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cinemachine;
using TMPro;

public class EntityFX : MonoBehaviour
{
    protected SpriteRenderer sr;
    protected Player player;

    [Header("Pop Up Text FX")]
    [SerializeField] private GameObject popUpTextPrefab;

    [Header("Flash FX")]
    [SerializeField] private Material hitMat;
    private Material originalMat;

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
        player = PlayerManager.instance.player;
        originalMat = sr.material;
    }

    public void CreatePopUpText(string _t)
    {
        float randomX = Random.Range(-1, 1);
        float randomY = Random.Range(3, 5);
        Vector3 posOffset = new Vector3(randomX, randomY, 0);
        GameObject newText = Instantiate(popUpTextPrefab, transform.position + posOffset, Quaternion.identity);
        newText.GetComponent<TextMeshPro>().text = _t;
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
        yield return new WaitForSeconds(.2f);

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
        CancelInvoke();
        sr.color = Color.white;

        igniteFX.Stop();
        chillFX.Stop();
        shockFX.Stop();
    }

    public void ChillFxFor(float _seconds)
    {
        chillFX.Play();
        InvokeRepeating("ChillColorFx", 0, 0.3f);
        Invoke("CancelColorChange", _seconds);
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
        InvokeRepeating("IgniteColorFx", 0, 0.3f);
        Invoke("CancelColorChange", _seconds);
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
        InvokeRepeating("ShockColorFx", 0, 0.3f);
        Invoke("CancelColorChange", _seconds);
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

        GameObject newHitFx = Instantiate(hitPrefab, _t.position + new Vector3(xPos, yPos), Quaternion.identity);
        newHitFx.transform.Rotate(hitFxRotation);

        Destroy(newHitFx, 0.5f);
    }
}
