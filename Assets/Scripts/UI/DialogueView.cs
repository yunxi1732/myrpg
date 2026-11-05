using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Dialogue text and generated choices. The hidden root can be started explicitly.</summary>
public sealed class DialogueView : MonoBehaviour
{
    [SerializeField] private Text npcName;
    [SerializeField] private Text npcProfession;
    [SerializeField] private Text dialogContent;
    [SerializeField] private Button clickArea;
    [SerializeField] private Button closeButton;
    [SerializeField] private GameObject dialogOptions;
    [SerializeField] private DialogueOptionView optionPrefab;
    [SerializeField] private DialogueAsset testDialogue;
    private DialogueRunner runner;
    private GameManager pauseManager;
    private readonly List<DialogueOptionView> options = new List<DialogueOptionView>();

    /// <summary>Current binding; null after end, close, disable or destruction.</summary>
    public DialogueRunner Runner => runner;
    public NpcInteractable CurrentNpc { get; private set; }
    public bool IsDialogueRunning => runner != null && runner.IsRunning;

    private void Awake()
    {
        // Scene designers may leave the panel visible; only a live session should show its mask.
        if (runner == null || !runner.IsRunning) Hide();
    }

    /// <summary>Adopt one session. Rebinding ends the old session; binding the same runner is idempotent.</summary>
    public void Bind(DialogueRunner value)
    {
        if (value != null && ReferenceEquals(runner, value)) return;
        Close();
        if (value == null || !enabled) return;
        runner = value;
        runner.OnNodeChanged += Refresh;
        runner.OnEnded += Close;
        if (clickArea != null) clickArea.onClick.AddListener(Advance);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        // An idle runner can be bound while the root is hidden, then started explicitly.
        if (runner.IsRunning) Refresh();
    }

    public void StartDialogue(DialogueAsset dialogue, string name = "", string profession = "")
    {
        if (!IsDialogueRunning) StartSession(dialogue, name, profession, null);
    }

    public bool TryStartNpcDialogue(NpcInteractable npc)
    {
        if (npc == null || !npc.isActiveAndEnabled || !npc.IsPlayerInRange || !enabled || IsDialogueRunning ||
            (transform.parent != null && !transform.parent.gameObject.activeInHierarchy)) return false;
        StartSession(npc.dialogue, npc.npcName, npc.npcProfession, npc);
        return IsDialogueRunning && CurrentNpc == npc;
    }

    private void StartSession(DialogueAsset dialogue, string name, string profession, NpcInteractable source)
    {
        if (npcName == null || dialogContent == null || clickArea == null)
        {
            Debug.LogError("[DialogueView] 请绑定 NpcName、DialogContent 和 ClickArea Button。", this);
            return;
        }
        if (!enabled) return;
        DialogueRunner session = runner ?? new DialogueRunner();
        // End the previous session before subscribing to the replacement's Start notifications.
        Close();
        Bind(session);
        CurrentNpc = source;
        session.Start(dialogue, name, profession);
        if (!session.IsRunning) Close();
    }

    private void Advance() => runner?.TryAdvance();

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    private void Refresh()
    {
        ClearOptions();
        if (runner == null || !runner.IsRunning || runner.Current == null)
        {
            Close();
            return;
        }
        if (npcName != null) npcName.text = runner.NpcName;
        if (npcProfession != null) npcProfession.text = runner.NpcProfession;
        if (dialogContent != null) dialogContent.text = runner.Current.content;
        if (runner.HasChoices)
        {
            if (dialogOptions == null || optionPrefab == null)
                Debug.LogError("[DialogueView] 请绑定 DialogOptions 和 DialogOption 预制体。", this);
            else
            {
                for (int i = 0; i < runner.Current.choices.Count; i++)
                {
                    DialogueChoice choice = runner.Current.choices[i];
                    if (choice == null) continue;
                    DialogueOptionView option = Instantiate(optionPrefab, dialogOptions.transform, false);
                    option.Bind(i, choice.text, Choose);
                    options.Add(option);
                }
            }
        }
        if (dialogOptions != null) dialogOptions.SetActive(runner.HasChoices);
        if (clickArea != null) clickArea.interactable = !runner.HasChoices;
        gameObject.SetActive(true);
        if (Application.isPlaying && isActiveAndEnabled && pauseManager == null && GameManager.instance != null)
        {
            pauseManager = GameManager.instance;
            pauseManager.SetPauseOwner(this, true);
        }
    }

    public void Close()
    {
        try { ReleaseSession(); }
        finally { Hide(); }
    }
    private void Choose(int index) => runner?.Choose(index);

    private void ClearOptions()
    {
        foreach (DialogueOptionView option in options)
        {
            if (option == null) continue;
            option.Clear();
            // Remove from layout immediately, before Unity's deferred destruction at frame end.
            option.gameObject.SetActive(false);
            option.transform.SetParent(null, false);
            if (Application.isPlaying) Destroy(option.gameObject);
            else DestroyImmediate(option.gameObject);
        }
        options.Clear();
    }

    private void Hide()
    {
        ClearOptions();
        if (dialogOptions != null) dialogOptions.SetActive(false);
        if (npcName != null) npcName.text = "";
        if (npcProfession != null) npcProfession.text = "";
        if (dialogContent != null) dialogContent.text = "";
        gameObject.SetActive(false);
    }
    private void OnDisable() => Close();

    // Detach before End so its callbacks cannot re-enter this view or retain it.
    private void ReleaseSession()
    {
        DialogueRunner previous = runner;
        runner = null;
        CurrentNpc = null;
        if (previous != null)
        {
            previous.OnNodeChanged -= Refresh;
            previous.OnEnded -= Close;
        }
        if (clickArea != null) clickArea.onClick.RemoveListener(Advance);
        if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        ClearOptions();
        if (pauseManager != null) pauseManager.SetPauseOwner(this, false);
        pauseManager = null;
        previous?.End();
    }

    private void OnDestroy() => ReleaseSession();

    [ContextMenu("验证/开始测试对话（Play模式）")]
    private void StartTestDialogue()
    {
        if (Application.isPlaying) StartDialogue(testDialogue, "引路人", "村民");
        else Debug.LogWarning("[DialogueView] 请先进入 Play 模式。", this);
    }
}
