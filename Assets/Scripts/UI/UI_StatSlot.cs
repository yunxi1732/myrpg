using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Rpg.Networking;

public class UI_StatSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private UI ui;
    private bool networkBound;
    private RoomEntityPresentation networkEntity;
    private System.Action networkChanged;
    private int subscriptionVersion;
    public int BoundPlayerId => ValidNetworkStats() != null ? networkEntity.PlayerId : 0;

    [SerializeField] private string statName;
    [SerializeField] private StatType statType;
    [SerializeField] private TextMeshProUGUI statValueText;
    [SerializeField] private TextMeshProUGUI statNameText;

    [TextArea]
    [SerializeField] private string statDescription;

    private void OnValidate()
    {
        gameObject.name = "Stat - " + statName;
        if (statNameText != null)
            statNameText.text = statName;
    }

    private void Start()
    {
        ui = GetComponentInParent<UI>(true);
        UpdateStatValueUI();
    }

    public void BindNetwork(RoomEntityPresentation entity)
    {
        Detach(); networkBound = true;
        networkEntity = entity != null && entity.IsValid && entity.View != null && entity.PlayerId > 0 &&
            entity.View.GetComponent<NetworkEntityIdentity>()?.IsLocalInputOwner == true ? entity : null;
        if (isActiveAndEnabled) Subscribe();
        UpdateStatValueUI();
    }
    private PlayerStats ValidNetworkStats()
    {
        var entity = networkEntity;
        if (entity == null || !entity.IsValid || !entity.IsReady || entity.View == null || !entity.HasHealth) return null;
        var identity = entity.View.GetComponent<NetworkEntityIdentity>();
        var player = entity.View.GetComponent<Player>();
        var stats = entity.View.GetComponent<PlayerStats>();
        return identity != null && identity.IsBound && identity.IsLocalInputOwner && identity.EntityId == entity.EntityId &&
            identity.PlayerId == entity.PlayerId && identity.RoomSessionId == entity.RoomSessionId && player != null &&
            player.IsRuntimeInitialized && stats != null && stats.IsRuntimeInitialized && player.Combat.RoomConfigurationId != null ? stats : null;
    }
    private void Subscribe()
    {
        if (networkEntity == null) return;
        var expected = networkEntity; int version = subscriptionVersion;
        networkChanged = () => { if (version == subscriptionVersion && ReferenceEquals(networkEntity, expected)) UpdateStatValueUI(); };
        expected.Changed += networkChanged;
    }
    private void Detach()
    {
        subscriptionVersion++;
        if (networkEntity != null && networkChanged != null) networkEntity.Changed -= networkChanged;
        networkChanged = null;
    }
    private void OnEnable()
    {
        Detach();
        if (networkBound) Subscribe();
        UpdateStatValueUI();
    }
    private void OnDisable() { Detach(); HideTooltip(); }
    private void OnDestroy() { Detach(); networkEntity = null; }
    private void LateUpdate() { if (networkBound) UpdateStatValueUI(); } // Read only; includes current A attributes and B's last confirmed values.

    public void UpdateStatValueUI()
    {
        if (statValueText == null) return;
        bool room = networkBound || EntityRuntimePermissions.NetworkActive;
        PlayerStats playerStats = room ? ValidNetworkStats() :
            PlayerManager.instance != null && PlayerManager.instance.player != null ? PlayerManager.instance.player.GetComponent<PlayerStats>() : null;
        if (playerStats == null)
        {
            statValueText.text = "—";
            if (networkEntity != null && !networkEntity.IsValid) { Detach(); networkEntity = null; }
            return;
        }

        if (playerStats != null)
        {
            statValueText.text = playerStats.GetStat(statType).GetValue().ToString();

            if (statType == StatType.health)
                statValueText.text = playerStats.GetMaxHealthValue().ToString();

            if (statType == StatType.damage)
                statValueText.text = (playerStats.damage.GetValue() + playerStats.strength.GetValue()).ToString();

            if (statType == StatType.critPower)
                statValueText.text = (playerStats.critPower.GetValue() + playerStats.strength.GetValue()).ToString();

            if (statType == StatType.critChance)
                statValueText.text = (playerStats.critChance.GetValue() + playerStats.agility.GetValue()).ToString();

            if (statType == StatType.evasion)
                statValueText.text = (playerStats.evasion.GetValue() + playerStats.agility.GetValue()).ToString();

            if (statType == StatType.magicRes)
                statValueText.text = (playerStats.magicResistance.GetValue() + (playerStats.intelligence.GetValue() * 3)).ToString();
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if ((networkBound || EntityRuntimePermissions.NetworkActive) && ValidNetworkStats() == null) return;
        if (ui == null) ui = GetComponentInParent<UI>(true);
        if (ui != null && ui.statTooltip != null) ui.statTooltip.ShowStatTooltip(statDescription);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        HideTooltip();
    }
    private void HideTooltip() { if (ui != null && ui.statTooltip != null) ui.statTooltip.HideStatTooltip(); }
}
