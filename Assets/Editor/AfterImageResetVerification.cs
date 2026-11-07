using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class AfterImageResetVerification
{
    [MenuItem("Tools/RPG/Verify After Image Reset")]
    public static void Verify()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/FX/AfterImageFX.prefab");
        var root = new GameObject("AfterImageResetVerification");
        root.SetActive(false);
        Sprite secondSprite = null;
        try
        {
            var clone = UnityEngine.Object.Instantiate(prefab, root.transform);
            var fx = clone.GetComponent<AfterImageFx>();
            var renderer = clone.GetComponent<SpriteRenderer>();
            var sprite = renderer.sprite;
            var color = renderer.color;
            var scale = clone.transform.localScale;
            fx.Play(1.5f, sprite, Vector3.zero, Quaternion.identity);
            renderer.color = new Color(0.2f, 0.3f, 0.4f, 0f);
            clone.transform.localScale = Vector3.one * 4;
            secondSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            var position = new Vector3(2, 3, 0);
            var rotation = Quaternion.Euler(0, 180, 25);
            fx.Play(3f, secondSprite, position, rotation);
            Require(renderer.sprite == secondSprite && renderer.color == color, "Replace sprite and reset RGB/alpha");
            Require(clone.transform.position == position && Quaternion.Angle(clone.transform.rotation, rotation) < 0.01f, "Reset absolute position and rotation");
            Require(clone.transform.localScale == scale, "Restore prefab scale");
            var speed = typeof(AfterImageFx).GetField("colorLoseRate", BindingFlags.Instance | BindingFlags.NonPublic);
            Require((float)speed.GetValue(fx) == 3f, "Replace previous fade speed");
            renderer.color = Color.clear;
            fx.SetupAfterImage(1.5f, sprite);
            Require(renderer.sprite == sprite && renderer.color == color && (float)speed.GetValue(fx) == 1.5f, "Legacy entry also resets every play");
            Require(clone.transform.position == position && Quaternion.Angle(clone.transform.rotation, rotation) < 0.01f, "Legacy entry preserves current pose");
            Debug.Log("[AfterImageReset] PASS");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            if (secondSprite != null) UnityEngine.Object.DestroyImmediate(secondSprite);
        }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[AfterImageReset] " + message);
    }
}
