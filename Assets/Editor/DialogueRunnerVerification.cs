using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Temporary log-only driver: creates transient data, never edits scene or saved assets.</summary>
public static class DialogueRunnerVerification
{
    [MenuItem("Tools/RPG/Verify Branching Dialogue Runner")]
    public static void VerifyBranches()
    {
        var dialogue = ScriptableObject.CreateInstance<DialogueAsset>();
        dialogue.name = "TemporaryBranchingDialogue";
        dialogue.nodes = new List<DialogueNode>
        {
            new DialogueNode
            {
                id = "start", content = "走哪条路？", nextNodeId = "left",
                choices = new List<DialogueChoice>
                {
                    new DialogueChoice { text = "左边", nextNodeId = "left" },
                    new DialogueChoice { text = "右边", nextNodeId = "right" }
                }
            },
            new DialogueNode { id = "left", content = "你来到了森林。" },
            new DialogueNode { id = "right", content = "你来到了村庄。" }
        };
        var runner = new DialogueRunner();
        var trace = new List<string>();
        Action changed = () =>
        {
            trace.Add(runner.Current.id);
            Debug.Log($"[Dialogue分支验证] {runner.Current.id}：{runner.Current.content}");
        };
        Action ended = () =>
        {
            Require(!runner.IsRunning && runner.Current == null, "End event sees cleared state");
            trace.Add("end");
            Debug.Log("[Dialogue分支验证] 结束");
        };
        runner.OnNodeChanged += changed;
        runner.OnEnded += ended;
        try
        {
            for (int index = 0; index < 2; index++)
            {
                trace.Clear();
                Debug.Log($"[Dialogue分支验证] 开始，选择索引 {index}");
                runner.Start(dialogue);
                runner.TryAdvance();
                Require(runner.Current == dialogue.nodes[0] && runner.IsRunning && trace.Count == 1,
                    "Ordinary next cannot skip choices even with nextNodeId set");
                runner.Choose(-1);
                runner.Choose(2);
                runner.Choose(int.MaxValue);
                Require(runner.Current == dialogue.nodes[0] && runner.IsRunning && trace.Count == 1,
                    "Out-of-range choices preserve state and emit no events");
                runner.Choose(index);
                string target = index == 0 ? "left" : "right";
                Require(runner.IsRunning && runner.Current.id == target &&
                    string.Join(",", trace) == "start," + target, "Each choice reaches its own node once");
                runner.Choose(0);
                Require(trace.Count == 2 && runner.Current.id == target, "Choose on a linear node is ignored");
                runner.TryAdvance();
                Require(string.Join(",", trace) == "start," + target + ",end", "Leaf ends once");
                runner.Choose(0);
                runner.TryAdvance();
                Require(trace.Count == 3, "Choice after end is ignored");
            }

            dialogue.nodes[0].choices[0].nextNodeId = "";
            trace.Clear();
            runner.Start(dialogue);
            runner.Choose(0);
            runner.End();
            Require(string.Join(",", trace) == "start,end", "Empty choice target ends once");
            dialogue.nodes[0].choices[0].nextNodeId = null;
            trace.Clear();
            runner.Start(dialogue);
            runner.Choose(0);
            Require(string.Join(",", trace) == "start,end", "Null choice target ends");

            // Configuration is valid on Start, then becomes invalid during the session.
            dialogue.nodes[0].choices[0].nextNodeId = "left";
            trace.Clear();
            runner.Start(dialogue);
            dialogue.nodes[0].choices[0].nextNodeId = "missing";
            runner.Choose(0);
            Require(string.Join(",", trace) == "start,end", "Runtime invalid target safely ends");
            dialogue.nodes[0].choices[0].nextNodeId = "left";
            trace.Clear();
            runner.Start(dialogue);
            dialogue.nodes[0].choices[0] = null;
            runner.Choose(0);
            Require(string.Join(",", trace) == "start,end", "Runtime null choice safely ends");
            Debug.Log("[DialogueRunner] PASS: two branches, index checks, choice guard and safe endings");
        }
        finally
        {
            runner.OnNodeChanged -= changed;
            runner.OnEnded -= ended;
            runner.End();
            UnityEngine.Object.DestroyImmediate(dialogue);
        }
    }

    [MenuItem("Tools/RPG/Verify Linear Dialogue Runner")]
    public static void Verify()
    {
        var dialogue = ScriptableObject.CreateInstance<DialogueAsset>();
        dialogue.name = "TemporaryLinearDialogue";
        dialogue.nodes = new List<DialogueNode>
        {
            new DialogueNode { id = "start", content = "你好，旅行者。", nextNodeId = "road" },
            new DialogueNode { id = "road", content = "沿着小路向前走。", nextNodeId = "bye" },
            new DialogueNode { id = "bye", content = "一路平安。" }
        };
        var runner = new DialogueRunner();
        var trace = new List<string>();
        Action changed = () =>
        {
            Require(runner.IsRunning && runner.Current != null, "State is ready before node event");
            trace.Add(runner.Current.id);
            Debug.Log($"[DialogueRunner验证] 当前节点 {runner.Current.id}：{runner.Current.content}");
        };
        Action ended = () =>
        {
            Require(!runner.IsRunning && runner.Current == null, "State is cleared before end event");
            trace.Add("end");
            Debug.Log("[DialogueRunner验证] 结束");
        };
        runner.OnNodeChanged += changed;
        runner.OnEnded += ended;
        try
        {
            Debug.Log("[DialogueRunner验证] 开始直线对话");
            runner.Start(dialogue);
            Require(runner.Current == dialogue.nodes[0], "Start immediately selects first node");
            runner.TryAdvance();
            runner.TryAdvance();
            runner.TryAdvance();
            Require(string.Join(",", trace) == "start,road,bye,end", "Exact linear event order");
            runner.TryAdvance();
            runner.End();
            Require(trace.Count == 4, "Advance/end after completion are no-ops");

            trace.Clear();
            runner.Start(dialogue);
            dialogue.nodes[0].nextNodeId = "missing";
            runner.TryAdvance();
            Require(string.Join(",", trace) == "start,end", "Runtime invalid jump ends once");
            dialogue.nodes[0].nextNodeId = "road";
            dialogue.startNodeId = "missing-start";
            runner.Start(dialogue);
            Require(!runner.IsRunning && runner.Current == null, "Invalid start is rejected safely");
            dialogue.startNodeId = "start";
            runner.Start(dialogue);
            runner.Start(null);
            Require(!runner.IsRunning && runner.Current == null, "Null replacement ends old session");

            runner.Start(dialogue);
            runner.Start(dialogue);
            Require(runner.IsRunning && runner.Current == dialogue.nodes[0], "Restart selects first node");
            runner.End();
            dialogue.nodes[0].choices.Add(new DialogueChoice { text = "结束", nextNodeId = "" });
            runner.Start(dialogue);
            runner.TryAdvance();
            Require(runner.Current == dialogue.nodes[0], "Linear advance cannot skip choices");
            runner.End();
            Debug.Log("[DialogueRunner] PASS: linear lifecycle, invalid nodes, restart and choice guard");
        }
        finally
        {
            runner.OnNodeChanged -= changed;
            runner.OnEnded -= ended;
            runner.End();
            UnityEngine.Object.DestroyImmediate(dialogue);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[DialogueRunner] FAIL: " + message);
    }
}
