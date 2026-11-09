using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Rpg.Networking;

public class UI_InGame : MonoBehaviour
{
    [SerializeField] private PlayerStats playerStats;
    [SerializeField] private Slider slider;

    [SerializeField] private Image dashImage;
    [SerializeField] private Image parryImage;
    [SerializeField] private Image crystalImage;
    [SerializeField] private Image swordImage;
    [SerializeField] private Image blackholeImage;
    [SerializeField] private Image flaskImage;
    [SerializeField] private GameObject flaskUI;
    [SerializeField] private Image flaskIcon;

    private SkillManager skills;

    [Header("Souls info")]
    [SerializeField] private TextMeshProUGUI currentSouls;
    [SerializeField] private float soulsAmount;
    [SerializeField] private float increaseRate = 500;
    private RoomEntityPresentation networkEntity;
    private CanvasGroup networkVisibility;
    private bool legacyStarted;
    private bool networkBound;
    public int BoundPlayerId => networkEntity != null && networkEntity.IsValid ? networkEntity.PlayerId : 0;
    private bool NetworkMode => RoomSceneCoordinator.ProtectSaves || networkBound;


    private void Start()
    {
        if (NetworkMode) { RenderNetwork(); return; }
        legacyStarted = true;
        if (playerStats != null)
            playerStats.OnHealthChanged += UpdateHealthUI;

        skills = SkillManager.instance;
    }

    private void Update()
    {
        if (NetworkMode) return;
        UpdateSoulsUI();

        if (!GameplayControlLock.IsActionInputBlocked && Input.GetKeyDown(KeyCode.LeftShift) && skills.dash.dashUnlocked)
            SetCooldownOf(dashImage);

        if (!GameplayControlLock.IsActionInputBlocked && Input.GetKeyDown(KeyCode.Q) && skills.parry.parryUnlocked)
            SetCooldownOf(parryImage);

        if (!GameplayControlLock.IsActionInputBlocked && Input.GetKeyDown(KeyCode.F) && skills.crystal.crystalUnlocked)
            SetCooldownOf(crystalImage);

        if (!GameplayControlLock.IsActionInputBlocked && Input.GetKeyDown(KeyCode.Mouse1) && skills.sword.swordUnlocked)
            SetCooldownOf(swordImage);

        if (!GameplayControlLock.IsActionInputBlocked && Input.GetKeyDown(KeyCode.R) && skills.blackhole.blackholeUnlocked)
            SetCooldownOf(blackholeImage);

        CheckCooldownOf(dashImage, skills.dash.cooldown);
        CheckCooldownOf(parryImage, skills.parry.cooldown);
        CheckCooldownOf(crystalImage, skills.crystal.cooldown);
        CheckCooldownOf(swordImage, skills.sword.cooldown);
        CheckCooldownOf(blackholeImage, skills.blackhole.cooldown);
    }

    private void OnEnable()
    {
        if (NetworkMode)
        {
            if (networkEntity != null) networkEntity.Changed += RenderNetwork;
            RenderNetwork();
        }
        else
        {
            if (legacyStarted && playerStats != null) playerStats.OnHealthChanged += UpdateHealthUI;
            RefreshFlaskUI();
        }
    }

    // Runs after Player.Update has processed the only Alpha1 input, so successful use is visible this frame.
    private void LateUpdate() { if (!NetworkMode) RefreshFlaskUI(); }

    public void BindNetwork(RoomEntityPresentation entity)
    {
        Detach();
        networkBound = true;
        networkEntity = entity;
        if (isActiveAndEnabled && entity != null) entity.Changed += RenderNetwork;
        RenderNetwork();
    }

    public void UnbindNetwork()
    {
        Detach(); networkEntity = null;
        HideNetwork(false);
    }

    private void HideNetwork(bool createIfMissing = true)
    {
        // Unity's missing/destroyed component wrappers require its overloaded null check, not ??.
        if (networkVisibility == null) networkVisibility = GetComponent<CanvasGroup>();
        if (networkVisibility == null && createIfMissing) networkVisibility = gameObject.AddComponent<CanvasGroup>();
        if (networkVisibility == null) return;
        networkVisibility.alpha = 0;
        networkVisibility.interactable = networkVisibility.blocksRaycasts = false;
    }

    private void RenderNetwork()
    {
        HideNetwork();
        if (networkEntity != null && !networkEntity.IsValid) { Detach(); networkEntity = null; }
        if (networkEntity == null || !networkEntity.IsValid || !networkEntity.IsReady || networkEntity.View == null || !networkEntity.HasHealth || !networkEntity.HasHud) return;
        networkVisibility.alpha = 1;
        if (slider != null) { slider.maxValue = networkEntity.MaxHealth; slider.value = networkEntity.Health; }
        if (currentSouls != null) currentSouls.text = networkEntity.Currency.ToString();
        Image[] cooldowns = { dashImage, parryImage, crystalImage, swordImage, blackholeImage };
        for (int i = 0; i < cooldowns.Length; ++i) if (cooldowns[i] != null) cooldowns[i].fillAmount = networkEntity.SkillFill(i);
        bool flaskVisible = networkEntity.FlaskIcon != null;
        if (flaskUI != null) flaskUI.SetActive(flaskVisible);
        if (flaskVisible)
        {
            if (flaskIcon != null) flaskIcon.sprite = networkEntity.FlaskIcon;
            if (flaskImage != null) { flaskImage.sprite = networkEntity.FlaskIcon; flaskImage.fillAmount = networkEntity.FlaskFill; }
        }
    }

    private void Detach()
    {
        if (networkEntity != null) networkEntity.Changed -= RenderNetwork;
        if (playerStats != null) playerStats.OnHealthChanged -= UpdateHealthUI;
    }

    private void OnDisable() => Detach();
    private void OnDestroy() => Detach();

    private void RefreshFlaskUI()
    {
        Inventory inventory = Inventory.instance;
        ItemData_Equipment flask = inventory != null && inventory.IsInitialized ? inventory.GetEquipment(EquipmentType.Flask) : null;
        bool visible = flask != null;
        if (flaskUI != null && flaskUI.activeSelf != visible) flaskUI.SetActive(visible);
        if (!visible)
        {
            if (flaskImage != null) flaskImage.fillAmount = 0;
            return;
        }
        if (flaskIcon != null && flaskIcon.sprite != flask.itemIcon) flaskIcon.sprite = flask.itemIcon;
        if (flaskImage != null)
        {
            if (flaskImage.sprite != flask.itemIcon) flaskImage.sprite = flask.itemIcon;
            flaskImage.fillAmount = inventory.flaskCooldown > 0 ?
                Mathf.Clamp01(inventory.FlaskCooldownRemaining / inventory.flaskCooldown) : 0;
        }
    }

    private void UpdateSoulsUI()
    {
        if (soulsAmount < PlayerManager.instance.GetCurrency())
            soulsAmount += GameSimulationClock.DeltaTime * increaseRate;
        else
            soulsAmount = PlayerManager.instance.GetCurrency();

        currentSouls.text = ((int)soulsAmount).ToString();
    }

    private void UpdateHealthUI()
    {
        slider.maxValue = playerStats.GetMaxHealthValue();
        slider.value = playerStats.currentHealth;
    }

    private void SetCooldownOf(Image _image)
    {
        if (_image.fillAmount <= 0)
            _image.fillAmount = 1;
    }

    private void CheckCooldownOf(Image _image, float _cooldown)
    {
        if (_image.fillAmount > 0)
            _image.fillAmount -= 1 / _cooldown * GameSimulationClock.DeltaTime;
    }
}
