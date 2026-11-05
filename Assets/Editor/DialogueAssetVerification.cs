using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class DialogueAssetVerification
{
    [MenuItem("Tools/RPG/Verify Dialogue Asset")]
    public static void Verify()
    {
        var asset = ScriptableObject.CreateInstance<DialogueAsset>();
        try
        {
            Require(asset.GetValidationErrors().Count == 0, "New asset is a valid one-line dialogue");
            asset.nodes[0].nextNodeId = "question";
            asset.nodes.Add(new DialogueNode
            {
                id = "question", content = "想了解村子吗？",
                choices = new List<DialogueChoice>
                {
                    new DialogueChoice { text = "想", nextNodeId = "answer" },
                    new DialogueChoice { text = "再见", nextNodeId = "" }
                }
            });
            asset.nodes.Add(new DialogueNode { id = "answer", content = "村子就在前方。" });
            Require(asset.GetValidationErrors().Count == 0, "Sequential and branching dialogue is valid");
            Require(asset.GetNode("answer") == asset.nodes[2] && asset.GetNode("missing") == null,
                "Node lookup preserves original references");
            asset.nodes.Add(new DialogueNode { id = "start" });
            Require(HasError(asset, "重复"), "Duplicate ID is reported");
            asset.nodes.RemoveAt(3);
            asset.startNodeId = "missing";
            Require(HasError(asset, "起始节点 'missing' 不存在"), "Missing start is reported");
            asset.startNodeId = " ";
            Require(HasError(asset, "起始节点 ID 为空"), "Blank start is reported");
            asset.startNodeId = "start";
            asset.nodes[0].nextNodeId = "bad-next";
            asset.nodes[1].choices[0].nextNodeId = "bad-choice";
            Require(HasError(asset, "bad-next") && HasError(asset, "bad-choice"),
                "Both sequential and branch targets are reported");
            asset.nodes[0].nextNodeId = "question";
            asset.nodes[1].choices[0].nextNodeId = "answer";
            asset.nodes[2].nextNodeId = "start";
            Require(asset.GetValidationErrors().Count == 0, "Cycles and empty branch end are allowed");
            asset.nodes.Add(null);
            asset.nodes.Add(new DialogueNode { id = " " });
            asset.nodes[1].choices.Add(null);
            Require(HasError(asset, "节点[3]为空") && HasError(asset, "ID 为空或仅含空白") &&
                HasError(asset, "分支[2]为空"), "Malformed list entries are reported");
            asset.nodes = null;
            Require(HasError(asset, "节点列表为空"), "Null node list is safe");
            Debug.Log("[DialogueAsset] PASS: 10 checks");
        }
        finally { UnityEngine.Object.DestroyImmediate(asset); }
    }

    private static bool HasError(DialogueAsset asset, string text)
    {
        return asset.GetValidationErrors().Exists(error => error.Contains(text));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[DialogueAsset] FAIL: " + message);
    }
}
