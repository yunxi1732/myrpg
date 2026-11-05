using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewDialogue", menuName = "RPG/Dialogue Asset")]
public class DialogueAsset : ScriptableObject
{
    [Tooltip("起始节点 ID，必须对应一个唯一节点（区分大小写）。")]
    public string startNodeId = "start";

    public List<DialogueNode> nodes = new List<DialogueNode>
    {
        new DialogueNode { id = "start", content = "你好，旅行者。" }
    };

    public DialogueNode GetNode(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || nodes == null) return null;
        foreach (DialogueNode node in nodes)
            if (node != null && node.id == id) return node;
        return null;
    }

    /// <summary>Checks configuration without modifying it. Empty jump IDs end dialogue; cycles are allowed.</summary>
    public List<string> GetValidationErrors()
    {
        var errors = new List<string>();
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        if (nodes == null || nodes.Count == 0)
            errors.Add("节点列表为空，请至少添加一个起始节点。");

        if (nodes != null)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                DialogueNode node = nodes[i];
                if (node == null)
                {
                    errors.Add($"节点[{i}]为空，请填写或删除该项。");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(node.id))
                    errors.Add($"节点[{i}]的 ID 为空或仅含空白。");
                else if (ids.TryGetValue(node.id, out int first))
                    errors.Add($"节点[{i}]的 ID '{node.id}' 与节点[{first}]重复，请使用唯一 ID。");
                else
                    ids.Add(node.id, i);
            }
        }

        if (string.IsNullOrWhiteSpace(startNodeId))
            errors.Add("起始节点 ID 为空，请填写 startNodeId。");
        else if (!ids.ContainsKey(startNodeId))
            errors.Add($"起始节点 '{startNodeId}' 不存在，请检查 startNodeId 或补齐节点。");

        if (nodes == null) return errors;
        for (int i = 0; i < nodes.Count; i++)
        {
            DialogueNode node = nodes[i];
            if (node == null) continue;
            string source = $"节点[{i}] '{node.id}'";
            CheckTarget(node.nextNodeId, source + "的下一节点", ids, errors);
            if (node.choices == null) continue;
            for (int j = 0; j < node.choices.Count; j++)
            {
                DialogueChoice choice = node.choices[j];
                if (choice == null)
                    errors.Add($"{source}的分支[{j}]为空，请填写或删除该项。");
                else
                    CheckTarget(choice.nextNodeId, $"{source}的分支[{j}] '{choice.text}'", ids, errors);
            }
        }
        return errors;
    }

    private static void CheckTarget(string target, string source,
        Dictionary<string, int> ids, List<string> errors)
    {
        // Preserve the original convention: null/empty means end, whitespace is not an end marker.
        if (!string.IsNullOrEmpty(target) && !ids.ContainsKey(target))
            errors.Add($"{source}跳转到不存在的节点 '{target}'，请修正 ID；结束对话请留空。");
    }

    [ContextMenu("检查对话配置")]
    private void CheckConfiguration()
    {
        List<string> errors = GetValidationErrors();
        if (errors.Count == 0) Debug.Log($"[DialogueAsset] '{name}' 配置检查通过。", this);
        else Debug.LogError($"[DialogueAsset] '{name}' 配置有误：\n" + string.Join("\n", errors), this);
    }
}

[Serializable]
public class DialogueNode
{
    [Tooltip("节点唯一 ID，区分大小写。")]
    public string id = "start";
    [TextArea(2, 6)] public string content;
    [Tooltip("无分支时的下一节点 ID；留空表示结束。")]
    public string nextNodeId;
    [Tooltip("有分支时必须选择选项，忽略普通下一节点。")]
    public List<DialogueChoice> choices = new List<DialogueChoice>();
}

[Serializable]
public class DialogueChoice
{
    public string text = "选项";
    [Tooltip("选中后的节点 ID；留空表示结束。")]
    public string nextNodeId;
}
