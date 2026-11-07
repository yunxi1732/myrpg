using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AfterImagePoolVerification
{
    [MenuItem("Tools/RPG/Verify After Image Pool (Play Mode)")]
    public static void Verify()
    {
        if (!Application.isPlaying) return;
        var scene = SceneManager.CreateScene("AfterImagePoolVerification");
        TemporaryEffectPool pool = null;
        Sprite secondSprite = null;
        try
        {
            var source = new GameObject("IsolatedAfterImageSource");
            source.SetActive(false); // Do not run player initialization or alter real character effects.
            SceneManager.MoveGameObjectToScene(source, scene);
            var renderer = source.AddComponent<SpriteRenderer>();
            var playerFx = source.AddComponent<PlayerFX>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/FX/AfterImageFX.prefab");
            renderer.sprite = prefab.GetComponent<SpriteRenderer>().sprite;
            typeof(EntityFX).GetField("sr", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(playerFx, renderer);
            Field("afterImagePrefab").SetValue(playerFx, prefab);
            Field("colorLoseRate").SetValue(playerFx, 1.5f);
            Field("afterImageCooldown").SetValue(playerFx, 0.03f);
            Field("afterImageTimer").SetValue(playerFx, -0.1f);
            pool = TemporaryEffectPool.ForScene(scene);
            source.transform.SetPositionAndRotation(new Vector3(2, 3, 0), Quaternion.Euler(0, 180, 0));
            playerFx.CreateAfterImage();
            var instances = pool.GetComponentsInChildren<PooledTemporaryEffect>(true);
            Require(instances.Length == 1, "CreateAfterImage borrows from scene pool");
            var instance = instances[0];
            var fx = instance.GetComponent<AfterImageFx>();
            var image = instance.GetComponent<SpriteRenderer>();
            var initialColor = image.color;
            var initialScale = instance.transform.localScale;
            Require(instance.IsInUse && image.sprite == renderer.sprite && image.color.a == 1f, "Initialize current snapshot");
            Require(instance.transform.position == source.transform.position && Quaternion.Angle(instance.transform.rotation, source.transform.rotation) < 0.01f, "Initialize source pose");
            Require((float)Field("afterImageTimer").GetValue(playerFx) == 0.03f, "Original generation interval retained");
            playerFx.CreateAfterImage();
            Require(pool.GetComponentsInChildren<PooledTemporaryEffect>(true).Length == 1, "Cooldown blocks another spawn");
            fx.Invoke("Update", 30f);
            image.color = new Color(0.2f, 0.3f, 0.4f, 0f);
            typeof(AfterImageFx).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(fx, null);
            Require(!instance.IsInUse && !instance.gameObject.activeSelf && !fx.IsInvoking(), "Fade returns and cancels own invokes");
            Require(!instance.ReturnToPool(), "Duplicate return rejected");
            instance.transform.localScale = Vector3.one * 4;
            secondSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
            renderer.sprite = secondSprite;
            source.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Field("afterImageTimer").SetValue(playerFx, -0.1f);
            playerFx.CreateAfterImage();
            Require(pool.GetComponentsInChildren<PooledTemporaryEffect>(true).Length == 1 && instance.IsInUse, "Reuse faded instance");
            Require(image.sprite == secondSprite && image.color == initialColor && instance.transform.localScale == initialScale, "Replay replaces sprite and resets appearance");
            Require(instance.transform.position == Vector3.zero && Quaternion.Angle(instance.transform.rotation, Quaternion.identity) < 0.01f, "Replay resets pose");
            pool.Dispose();
            Require(!instance.ReturnToPool(), "Late return after disposal is safe");
            Debug.Log("[AfterImagePool] PASS");
        }
        finally
        {
            if (pool != null) pool.Dispose();
            if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            if (secondSprite != null) UnityEngine.Object.Destroy(secondSprite);
        }
    }
    private static FieldInfo Field(string name) => typeof(PlayerFX).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[AfterImagePool] " + message);
    }
}
