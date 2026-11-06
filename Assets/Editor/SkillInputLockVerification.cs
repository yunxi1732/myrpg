using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class SkillInputLockVerification
{
    [MenuItem("Tools/RPG/Verify Skill Input Cancellation")]
    public static void Verify()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("[SkillInputLock] 请退出 Play 后运行独立投剑取消检查。");
            return;
        }
        var root = new GameObject("TemporarySwordInputVerification");
        root.SetActive(false); // Never run skill Start/unlock logic.
        var sword = root.AddComponent<SwordSkill>();
        var owner = new object();
        var pending = typeof(SwordSkill).GetField("throwRequested", BindingFlags.Instance | BindingFlags.NonPublic);
        var direction = typeof(SwordSkill).GetField("finalDir", BindingFlags.Instance | BindingFlags.NonPublic);
        try
        {
            sword.cooldownTimer = 7;
            pending.SetValue(sword, true);
            direction.SetValue(sword, new Vector2(5, 3));
            GameplayControlLock.Acquire(owner);
            sword.CancelAim();
            Require(!(bool)pending.GetValue(sword) && (Vector2)direction.GetValue(sword) == Vector2.zero,
                "Cancel clears pending throw and cached direction");
            Require(!sword.RequestThrow(), "Locked release cannot request a throw");
            pending.SetValue(sword, true); // Simulate a release already waiting for an animation event.
            Require(!sword.TryCreateSwordFromInput() && !(bool)pending.GetValue(sword),
                "Late animation event cannot spawn while locked and consumes cancelled request");
            sword.BeginAim();
            Require(!(bool)pending.GetValue(sword), "Locked aim cannot arm a throw");
            GameplayControlLock.Release(owner);
            sword.CancelAim();
            Require(!sword.TryCreateSwordFromInput(), "Cancelled event cannot spawn after unlock either");
            sword.CancelAim();
            Require(sword.cooldownTimer == 7, "Cancellation does not consume or reset cooldown");
            Debug.Log("[SkillInputLock] PASS: cancel, blocked release, stale animation event and cooldown preservation");
        }
        finally
        {
            GameplayControlLock.Release(owner);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[SkillInputLock] FAIL: " + message);
    }
}
