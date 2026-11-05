using System;
using UnityEngine;

/// <summary>Dialogue state only; no UI, input or pause ownership.</summary>
public sealed class DialogueRunner
{
    private DialogueAsset asset;
    public bool IsRunning { get; private set; }
    public DialogueNode Current { get; private set; }
    public bool HasChoices => Current != null && Current.choices != null && Current.choices.Count > 0;
    public string NpcName { get; private set; }
    public string NpcProfession { get; private set; }

    public event Action OnNodeChanged;
    public event Action OnEnded;

    public void Start(DialogueAsset dialogue, string npcName = "", string npcProfession = "")
    {
        End();
        if (dialogue == null)
        {
            Debug.LogWarning("[DialogueRunner] 对话资源为空，未开始对话。");
            return;
        }
        var errors = dialogue.GetValidationErrors();
        if (errors.Count > 0)
        {
            Debug.LogWarning($"[DialogueRunner] '{dialogue.name}' 配置无效，未开始对话：\n" +
                string.Join("\n", errors), dialogue);
            return;
        }

        asset = dialogue;
        NpcName = npcName;
        NpcProfession = npcProfession;
        IsRunning = true;
        ShowNode(dialogue.startNodeId);
    }

    public void TryAdvance()
    {
        if (!IsRunning) return;
        // Configuration can be edited or its resource destroyed during a session.
        if (asset == null || Current == null)
        {
            End();
            return;
        }
        // A choice must be selected explicitly; nextNodeId cannot bypass it.
        if (HasChoices) return;
        if (string.IsNullOrEmpty(Current.nextNodeId)) End();
        else ShowNode(Current.nextNodeId);
    }

    /// <summary>Select a zero-based choice. Invalid indices do not change state or publish events.</summary>
    public void Choose(int index)
    {
        if (!IsRunning) return;
        if (asset == null || Current == null)
        {
            End();
            return;
        }
        if (!HasChoices || index < 0 || index >= Current.choices.Count) return;

        DialogueChoice choice = Current.choices[index];
        if (choice == null)
        {
            Debug.LogWarning($"[DialogueRunner] 节点 '{Current.id}' 的分支[{index}]为空，安全结束对话。", asset);
            End();
            return;
        }
        if (string.IsNullOrEmpty(choice.nextNodeId)) End();
        else ShowNode(choice.nextNodeId);
    }

    private void ShowNode(string id)
    {
        DialogueNode node = asset == null ? null : asset.GetNode(id);
        if (node == null)
        {
            Debug.LogWarning($"[DialogueRunner] 节点 '{id}' 不存在，安全结束对话。", asset);
            End();
            return;
        }
        Current = node;
        OnNodeChanged?.Invoke();
    }

    public void End()
    {
        if (!IsRunning) return;
        IsRunning = false;
        Current = null;
        asset = null;
        NpcName = null;
        NpcProfession = null;
        OnEnded?.Invoke();
    }
}
