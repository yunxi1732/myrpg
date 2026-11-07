using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public static class EffectAsyncCleanupVerification
{
    private static bool running;

    [MenuItem("Tools/RPG/Verify Effect Async Cleanup (Play Mode)")]
    public static async void Verify()
    {
        if (!Application.isPlaying || Time.timeScale <= 0 || running)
        {
            Debug.LogWarning("[EffectAsyncCleanup] 需要未暂停的 Play，且不能重复运行。");
            return;
        }
        running = true;
        var root = new GameObject("EffectAsyncCleanupVerification");
        var pool = root.AddComponent<TemporaryEffectPool>();
        try
        {
            foreach (string name in new[] { "PopUpText", "HitFX_00", "HitFX_Critical", "AfterImageFX" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/FX/" + name + ".prefab");
                var instance = pool.Take(prefab, Vector3.zero, Quaternion.identity);
                var effect = FindPlayback(instance);
                Play(effect);
                if (effect is HitEffectFX)
                    typeof(HitEffectFX).GetField("remainingTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(effect, 0.05f);
                bool oldCoroutineRan = false;
                effect.StartCoroutine(Delayed(() => { oldCoroutineRan = true; instance.ReturnToPool(); }));
                effect.Invoke("StopPlayback", 0.1f);
                Require(pool.Return(instance), name + " early return");
                Require(!effect.IsInvoking() && !Playing(effect), name + " clears invoke and playing state");
                Require(pool.Take(prefab, Vector3.zero, Quaternion.identity) == instance, name + " same instance reused");
                Play(effect);
                bool newCoroutineRan = false;
                effect.StartCoroutine(Delayed(() => newCoroutineRan = true));
                await Wait(0.2f);
                Require(!oldCoroutineRan && newCoroutineRan, name + " old coroutine stopped; new coroutine works");
                Require(instance.IsInUse && instance.gameObject.activeSelf && Playing(effect), name + " old invoke cannot stop new playback");
                // Replay without returning must cancel the previous playback's work too.
                oldCoroutineRan = false;
                effect.StartCoroutine(Delayed(() => { oldCoroutineRan = true; instance.ReturnToPool(); }));
                effect.Invoke("StopPlayback", 0.1f);
                Play(effect);
                await Wait(0.2f);
                Require(!oldCoroutineRan && instance.IsInUse && Playing(effect), name + " direct replay clears old work");
                // Explicit pool cleanup must work even when OnDisable cannot be triggered again.
                effect.enabled = false;
                effect.Invoke("StopPlayback", 5f);
                Require(pool.Return(instance) && !effect.IsInvoking(), name + " disabled component still explicitly cleaned");
                effect.enabled = true;
            }
            Debug.Log("[EffectAsyncCleanup] PASS");
        }
        catch (Exception error) { Debug.LogException(error); }
        finally
        {
            if (pool != null) pool.Dispose();
            if (root != null) UnityEngine.Object.Destroy(root);
            running = false;
        }
    }

    private static IEnumerator Delayed(Action action)
    {
        yield return new WaitForSeconds(0.1f);
        action();
    }
    private static async Task Wait(float seconds)
    {
        float end = Time.time + seconds;
        float deadline = Time.realtimeSinceStartup + 10f;
        do
        {
            await Task.Yield();
            if (!Application.isPlaying || Time.realtimeSinceStartup > deadline)
                throw new InvalidOperationException("Play stopped or verification timed out while paused.");
        } while (Time.time < end);
    }
    private static MonoBehaviour FindPlayback(PooledTemporaryEffect instance)
    {
        foreach (var component in instance.GetComponents<MonoBehaviour>())
            if (component is ITemporaryEffectPlayback) return component;
        throw new InvalidOperationException("Missing playback component");
    }
    private static void Play(MonoBehaviour effect)
    {
        if (effect is PopUpTextFX text) text.Play("Async cleanup");
        else if (effect is HitEffectFX hit) hit.Play(Vector3.zero, Quaternion.identity);
        else if (effect is AfterImageFx image) image.Play(1.5f, image.GetComponent<SpriteRenderer>().sprite, Vector3.zero, Quaternion.identity);
    }
    private static bool Playing(MonoBehaviour effect)
    {
        if (effect is PopUpTextFX text) return text.IsPlaying;
        if (effect is HitEffectFX hit) return hit.IsPlaying;
        return ((AfterImageFx)effect).IsPlaying;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[EffectAsyncCleanup] " + message);
    }
}
