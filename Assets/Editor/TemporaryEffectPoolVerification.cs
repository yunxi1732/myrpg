using System;
using UnityEditor;
using UnityEngine;

public static class TemporaryEffectPoolVerification
{
    [MenuItem("Tools/RPG/Verify Temporary Effect Take (Play Mode)")]
    public static void Verify()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[TemporaryEffectTake] 请先进入 Play。");
            return;
        }
        var root = new GameObject("TemporaryEffectTakeVerification");
        try
        {
            var pool = root.AddComponent<TemporaryEffectPool>();
            var prefabA = new GameObject("TemporaryPrefabA");
            prefabA.transform.SetParent(root.transform);
            prefabA.SetActive(false);
            var prefabB = new GameObject("TemporaryPrefabB");
            prefabB.transform.SetParent(root.transform);
            prefabB.SetActive(false);
            var position = new Vector3(12, 34, 0);
            var rotation = Quaternion.Euler(0, 0, 25);
            var a1 = pool.Take(prefabA, position, rotation);
            var a2 = pool.Take(prefabA, Vector3.zero, Quaternion.identity);
            var b = pool.Take(prefabB, Vector3.zero, Quaternion.identity);
            Require(a1 != a2 && a1 != b, "Busy instances must never be reused");
            Require(a1.OwnerPool == a2.OwnerPool && a1.OwnerPool != b.OwnerPool, "Separate prefab pools");
            Require(a1.OwnerPool.Prefab == prefabA && b.OwnerPool.Prefab == prefabB, "Prefab identity");
            Require(a1.IsInUse && a2.IsInUse && b.IsInUse && a1.gameObject.activeSelf, "Usage and activation");
            Require(a1.transform.position == position && Quaternion.Angle(a1.transform.rotation, rotation) < 0.01f, "Spawn pose");
            a1.gameObject.SetActive(false);
            Require(pool.Take(prefabA, Vector3.zero, Quaternion.identity) != a1 && a1.IsInUse, "Disabling is not returning");
            var otherRoot = new GameObject("OtherPool");
            otherRoot.transform.SetParent(root.transform);
            var other = otherRoot.AddComponent<TemporaryEffectPool>();
            var foreign = other.Take(prefabA, position, rotation);
            Require(!pool.Return(foreign) && foreign.IsInUse, "Reject foreign return");
            Require(!pool.Return(null), "Reject null");
            Require(pool.Return(a1) && !a1.IsInUse && !a1.gameObject.activeSelf, "Hide returned instance");
            Require(!pool.Return(a1), "Reject duplicate return");
            Require(pool.Return(b), "Return other prefab");
            var reused = pool.Take(prefabA, position, rotation);
            Require(reused == a1 && reused.IsInUse && reused.gameObject.activeSelf, "Reuse first");
            Require(pool.Take(prefabA, position, rotation) != reused, "Never lend twice");
            Require(pool.Take(prefabB, position, rotation) == b, "Separate idle collections");
            Require(pool.Return(a2), "Return before destruction");
            UnityEngine.Object.DestroyImmediate(a2.gameObject);
            Require(pool.Take(prefabA, position, rotation) != null, "Skip destroyed idle instance");
            pool.SetIdleCapacity(prefabA, 1);
            var c1 = pool.Take(prefabA, position, rotation);
            var c2 = pool.Take(prefabA, position, rotation);
            Require(c1 != c2 && c1.IsInUse && c2.IsInUse, "Capacity does not limit active effects");
            Require(pool.Return(c1) && pool.Return(c2), "Accept overflow returns");
            Require(c1.OwnerPool.IdleCount == 1 && !c2.IsInUse && !c2.gameObject.activeSelf, "Retain only capacity, hide overflow");
            Require(!pool.Return(c2), "Reject overflow duplicate return before delayed destruction");
            Require(pool.Take(prefabA, position, rotation) == c1, "Reuse retained instance");
            Require(b.IsInUse && b.gameObject.activeSelf && b.OwnerPool.IdleCapacity == 32, "Other prefab and active instances unaffected");
            Require(pool.Return(c1), "Return before shrinking");
            pool.SetIdleCapacity(prefabA, 0);
            Require(c1.OwnerPool.IdleCount == 0 && reused.IsInUse, "Shrink clears only idle instances");
            var zero = pool.Take(prefabA, position, rotation);
            Require(pool.Return(zero) && zero.OwnerPool.IdleCount == 0, "Zero capacity still permits take and return");
            bool invalidCapacity = false;
            try { pool.SetIdleCapacity(prefabA, -1); }
            catch (ArgumentOutOfRangeException) { invalidCapacity = true; }
            Require(invalidCapacity, "Reject negative capacity");
            bool rejected = false;
            try { pool.Take(null, Vector3.zero, Quaternion.identity); }
            catch (ArgumentNullException) { rejected = true; }
            Require(rejected, "Reject missing prefab");
            var oldOwner = reused.OwnerPool;
            pool.Dispose();
            pool.Dispose();
            Require(pool.IsDisposed && oldOwner.IsDisposed && oldOwner.IdleCount == 0, "Dispose clears records idempotently");
            Require(reused.OwnerPool == null && !reused.IsInUse && !reused.gameObject.activeSelf, "Dispose detaches active instances");
            Require(!pool.Return(reused) && !pool.Return(foreign), "Reject late returns");
            bool disposedRejected = false;
            try { pool.Take(prefabA, position, rotation); }
            catch (ObjectDisposedException) { disposedRejected = true; }
            Require(disposedRejected, "Disposed pool cannot restart");
            Require(other.Return(foreign), "Other scene owner remains independent");
            Debug.Log("[TemporaryEffectTake] PASS");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [MenuItem("Tools/RPG/Verify Temporary Effect Scene Cleanup (Play Mode)")]
    public static async void VerifySceneCleanup()
    {
        if (!Application.isPlaying) return;
        var scene = UnityEngine.SceneManagement.SceneManager.CreateScene("TemporaryEffectCleanupVerification");
        GameObject template = new GameObject("CleanupTemplate");
        template.SetActive(false);
        try
        {
            var root = new GameObject("SceneOwnedPool");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var pool = root.AddComponent<TemporaryEffectPool>();
            var active = pool.Take(template, Vector3.zero, Quaternion.identity);
            var idle = pool.Take(template, Vector3.zero, Quaternion.identity);
            pool.Return(idle);
            var owner = active.OwnerPool;
            var unloading = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
            while (!unloading.isDone) await System.Threading.Tasks.Task.Yield();
            Require(root == null && active == null && idle == null, "Scene destroys pool root and all instances");
            Require(pool.IsDisposed && owner.IsDisposed && owner.IdleCount == 0, "Scene clears managed records");
            Require(!pool.Return(active), "Late callback to destroyed manager is safe");
            Debug.Log("[TemporaryEffectSceneCleanup] PASS");
        }
        catch (Exception error) { Debug.LogException(error); }
        finally
        {
            if (template != null) UnityEngine.Object.Destroy(template);
            if (scene.IsValid() && scene.isLoaded)
                UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[TemporaryEffectTake] " + message);
    }
}




