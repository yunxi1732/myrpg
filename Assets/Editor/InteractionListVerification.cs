using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class InteractionListVerification
{
    [MenuItem("Tools/RPG/Verify Interaction Prompt List")]
    public static void Verify()
    {
        var root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Dialogue/InteractionPrompts.prefab");
        var manager = new InteractionManager();
        try
        {
            var view = root.GetComponent<InteractionListView>();
            var container = root.transform.Find("List");
            Require(container.childCount == 0 && !container.gameObject.activeSelf,
                "Template is an asset, not a permanently visible row");
            Require(container.GetComponent<VerticalLayoutGroup>() != null &&
                container.GetComponent<ContentSizeFitter>().verticalFit == ContentSizeFitter.FitMode.PreferredSize,
                "List arranges vertically and adapts height");
            view.Bind(manager);
            var owner = new object();
            var talk = new InteractionOptionData(owner, "talk", "对话", null, "E", KeyCode.E);
            var trade = new InteractionOptionData(owner, "trade", "交易", null, "F", KeyCode.F);
            manager.Register(talk);
            Require(container.gameObject.activeSelf && container.childCount == 1, "First candidate shows list");
            Transform first = container.GetChild(0);
            manager.Register(trade);
            Require(container.childCount == 2 && container.GetChild(0) == first, "Growing list reuses existing row");
            CheckRow(first, "对话", "E");
            CheckRow(container.GetChild(1), "交易", "F");
            Require(first.GetComponent<Image>().color == Color.white &&
                container.GetChild(1).GetComponent<Image>().color != Color.white, "First candidate is highlighted");
            manager.MoveSelection(1);
            Require(container.GetChild(0) == first && first.GetComponent<Image>().color != Color.white &&
                container.GetChild(1).GetComponent<Image>().color == Color.white,
                "Selection updates highlight without rebuilding rows");
            manager.Unregister(talk);
            Require(container.childCount == 1 && container.GetChild(0) == first, "Shrinking list reuses first row");
            CheckRow(first, "交易", "F");
            Require(first.GetComponent<Image>().color == Color.white, "Removal preserves selected candidate highlight");
            view.Bind(manager);
            manager.Clear();
            Require(container.childCount == 0 && !container.gameObject.activeSelf, "Empty list hides and removes rows");
            view.Bind(null);
            manager.Register(talk);
            Require(container.childCount == 0 && !container.gameObject.activeSelf, "Unbound source cannot update display");
            view.Bind(manager);
            CheckRow(container.GetChild(0), "对话", "E");
            Debug.Log("[InteractionPromptList] PASS: layout, text/key, row reuse, empty hide and binding");
        }
        finally
        {
            root.GetComponent<InteractionListView>().Bind(null);
            manager.Clear();
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void CheckRow(Transform row, string action, string key)
    {
        Require(row.GetComponent<Image>() != null &&
            row.Find("InteractionContent").GetComponent<Text>().text == action &&
            row.Find("Image_KeyTip/TextKeyTip").GetComponent<Text>().text == key,
            "Row retains background and displays action/key correctly");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[InteractionPromptList] FAIL: " + message);
    }
}
