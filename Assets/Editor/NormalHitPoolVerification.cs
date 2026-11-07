using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class NormalHitPoolVerification
{
    [MenuItem("Tools/RPG/Verify Normal Hit Pool (Play Mode)")]
    public static void Verify()
    {
        if (!Application.isPlaying) return;
        var root = new GameObject("NormalHitPoolVerification");
        try
        {
            var pool = root.AddComponent<TemporaryEffectPool>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/FX/HitFX_00.prefab");
            var instance = pool.Take(prefab, Vector3.zero, Quaternion.identity);
            var fx = instance.GetComponent<HitEffectFX>();
            var timer = typeof(HitEffectFX).GetField("remainingTime", BindingFlags.NonPublic | BindingFlags.Instance);
            var update = typeof(HitEffectFX).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            Require(!fx.IsPlaying, "Borrowing alone does not start lifetime");
            fx.Play(Vector3.one, Quaternion.Euler(0, 0, 25));
            Require(fx.IsPlaying && (float)timer.GetValue(fx) == 0.5f, "Play starts fresh half-second timer");
            timer.SetValue(fx, 0.1f);
            fx.Invoke("Update", 30f);
            Require(pool.Return(instance), "Early return accepted");
            Require(!fx.IsPlaying && (float)timer.GetValue(fx) == 0f && !fx.IsInvoking(), "Early return cancels timer and own invokes");
            update.Invoke(fx, null);
            Require(instance.OwnerPool.IdleCount == 1, "Idle update cannot return again");
            var reused = pool.Take(prefab, Vector3.zero, Quaternion.identity);
            Require(reused == instance && !fx.IsPlaying, "Reborrow does not inherit old timer");
            fx.Play(Vector3.zero, Quaternion.identity);
            Require((float)timer.GetValue(fx) == 0.5f, "Reuse gets full lifetime");
            timer.SetValue(fx, 0f);
            update.Invoke(fx, null);
            Require(!instance.IsInUse && !instance.gameObject.activeSelf && instance.OwnerPool.IdleCount == 1, "Expiry returns rather than destroys");
            Require(!instance.ReturnToPool(), "Duplicate expiry return rejected");
            Require(pool.Take(prefab, Vector3.zero, Quaternion.identity) == instance, "Expired object reusable");
            pool.Dispose();
            Require(!instance.ReturnToPool(), "Late return rejected after disposal");
            Debug.Log("[NormalHitPool] PASS");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[NormalHitPool] " + message);
    }
}
