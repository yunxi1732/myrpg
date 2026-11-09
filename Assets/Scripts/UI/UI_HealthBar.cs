using Rpg.Networking;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(9500)] // Correct network bar orientation after RemotePlayerView flips its parent (9000).
public class UI_HealthBar : MonoBehaviour
{
    private Entity entity;
    private Slider slider;
    private CharaterStats myStats;
    private CanvasGroup visibility;
    private RoomEntityPresentation networkEntity;
    private bool started;
    private bool networkBound;
    public int BoundEntityId => networkEntity != null && networkEntity.IsValid ? networkEntity.EntityId : 0;

    private void Cache()
    {
        if (slider == null) slider = GetComponentInChildren<Slider>(true);
        if (visibility == null) visibility = GetComponent<CanvasGroup>();
        if (visibility == null) visibility = gameObject.AddComponent<CanvasGroup>();
        visibility.interactable = visibility.blocksRaycasts = false;
    }

    private void Start() { started = true; Rebind(); }
    private void OnEnable()
    {
        Cache();
        if (started || networkEntity != null) Rebind();
        else visibility.alpha = 0;
    }

    public void BindNetwork(RoomEntityPresentation value)
    {
        Detach(); networkBound = true; networkEntity = value;
        if (value == null)
        {
            // Cleanup must not add components to an object that is being destroyed.
            if (visibility == null) visibility = GetComponent<CanvasGroup>();
            if (visibility != null) { visibility.alpha = 0; visibility.interactable = visibility.blocksRaycasts = false; }
            return;
        }
        Cache();
        if (isActiveAndEnabled && value != null) value.Changed += UpdateHealthUI;
        UpdateHealthUI();
    }

    private void Rebind()
    {
        Detach(); Cache();
        if (networkEntity != null) networkEntity.Changed += UpdateHealthUI;
        else if (!networkBound && !RoomSceneCoordinator.ProtectSaves)
        {
            entity = GetComponentInParent<Entity>();
            myStats = GetComponentInParent<CharaterStats>();
            if (entity != null) entity.OnFlipped += FlipUI;
            if (myStats != null) myStats.OnHealthChanged += UpdateHealthUI;
        }
        UpdateHealthUI();
    }

    private void UpdateHealthUI()
    {
        if (networkEntity != null && !networkEntity.IsValid) { Detach(); networkEntity = null; }
        bool ready = networkEntity != null ? networkEntity.IsValid && networkEntity.IsReady && networkEntity.View != null && networkEntity.HasHealth :
            !networkBound && !RoomSceneCoordinator.ProtectSaves && myStats != null;
        if (visibility == null) return;
        visibility.alpha = ready ? 1 : 0;
        if (!ready || slider == null) return;
        slider.maxValue = networkEntity != null ? networkEntity.MaxHealth : myStats.GetMaxHealthValue();
        slider.value = networkEntity != null ? networkEntity.Health : myStats.currentHealth;
    }

    private void LateUpdate()
    {
        if (networkEntity != null) transform.rotation = Quaternion.identity;
    }
    private void FlipUI() => transform.Rotate(0, 180, 0);
    private void Detach()
    {
        if (networkEntity != null) networkEntity.Changed -= UpdateHealthUI;
        if (entity != null) entity.OnFlipped -= FlipUI;
        if (myStats != null) myStats.OnHealthChanged -= UpdateHealthUI;
        entity = null; myStats = null;
    }
    private void OnDisable() => Detach();
    private void OnDestroy() => Detach();
}
