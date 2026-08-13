using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AfterImageFx : MonoBehaviour
{
    private SpriteRenderer sr;
    private float colorLoseRate;

    public void SetupAfterImage(float _speed, Sprite _iamge)
    {
        sr = GetComponent<SpriteRenderer>();
        sr.sprite = _iamge;
        colorLoseRate = _speed;
    }

    private void Update()
    {
        float alpha = sr.color.a - colorLoseRate * Time.deltaTime;
        sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, alpha);
        if (sr.color.a <= 0)
            Destroy(gameObject);
    }
}
