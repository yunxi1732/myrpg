using System;
using UnityEngine;

/// <summary>Owns a talk candidate while the local player is in range.</summary>
public sealed class NpcInteractable : MonoBehaviour
{
    [Header("NPC 身份与对话")]
    [Tooltip("稳定的 NPC 标识；不同 NPC 使用不同值，不作为显示名字。")]
    public string npcId;
    [Tooltip("显示在对话框中的名字。")]
    public string npcName = "测试引路人";
    [Tooltip("可选职业，允许留空。")]
    public string npcProfession = "";
    [Tooltip("此 NPC 使用的对话配置资源。")]
    public DialogueAsset dialogue;

    [Tooltip("独立范围子节点上的 2D Trigger；不要将范围碰撞器挂到显示节点。")]
    public Collider2D interactionRange;

    [Tooltip("本场景玩家根对象上的 Player 组件。")]
    public Player interactionPlayer;
    public LayerMask playerLayers = 1 << 6;
    public bool IsPlayerInRange { get; private set; }
    public event Action<bool> OnPlayerRangeChanged;
    [SerializeField] private UI interactionUi;
    [SerializeField] private DialogueView dialogueView;
    [Header("交互提示")]
    public string actionDisplayName = "对话";
    public string keyTip = "E";
    public KeyCode key = KeyCode.E;
    private InteractionManager registeredManager;

    public bool IsPlayerCollider(Collider2D other)
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanUseLegacyActions(interactionPlayer)) return false;
        if (!isActiveAndEnabled || interactionPlayer == null || !interactionPlayer.isActiveAndEnabled ||
            other == null || !other.enabled || !other.gameObject.activeInHierarchy || other.isTrigger ||
            (playerLayers.value & (1 << other.gameObject.layer)) == 0) return false;

        Rigidbody2D body = other.attachedRigidbody;
        return body != null && body.simulated && body.GetComponent<Player>() == interactionPlayer &&
            other.GetComponentInParent<Player>() == interactionPlayer;
    }

    internal void SetPlayerInRange(bool inside)
    {
        if (IsPlayerInRange == inside) return;
        IsPlayerInRange = inside;
        if (inside) RegisterTalkOption();
        else
        {
            RemoveTalkOption();
            CloseOwnedDialogue();
        }
        if (IsPlayerInRange == inside) OnPlayerRangeChanged?.Invoke(inside);
    }

    private void RegisterTalkOption()
    {
        if (!isActiveAndEnabled || registeredManager != null) return;
        if (interactionUi == null || dialogueView == null)
        {
            Debug.LogWarning("[NpcInteractable] 请绑定场景 UI 和 DialogueView。", this);
            return;
        }
        if (interactionUi.IsShuttingDown || interactionUi.Interactions.IsDisposed) return;
        registeredManager = interactionUi.Interactions;
        registeredManager.OnDisposed += OnInteractionManagerDisposed;
        registeredManager.Register(new InteractionOptionData(this, "talk", actionDisplayName,
            ExecuteTalk, keyTip, key));
    }

    private void ExecuteTalk() => TryStartTalk();

    public bool TryStartTalk()
    {
        if (!Rpg.Networking.EntityRuntimePermissions.CanUseLegacyActions(interactionPlayer)) return false;
        if (this == null || !isActiveAndEnabled || !IsPlayerInRange || dialogueView == null ||
            dialogueView.IsDialogueRunning || (Application.isPlaying && Time.timeScale <= 0)) return false;
        return dialogueView.TryStartNpcDialogue(this);
    }

    private void RemoveTalkOption()
    {
        InteractionManager previous = registeredManager;
        registeredManager = null;
        if (previous != null) previous.OnDisposed -= OnInteractionManagerDisposed;
        previous?.UnregisterOwner(this);
    }

    private void OnInteractionManagerDisposed()
    {
        if (registeredManager != null) registeredManager.OnDisposed -= OnInteractionManagerDisposed;
        registeredManager = null;
        ClearRange();
    }

    private void CloseOwnedDialogue()
    {
        if (dialogueView != null && dialogueView.CurrentNpc == this) dialogueView.Close();
    }

    private void ClearRange()
    {
        if (interactionRange != null)
            interactionRange.GetComponent<NpcInteractionRange>()?.ClearContacts();
        SetPlayerInRange(false);
        RemoveTalkOption();
        CloseOwnedDialogue();
    }

    private void OnDisable() => ClearRange();
    private void OnDestroy()
    {
        try { ClearRange(); }
        finally { OnPlayerRangeChanged = null; }
    }
}
