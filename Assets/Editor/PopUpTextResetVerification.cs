using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class PopUpTextResetVerification
{
    [MenuItem("Tools/RPG/Verify Pop Up Text Reset")]
    public static void Verify()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/FX/PopUpText.prefab");
        var root = new GameObject("PopUpTextResetVerification");
        root.SetActive(false);
        try
        {
            var clone = UnityEngine.Object.Instantiate(prefab, root.transform);
            var fx = clone.GetComponent<PopUpTextFX>();
            var text = clone.GetComponent<TextMeshPro>();
            Color color = text.color;
            Vector3 scale = clone.transform.localScale;
            fx.Play("First");
            Require(text.text == "First", "First play assigns text");
            text.color = new Color(0.2f, 0.3f, 0.4f, 0);
            clone.transform.localScale = Vector3.one * 3;
            Field("currentSpeed").SetValue(fx, 10f);
            Field("textTimer").SetValue(fx, -1f);
            fx.Play("Second");
            Require(text.text == "Second" && text.color == color, "Replay restores text, RGB and alpha");
            Require(clone.transform.localScale == scale, "Replay restores initial scale");
            Require((float)Field("currentSpeed").GetValue(fx) == (float)Field("speed").GetValue(fx), "Replay restores initial speed");
            Require((float)Field("textTimer").GetValue(fx) == (float)Field("lifeTime").GetValue(fx), "Replay restores lifetime");
            fx.Play("Third");
            Require(text.text == "Third" && text.color == color, "Repeated reset remains stable");
            Debug.Log("[PopUpTextReset] PASS");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static FieldInfo Field(string name) => typeof(PopUpTextFX).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[PopUpTextReset] " + message);
    }
}
