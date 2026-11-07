using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CriticalHitPoolVerification
{
    [MenuItem("Tools/RPG/Verify Critical Hit Pool (Play Mode)")]
    public static void Verify()
    {
        if (!Application.isPlaying) return;
        var randomState = UnityEngine.Random.state;
        var scene = SceneManager.CreateScene("CriticalHitPoolVerification");
        TemporaryEffectPool pool = null;
        try
        {
            var source = new GameObject("IsolatedHitSource");
            source.SetActive(false); // Never run Entity/EntityFX Start or character effects.
            SceneManager.MoveGameObjectToScene(source, scene);
            var entity = source.AddComponent<Entity>();
            var fx = source.AddComponent<EntityFX>();
            var normal = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/FX/HitFX_00.prefab");
            var critical = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/FX/HitFX_Critical.prefab");
            typeof(EntityFX).GetField("hitFx", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(fx, normal);
            typeof(EntityFX).GetField("criticalHitFx", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(fx, critical);
            pool = TemporaryEffectPool.ForScene(scene);
            var normalInstance = pool.Take(normal, Vector3.zero, Quaternion.identity);
            pool.Return(normalInstance);
            PooledTemporaryEffect previous = null;
            foreach (int facing in new[] { 1, -1 })
            {
                typeof(Entity).GetProperty("facingDir").SetValue(entity, facing);
                UnityEngine.Random.InitState(314159);
                UnityEngine.Random.Range(-90, 90); // Preserve original call order, including discarded rotation.
                float x = UnityEngine.Random.Range(-0.5f, 0.5f);
                float y = UnityEngine.Random.Range(-0.5f, 0.5f);
                float z = UnityEngine.Random.Range(-45, 45);
                Quaternion expected = Quaternion.Euler(0, facing == -1 ? 180 : 0, z);
                UnityEngine.Random.InitState(314159);
                fx.CreatedHitFx(source.transform, true);
                var all = pool.GetComponentsInChildren<PooledTemporaryEffect>(true);
                PooledTemporaryEffect instance = Array.Find(all, item => item.IsInUse);
                Require(instance != null && instance.OwnerPool.Prefab == critical, "Critical prefab selected");
                Require(instance.OwnerPool != normalInstance.OwnerPool && !normalInstance.IsInUse, "Critical has independent pool");
                Require(instance.transform.position == new Vector3(x, y, 0), "Preserve random offset");
                Require(Quaternion.Angle(instance.transform.rotation, expected) < 0.01f, "Preserve facing and random rotation");
                if (previous != null) Require(previous == instance, "Reuse critical instance across opposite facings");
                var playback = instance.GetComponent<HitEffectFX>();
                var timer = typeof(HitEffectFX).GetField("remainingTime", BindingFlags.Instance | BindingFlags.NonPublic);
                Require(playback.IsPlaying && (float)timer.GetValue(playback) == 0.5f, "Fresh half-second playback");
                timer.SetValue(playback, 0f);
                typeof(HitEffectFX).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(playback, null);
                Require(!instance.IsInUse && !instance.gameObject.activeSelf && !playback.IsPlaying, "Expiry returns critical instance");
                Require(!pool.Return(instance), "Reject duplicate critical return");
                previous = instance;
            }
            Debug.Log("[CriticalHitPool] PASS");
        }
        finally
        {
            UnityEngine.Random.state = randomState;
            if (pool != null) pool.Dispose();
            if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[CriticalHitPool] " + message);
    }
}
