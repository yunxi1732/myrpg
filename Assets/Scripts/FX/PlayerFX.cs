using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerFX : EntityFX
{
    [Header("Screen Shake FX")]
    private CinemachineImpulseSource screenShake;
    [SerializeField] private float shakeMultiplier;
    public Vector3 shakeSwordImpact;
    public Vector3 shakeHighDamage;

    [Header("After Image FX")]
    [SerializeField] private GameObject afterImagePrefab;
    [SerializeField] private float colorLoseRate;
    [SerializeField] private float afterImageCooldown;
    private float afterImageTimer;

    [Space]
    [SerializeField] private ParticleSystem dustFx;

    protected override void Start()
    {
        base.Start();
        screenShake = GetComponent<CinemachineImpulseSource>();
    }

    private void Update()
    {
        afterImageTimer -= GameSimulationClock.DeltaTime;
    }

    public void ScreenShake(Vector3 _p)
    {
        screenShake.m_DefaultVelocity = new Vector3(_p.x * player.facingDir, _p.y) * shakeMultiplier;
        screenShake.GenerateImpulse();
    }

    public void CreateAfterImage()
    {
        if (afterImageTimer < 0)
        {
            afterImageTimer = afterImageCooldown;
            var newAfterImage = TemporaryEffectPool.ForScene(gameObject.scene).Take(afterImagePrefab, transform.position, transform.rotation);
            newAfterImage.GetComponent<AfterImageFx>().Play(colorLoseRate, sr.sprite, transform.position, transform.rotation);
        }
    }

    public void PlayDustFx()
    {
        if (dustFx != null)
            dustFx.Play();
    }
}
