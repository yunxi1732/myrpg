using System;
using UnityEditor;
using UnityEngine;

public static class GameplayControlLockVerification
{
    [MenuItem("Tools/RPG/Verify Action Closing Frame (Play Mode)")]
    public static void VerifyClosingFrame()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ActionClosingFrame] 请进入 Play 模式验证跨帧恢复。");
            return;
        }
        int closingFrame = Time.frameCount;
        GameplayControlLock.SuppressActionInputForCurrentFrame();
        Require(GameplayControlLock.IsActionInputBlocked, "Closing frame blocks action input");
        EditorApplication.CallbackFunction check = null;
        check = () =>
        {
            if (!Application.isPlaying)
            {
                EditorApplication.update -= check;
                return;
            }
            if (Time.frameCount == closingFrame) return;
            EditorApplication.update -= check;
            Require(GameplayControlLock.IsActionInputBlocked == GameplayControlLock.IsLocked,
                "Later frames depend only on remaining lock owners");
            Debug.Log("[ActionClosingFrame] PASS: current frame blocked, next frame restores owner-based state");
        };
        EditorApplication.update += check;
    }

    // Deliberately equal by value: the lock must still distinguish these two references.
    private sealed class Owner
    {
        public override bool Equals(object obj) => obj is Owner;
        public override int GetHashCode() => 1;
    }

    [MenuItem("Tools/RPG/Verify Gameplay Control Lock")]
    public static void Verify()
    {
        var menu = new Owner();
        var dialogue = new Owner();
        var unknown = new Owner();
        int baseline = GameplayControlLock.HolderCount;
        bool wasLocked = GameplayControlLock.IsLocked;
        try
        {
            Require(!GameplayControlLock.Acquire(null) && !GameplayControlLock.Release(null) &&
                GameplayControlLock.HolderCount == baseline, "Null cannot acquire or release");
            Require(GameplayControlLock.Acquire(menu) && GameplayControlLock.IsLocked &&
                GameplayControlLock.HolderCount == baseline + 1, "First owner locks input");
            Require(!GameplayControlLock.Acquire(menu) && GameplayControlLock.HolderCount == baseline + 1,
                "Duplicate acquire does not stack");
            Require(GameplayControlLock.Acquire(dialogue) && GameplayControlLock.HolderCount == baseline + 2,
                "Equal values with distinct references hold independent locks");
            Require(!GameplayControlLock.Release(unknown) && GameplayControlLock.HolderCount == baseline + 2,
                "Unknown owner cannot release someone else's lock");
            Require(GameplayControlLock.Release(menu) && GameplayControlLock.IsLocked &&
                GameplayControlLock.HolderCount == baseline + 1, "Closing menu preserves dialogue lock");
            Require(!GameplayControlLock.Release(menu) && GameplayControlLock.IsLocked,
                "Repeated menu release preserves remaining locks");
            Require(GameplayControlLock.Release(dialogue) && GameplayControlLock.HolderCount == baseline &&
                GameplayControlLock.IsLocked == wasLocked, "Last test owner restores prior lock state");
            Require(!GameplayControlLock.Release(dialogue), "Repeated final release is safe");
            Require(GameplayControlLock.Acquire(menu) && GameplayControlLock.Release(menu) &&
                GameplayControlLock.HolderCount == baseline, "Released owner can acquire again");
            Debug.Log("[GameplayControlLock] PASS: owner identity, overlapping locks and idempotent acquire/release");
        }
        finally
        {
            GameplayControlLock.Release(menu);
            GameplayControlLock.Release(dialogue);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[GameplayControlLock] FAIL: " + message);
    }
}
