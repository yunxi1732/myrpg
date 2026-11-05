using System;
using UnityEditor;
using UnityEngine;

public static class UIPanelRegistryVerification
{
    [MenuItem("Tools/RPG/Verify Menu Visibility")]
    public static void VerifyMenuVisibility()
    {
        var registry = new UISystem();
        var root = new GameObject("Menu visibility verification");
        try
        {
            foreach (UIPanelId id in Enum.GetValues(typeof(UIPanelId)))
            {
                var panel = new GameObject(id.ToString());
                panel.transform.SetParent(root.transform);
                registry.RegisterPanel(id, panel);
            }
            var overlay = new GameObject("Fade overlay");
            overlay.transform.SetParent(root.transform);
            var endText = new GameObject("End text");
            endText.transform.SetParent(root.transform);

            foreach (bool overlayVisible in new[] { true, false })
            {
                overlay.SetActive(overlayVisible);
                foreach (UIPanelId target in new[] { UIPanelId.Character, UIPanelId.Options, UIPanelId.InGame })
                {
                    Require(registry.ShowExclusive(target), "Registered menu can be selected");
                    foreach (UIPanelId id in Enum.GetValues(typeof(UIPanelId)))
                        Require(registry.GetPanel(id).gameObject.activeSelf == (id == target),
                            "Only selected menu/HUD is visible: " + target);
                    Require(overlay.activeSelf == overlayVisible && endText.activeSelf,
                        "Independent overlays retain their visibility");
                }
            }
            registry.CloseMenuGroup();
            foreach (UIPanelId id in Enum.GetValues(typeof(UIPanelId)))
                Require(!registry.GetPanel(id).gameObject.activeSelf, "Null selection closes menu group");
            Require(endText.activeSelf, "Active end text is outside menu group");
            Debug.Log("[MenuVisibility] PASS: Character -> Options -> InGame; overlays preserved");
        }
        finally
        {
            registry.Clear();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [MenuItem("Tools/RPG/Verify UI Panel Registry")]
    public static void Verify()
    {
        var registry = new UISystem();
        var otherRegistry = new UISystem();
        var visible = new GameObject("Registry verification visible");
        var hidden = new GameObject("Registry verification hidden");
        hidden.SetActive(false);
        try
        {
            Require(registry.GetPanel(UIPanelId.Character) == null, "Missing ID returns null");
            BasePanel first = registry.RegisterPanel(UIPanelId.Character, visible);
            BasePanel second = hidden.AddComponent<BasePanel>();
            Require(registry.RegisterPanel(UIPanelId.Craft, hidden) == second, "Reuse existing component");
            Require(registry.GetPanel(UIPanelId.Character) == first && first.gameObject == visible,
                "Return original scene panel");
            Require(visible.activeSelf && !hidden.activeSelf, "Registration preserves visibility");
            Require(registry.RegisterPanel(UIPanelId.Character, visible) == first &&
                visible.GetComponents<BasePanel>().Length == 1, "Repeated registration is idempotent");
            bool rejected = false;
            try { registry.RegisterPanel(UIPanelId.Character, hidden); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected && registry.GetPanel(UIPanelId.Character) == first, "Reject conflicting ID");
            Require(otherRegistry.GetPanel(UIPanelId.Character) == null, "Registries are independent");
            registry.Clear();
            Require(registry.GetPanel(UIPanelId.Character) == null && visible != null && hidden != null,
                "Clear releases references without destroying panels");
            UnityEngine.Object.DestroyImmediate(hidden);
            registry.RegisterPanel(UIPanelId.Craft, visible);
            UnityEngine.Object.DestroyImmediate(visible);
            Require(registry.GetPanel(UIPanelId.Craft) == null, "Destroyed panel returns null");
            Debug.Log("[UIPanelRegistry] PASS: 9 checks");
        }
        finally
        {
            registry.Clear();
            if (visible != null) UnityEngine.Object.DestroyImmediate(visible);
            if (hidden != null) UnityEngine.Object.DestroyImmediate(hidden);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[UIPanelRegistry] FAIL: " + message);
    }
}
