using UnityEngine;
using UnityEngine.UI;

public class HealthBarUI : MonoBehaviour
{
    private Entity entity;
    private RectTransform myTransform;
    private Slider slider;
    private CharaterStats myStats;

    private void Start()
    {
        entity = GetComponentInParent<Entity>();
        myTransform = GetComponent<RectTransform>();
        slider = GetComponentInChildren<Slider>();
        myStats = GetComponentInParent<CharaterStats>();
        entity.OnFlipped += FlipUI;
        myStats.OnHealthChanged += UpdateHealthUI;

        UpdateHealthUI();
    }

   private void UpdateHealthUI()
    {
        slider.maxValue = myStats.maxHealth.GetValue();
        slider.value = myStats.currentHealth;
    }

    private void FlipUI() => myTransform.Rotate(0, 180, 0);

    private void OnDisable()
    {
        entity.OnFlipped -= FlipUI;
        myStats.OnHealthChanged -= UpdateHealthUI;
    }
}
