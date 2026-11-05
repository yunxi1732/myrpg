using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class DialogueViewVerification
{
    [MenuItem("Tools/RPG/Verify Dialogue View")]
    public static void Verify()
    {
        GameObject root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Dialogue/DialogueBox.prefab");
        var asset = ScriptableObject.CreateInstance<DialogueAsset>();
        try
        {
            Require(!root.activeSelf, "Dialogue root is initially hidden");
            var view = root.GetComponent<DialogueView>();
            // Locate by name without relying on the source prefab's intermediate layout.
            Text body = null, name = null;
            foreach (Text text in root.GetComponentsInChildren<Text>(true))
            {
                if (text.name == "DialogContent") body = text;
                if (text.name == "NpcName") name = text;
            }
            var button = root.transform.Find("DialogBox/ClickArea").GetComponent<Button>();
            var close = root.transform.Find("DialogBox/CloseButton").GetComponent<Button>();
            var mask = root.transform.Find("DialogueMask").GetComponent<Image>();
            var maskRect = (RectTransform)mask.transform;
            Require(mask.raycastTarget && mask.color.a > 0 && mask.transform.parent == button.transform.parent.parent,
                "Mask blocks background clicks and is a sibling of DialogBox");
            Require(mask.transform.GetSiblingIndex() < button.transform.parent.GetSiblingIndex() &&
                maskRect.anchorMin == Vector2.zero && maskRect.anchorMax == Vector2.one && maskRect.sizeDelta == Vector2.zero,
                "Full-screen mask renders behind dialogue");
            Require(view != null && body != null && name != null && button != null, "Required bindings exist");
            Require(close != null && close.GetComponent<Image>() != null &&
                close.GetComponentInChildren<Text>() != null, "Close button has background and label");
            Require(close.transform.parent == button.transform.parent && !close.transform.IsChildOf(button.transform),
                "Close button is a sibling, outside the advance button");
            Require(close.onClick.GetPersistentEventCount() == 0, "Close has no legacy callbacks");
            view.Close();
            view.Close();
            Require(!root.activeSelf, "Unbound close is safe");
            Require(button.onClick.GetPersistentEventCount() == 0, "No source GamePanel callbacks");
            asset.nodes = new List<DialogueNode>
            {
                new DialogueNode { id = "start", content = "第一句", nextNodeId = "end" },
                new DialogueNode { id = "end", content = "第二句" }
            };
            for (int i = 0; i < 2; i++)
            {
                view.StartDialogue(asset, "引路人", "村民");
                Require(root.activeSelf && name.text == "引路人" && body.text == "第一句", "Start shows name and first line");
                button.onClick.Invoke();
                Require(root.activeSelf && body.text == "第二句", "One click advances exactly once");
                DialogueRunner active = view.Runner;
                view.StartDialogue(asset, "不应替换");
                Require(ReferenceEquals(active, view.Runner) && body.text == "第二句" && name.text == "引路人",
                    "Repeated start cannot replace an active dialogue");
                button.onClick.Invoke();
                Require(!root.activeSelf && view.Runner == null, "End hides root and releases binding");
            }
            var container = root.transform.Find("DialogBox/DialogOptions");
            Require(container.GetComponent<VerticalLayoutGroup>() != null, "Choices have vertical layout");
            asset.nodes = new List<DialogueNode>
            {
                new DialogueNode
                {
                    id = "start", content = "选择路线", choices = new List<DialogueChoice>
                    {
                        new DialogueChoice { text = "去森林", nextNodeId = "forest" },
                        new DialogueChoice { text = "去村庄", nextNodeId = "village" }
                    }
                },
                new DialogueNode { id = "forest", content = "森林终点" },
                new DialogueNode { id = "village", content = "村庄终点" }
            };
            for (int index = 0; index < 2; index++)
            {
                view.StartDialogue(asset, "引路人");
                Require(container.gameObject.activeSelf && container.childCount == 2 && !button.interactable,
                    "Two options generated and ordinary advance disabled");
                button.onClick.Invoke();
                Require(view.Runner.Current.id == "start", "Ordinary click cannot bypass choices");
                var optionButton = container.GetChild(index).GetComponent<Button>();
                Require(optionButton.onClick.GetPersistentEventCount() == 0, "No source option callbacks");
                Require(container.GetChild(index).Find("OptionContent").GetComponent<Text>().text ==
                    asset.nodes[0].choices[index].text, "Choice text matches its index");
                var oldClick = optionButton.onClick;
                optionButton.onClick.Invoke();
                Require(view.Runner.Current.id == (index == 0 ? "forest" : "village"),
                    "Each button selects its own branch");
                Require(container.childCount == 0 && !container.gameObject.activeSelf && button.interactable,
                    "Old options removed and linear click restored");
                view.Close();
                view.StartDialogue(asset, "引路人");
                oldClick.Invoke();
                Require(view.Runner.Current.id == "start" && container.childCount == 2,
                    "Old callback cannot affect new dialogue");
                view.Close();
                Require(container.childCount == 0 && !root.activeSelf, "Close clears generated choices");
            }
            view.StartDialogue(asset, "引路人", "村民");
            DialogueRunner closingSession = view.Runner;
            int endedCount = 0, changedCount = 0;
            view.Runner.OnEnded += () => endedCount++;
            view.Runner.OnNodeChanged += () => changedCount++;
            close.onClick.Invoke();
            close.onClick.Invoke();
            view.Close();
            Require(endedCount == 1 && changedCount == 0, "Close ends once without advancing");
            Require(!root.activeSelf && view.Runner == null && !closingSession.IsRunning && closingSession.Current == null &&
                closingSession.NpcName == null && closingSession.NpcProfession == null && container.childCount == 0,
                "Close clears session and generated options");
            Require(body.text == "" && name.text == "", "Close clears displayed text");
            view.StartDialogue(asset, "重启对话");
            Require(root.activeSelf && container.childCount == 2, "Dialogue can restart after close");
            view.Close();
            Debug.Log("[DialogueView] PASS: text, branches, cleanup, independent close and repeated close");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
            UnityEngine.Object.DestroyImmediate(asset);
        }
    }

    [MenuItem("Tools/RPG/Verify Dialogue View Lifecycle (Play Mode)")]
    public static void VerifyLifecycle()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[DialogueViewLifecycle] 请进入 Play 模式后运行，验证真实 OnDisable/OnDestroy。");
            return;
        }
        GameObject root = UnityEngine.Object.Instantiate(
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Dialogue/DialogueBox.prefab"));
        var asset = ScriptableObject.CreateInstance<DialogueAsset>();
        GameManager manager = GameManager.instance;
        bool previousMenuPause = manager != null && manager.IsMenuPaused;
        asset.nodes = new List<DialogueNode>
        {
            new DialogueNode { id = "start", content = "第一句", nextNodeId = "end" },
            new DialogueNode { id = "end", content = "第二句" }
        };
        try
        {
            var view = root.GetComponent<DialogueView>();
            var next = root.transform.Find("DialogBox/ClickArea").GetComponent<Button>();
            var close = root.transform.Find("DialogBox/CloseButton").GetComponent<Button>();
            for (int i = 0; i < 20; i++)
            {
                var session = new DialogueRunner();
                int endCount = 0;
                session.OnEnded += () => endCount++;
                view.Bind(session);
                view.Bind(session);
                session.Start(asset);
                if (manager != null) Require(Time.timeScale == 0, "Dialogue pauses gameplay");
                next.onClick.Invoke();
                Require(session.Current.id == "end", "Repeated bind does not duplicate advance callbacks");
                root.SetActive(false);
                Require(endCount == 1 && !session.IsRunning && session.Current == null && view.Runner == null,
                    "Disable ends once and releases runner");
                session.Start(asset);
                next.onClick.Invoke();
                close.onClick.Invoke();
                Require(!root.activeSelf && session.Current.id == "start",
                    "Old events and buttons cannot affect disabled view/session");
                session.End();
            }
            var old = new DialogueRunner();
            view.Bind(old);
            old.Start(asset);
            var replacement = new DialogueRunner();
            view.Bind(replacement);
            Require(!old.IsRunning && view.Runner == replacement, "Rebind ends previous session");
            old.Start(asset);
            Require(!root.activeSelf, "Old runner cannot reopen rebound view");
            replacement.Start(asset);
            if (manager != null)
            {
                manager.PauseGame(false);
                Require(Time.timeScale == 0, "HUD/menu resume cannot override dialogue pause");
                manager.PauseGame(true);
                view.Close();
                Require(Time.timeScale == 0, "Dialogue close cannot override settings pause");
                manager.PauseGame(false);
                Require(Time.timeScale == 1, "All pause requests released resumes gameplay");
                view.Bind(replacement);
                replacement.Start(asset);
            }
            view.enabled = false;
            Require(!replacement.IsRunning && view.Runner == null && !root.activeSelf,
                "Disabling component releases session and hides view");
            view.enabled = true;
            replacement.Start(asset);
            Require(!root.activeSelf, "Re-enable does not silently resubscribe");
            view.Bind(replacement);
            Require(root.activeSelf, "Explicit bind can present a running session again");
            UnityEngine.Object.DestroyImmediate(view);
            Require(!replacement.IsRunning && replacement.Current == null, "Destroy ends owned session");
            replacement.Start(asset);
            next.onClick.Invoke();
            close.onClick.Invoke();
            Require(replacement.Current.id == "start", "Destroyed view leaves no button callbacks");
            old.End();
            replacement.End();
            Debug.Log("[DialogueViewLifecycle] PASS: 20 cycles, rebind, disable, destroy and detached callbacks");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(asset);
            if (manager != null) manager.PauseGame(previousMenuPause);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[DialogueView] FAIL: " + message);
    }
}
