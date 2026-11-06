using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class NpcDialogueVerification
{
    [MenuItem("Tools/RPG/Verify NPC Talk Session")]
    public static void Verify()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("[NpcTalk] 请退出 Play 后执行独立连接检查；真实触发请在MainScene中验收。");
            return;
        }
        var panel = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Dialogue/DialogueBox.prefab");
        var uiRoot = new GameObject("TemporaryInteractionUI");
        uiRoot.SetActive(false);
        var ui = uiRoot.AddComponent<UI>();
        var aRoot = new GameObject("TemporaryNPC_A");
        var bRoot = new GameObject("TemporaryNPC_B");
        var data = ScriptableObject.CreateInstance<DialogueAsset>();
        data.nodes = new List<DialogueNode>
        {
            new DialogueNode { id = "start", content = "第一句", nextNodeId = "end" },
            new DialogueNode { id = "end", content = "结束句" }
        };
        var view = panel.GetComponent<DialogueView>();
        var a = aRoot.AddComponent<NpcInteractable>();
        var b = bRoot.AddComponent<NpcInteractable>();
        try
        {
            Configure(a, ui, view, data, "甲");
            Configure(b, ui, view, data, "乙");
            Require(!a.TryStartTalk(), "Out-of-range NPC cannot start");
            SetRange(a, true);
            SetRange(a, true);
            SetRange(b, true);
            Require(ui.Interactions.Options.Count == 2 && ReferenceEquals(ui.Interactions.GetSelected().owner, a),
                "Nearby NPCs register unique talk candidates");
            Require(ui.Interactions.TryExecuteSelected() && view.CurrentNpc == a && view.IsDialogueRunning,
                "Selected NPC starts its own session");
            DialogueRunner first = view.Runner;
            Require(first.NpcName == "甲" && first.NpcProfession == "村民" && first.Current == data.nodes[0],
                "NPC data and identity reach runner");
            Text label = panel.transform.Find("DialogBox/DialogInfo/NpcName")?.GetComponent<Text>();
            // The extracted hierarchy can contain layout wrappers; inspect all text components instead.
            foreach (var text in panel.GetComponentsInChildren<Text>(true))
                if (text.name == "NpcName") label = text;
            Require(label != null && label.text == "甲", "NPC name is shown");
            Require(!a.TryStartTalk() && !b.TryStartTalk() && ReferenceEquals(first, view.Runner) &&
                first.Current == data.nodes[0], "Same and other NPC cannot replace active session");
            first.TryAdvance();
            first.TryAdvance();
            Require(!view.IsDialogueRunning && view.CurrentNpc == null, "Natural end clears NPC association");
            Require(b.TryStartTalk() && view.CurrentNpc == b, "Another NPC may start after end");
            SetRange(a, false);
            Require(ui.Interactions.Options.Count == 1 && view.CurrentNpc == b, "Other NPC exit cannot close active owner");
            b.SendMessage("OnDisable");
            Require(ui.Interactions.Options.Count == 0 && view.CurrentNpc == null && !panel.activeSelf,
                "NPC disable removes its candidate and ends owned session");
            SetRange(a, true);
            a.dialogue = null;
            Require(!a.TryStartTalk() && view.CurrentNpc == null && !view.IsDialogueRunning,
                "Invalid data leaves no stuck session or NPC association");
            a.dialogue = data;
            Require(a.TryStartTalk(), "Failed start does not block a later valid session");
            ui.Interactions.Dispose();
            Require(view.CurrentNpc == null && !first.IsRunning && ui.Interactions.Options.Count == 0,
                "Manager disposal removes NPC candidate and ends owned dialogue");
            Require(!a.IsPlayerInRange && !a.TryStartTalk(), "Disposed scene clears NPC range ownership");
            Debug.Log("[NpcTalk] PASS: range candidates, NPC metadata, session association, busy guard and cleanup");
        }
        finally
        {
            view.Close();
            a.SendMessage("OnDisable");
            b.SendMessage("OnDisable");
            ui.SendMessage("OnDestroy");
            UnityEngine.Object.DestroyImmediate(aRoot);
            UnityEngine.Object.DestroyImmediate(bRoot);
            UnityEngine.Object.DestroyImmediate(uiRoot);
            UnityEngine.Object.DestroyImmediate(data);
            PrefabUtility.UnloadPrefabContents(panel);
        }
    }

    private static void Configure(NpcInteractable npc, UI ui, DialogueView view, DialogueAsset data, string name)
    {
        var serialized = new SerializedObject(npc);
        serialized.FindProperty("interactionUi").objectReferenceValue = ui;
        serialized.FindProperty("dialogueView").objectReferenceValue = view;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        npc.dialogue = data;
        npc.npcName = name;
        npc.npcProfession = "村民";
    }

    private static void SetRange(NpcInteractable npc, bool inside) =>
        typeof(NpcInteractable).GetMethod("SetPlayerInRange", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(npc, new object[] { inside });

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[NpcTalk] FAIL: " + message);
    }
}
