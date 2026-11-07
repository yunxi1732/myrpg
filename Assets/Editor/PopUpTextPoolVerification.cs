using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class PopUpTextPoolVerification
{
    [MenuItem("Tools/RPG/Verify Pooled Pop Up Text (Play Mode)")]
    public static void Verify()
    {
        if (!Application.isPlaying) return;
        var root = new GameObject("PopUpTextPoolVerification");
        try
        {
            var pool = root.AddComponent<TemporaryEffectPool>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/FX/PopUpText.prefab");
            var instance = pool.Take(prefab, Vector3.zero, Quaternion.identity);
            var fx = instance.GetComponent<PopUpTextFX>();
            var text = instance.GetComponent<TextMeshPro>();
            fx.Play("First");
            Color initial = text.color;
            Vector3 scale = instance.transform.localScale;
            fx.Invoke("Update", 30);
            typeof(PopUpTextFX).GetField("textTimer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(fx, -1f);
            text.color = new Color(1, 0, 0, -0.1f);
            typeof(PopUpTextFX).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(fx, null);
            Require(!instance.IsInUse && !instance.gameObject.activeSelf && !fx.IsInvoking(), "Fade returns and cancels own invokes");
            Require(!instance.ReturnToPool(), "Reject repeated return");
            instance.transform.localScale = Vector3.one * 3;
            var reused = pool.Take(prefab, Vector3.one, Quaternion.identity);
            Require(reused == instance, "Reuse faded instance");
            fx.Play("Second");
            Require(text.text == "Second" && text.color == initial && instance.transform.localScale == scale, "Reset reused appearance");
            pool.Dispose();
            Require(!instance.ReturnToPool(), "Late return after scene pool disposal is safe");
            Debug.Log("[PooledPopUpText] PASS");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[PooledPopUpText] " + message);
    }
}
