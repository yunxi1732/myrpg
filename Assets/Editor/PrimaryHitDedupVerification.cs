using System;
using Rpg.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PrimaryHitDedupVerification
{
    [MenuItem("Tools/RPG/Verify Primary Hit Dedup and Combo (4-33b)")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Controllers/Player_AC.controller");
            Require(controller != null, "Use the actual player animator controller");
            var value = new GameObject("Combo timing probe"); value.SetActive(false); SceneManager.MoveGameObjectToScene(value, scene);
            var animator = value.AddComponent<Animator>(); animator.runtimeAnimatorController = controller;
            for (int i = 0; i < 3; i++)
                Require(PlayerPrimaryAttack.TryNetworkTiming(animator, i, out var timing) && timing.ComboIndex == i &&
                    timing.IsValid && timing.Segments.Count == 1, "Each existing strike retains its own authored single hit marker: " + (i + 1));
            VerifyMarkerRules();
            VerifyLedgerAndCombo();
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        Debug.Log("[PrimaryHitDedup 4-33b] PASS: multi-collider/sample and segment/target/instance isolation, combo 1/2/3/reset, rejection/interrupt/timeout and independent actors/sessions. No damage or resultId produced.");
    }

    private static void VerifyMarkerRules()
    {
        Require(PlayerPrimaryAttack.TryCreateNetworkTiming(0, 1, 5, .02f, new[] { .3f, .2f }, out var timing) &&
            timing.Segments.Count == 2 && timing.Segments[0].EndOffsetTicks == timing.Segments[1].StartOffsetTicks,
            "Markers sort by authored time; an earlier window ends at the next segment's opening");
        Require(!PlayerPrimaryAttack.TryCreateNetworkTiming(0, 1, 14, .02f, new[] { .2f, .2f }, out _), "Duplicate authored markers are invalid");
        Require(!PlayerPrimaryAttack.TryCreateNetworkTiming(0, 1, 14, .02f, new[] { .201f, .202f }, out _), "Distinct markers collapsing to one authority tick are invalid");
        Require(!PlayerPrimaryAttack.TryCreateNetworkTiming(0, 1, 14, .02f, new[] { 1f }, out _) &&
            !PlayerPrimaryAttack.TryCreateNetworkTiming(0, 1, 14, .02f, new[] { float.NaN }, out _), "End/out-of-range/NaN markers are invalid");
        var source = new[] { new PrimaryHitSegmentTiming(5, 9), new PrimaryHitSegmentTiming(15, 20) };
        var frozen = new PrimaryAttackTiming(0, 30, source);
        source[0] = new PrimaryHitSegmentTiming(0, 30);
        Require(frozen.IsValid && frozen.Segments[0].StartOffsetTicks == 5, "Authored segment configuration is copied, not shared mutable state");
    }

    private static void VerifyLedgerAndCombo()
    {
        const int window = 75;
        string room = Guid.NewGuid().ToString("N"); long instanceId = 0;
        var a = new PrimaryAttackRuntime(room, 1, 101, () => ++instanceId);
        var b = new PrimaryAttackRuntime(room, 2, 102, () => ++instanceId);
        var first = Begin(a, 100, 10, 0);
        Require(first.RequestSeq == 100 && first.AttackInstanceId == 1 && first.ComboIndex == 0 && first.Segments.Count == 2,
            "Request sequence, instance ID, combo strike and hit segment are separate values");
        var one = first.Segments[0]; var two = first.Segments[1];
        Require(!a.TryRegisterHit(first, 1, 201, one.StartTick - 1), "No hit before window opening");
        Require(a.TryRegisterHit(first, 1, 201, one.StartTick), "First target admission");
        Require(!a.TryRegisterHit(first, 1, 201, one.StartTick) && !a.TryRegisterHit(first, 1, 201, one.StartTick + 1),
            "Same-tick child colliders and later physics samples cannot hit the same target twice");
        Require(a.TryRegisterHit(first, 1, 202, one.StartTick), "A different target in the same segment is independent");
        Require(!a.TryRegisterHit(first, 1, 201, one.EndTick) && !a.TryRegisterHit(first, 2, 201, one.StartTick) &&
            !a.TryRegisterHit(first, 0, 201, one.StartTick) && !a.TryRegisterHit(first, 1, 0, one.StartTick), "Wrong segment/window/target cannot admit hits");
        Require(a.TryRegisterHit(first, 2, 201, two.StartTick) && !a.TryRegisterHit(first, 2, 201, two.EndTick - 1),
            "A declared second hit segment can hit the same target once again");
        Require(first.AcceptedHitCount == 3 && !b.TryRegisterHit(first, 1, 201, one.StartTick), "Actor ledgers do not mix");
        a.FinishCurrent(first, first.EndTick, true);
        Require(first.IsClosed && !a.TryRegisterHit(first, 1, 201, one.StartTick), "Closed attack rejects even an old tick replay");
        Require(a.PeekComboIndex(first.EndTick + window, window) == 1 && a.PeekComboIndex(first.EndTick + window + 1, window) == 0,
            "Combo continuation includes exactly 1.5 seconds from completion; later presses reset to strike one");
        Require(!a.TryQueue(101, first.EndTick, PrimaryAttackRejection.ActionState) && a.PeekComboIndex(first.EndTick, window) == 1,
            "Rejected request does not advance the next combo strike");
        Require(a.TryQueue(102, first.EndTick + 1, PrimaryAttackRejection.None) &&
            !a.TryBegin(first.EndTick + 2, PrimaryAttackRejection.Dead, Timing(1), 40, window) &&
            instanceId == 1 && a.PeekComboIndex(first.EndTick + 2, window) == 1, "Execution rejection neither allocates an instance nor advances combo");
        var second = Begin(a, 103, first.EndTick + 3, 1);
        Require(a.TryRegisterHit(second, 1, 201, second.Segments[0].StartTick) &&
            !a.TryRegisterHit(first, 2, 203, two.StartTick), "New instance admits the same target; old instance cannot re-enter");
        var guest = Begin(b, 1, second.StartTick, 0);
        Require(guest.ComboIndex == 0 && b.TryRegisterHit(guest, 1, 201, guest.Segments[0].StartTick), "B's combo and target hits are independent from A");
        a.FinishCurrent(second, second.EndTick, true);
        var third = Begin(a, 104, second.EndTick + 1, 2);
        a.FinishCurrent(third, third.EndTick, true);
        Require(a.PeekComboIndex(third.EndTick, window) == 0, "After 1/2/3 the next strike is one");
        var fourth = Begin(a, 105, third.EndTick + 1, 0);
        ulong cooldown = a.NextAllowedTick;
        a.FinishCurrent(fourth, fourth.StartTick + 1, false);
        Require(fourth.IsClosed && a.PeekComboIndex(fourth.EndTick, window) == 0 && a.NextAllowedTick == cooldown &&
            !a.TryRegisterHit(fourth, 1, 201, fourth.Segments[0].StartTick), "Interrupted attack closes hit admission and resets combo without refreshing successful cooldown");
        var fifth = Begin(a, 106, cooldown, 0);
        a.FinishCurrent(fifth, fifth.EndTick, true);
        var afterTimeout = Begin(a, 107, fifth.EndTick + window + 1, 0);
        Require(afterTimeout.ComboIndex == 0, "Timeout resets the actual next execution, not only a preview of its index");
        a.InterruptCurrent(); a.InterruptCurrent();
        Require(afterTimeout.IsClosed && !a.HasPending && a.PeekComboIndex(afterTimeout.EndTick, window) == 0, "Disable/death/leave cleanup is idempotent");
        var replacement = new PrimaryAttackRuntime(room, 3, 103, () => ++instanceId);
        Require(replacement.PeekComboIndex(1000, window) == 0 && replacement.LastRequestSeq == 0 && replacement.NextAllowedTick == 0,
            "Replacement guest inherits no request, cooldown, combo or hit history");
        var otherRoom = new PrimaryAttackRuntime(Guid.NewGuid().ToString("N"), 1, 101, () => 1);
        var other = Begin(otherRoom, 1, 10, 0);
        Require(other.AttackInstanceId == first.AttackInstanceId && otherRoom.TryRegisterHit(other, 1, 201, other.Segments[0].StartTick) &&
            !otherRoom.TryRegisterHit(first, 1, 201, one.StartTick), "Reused numeric IDs in another session never share hit history or accept old instances");
    }

    private static PrimaryAttackTiming Timing(int combo) => new PrimaryAttackTiming(combo, 40,
        new PrimaryHitSegmentTiming(5, 9), new PrimaryHitSegmentTiming(15, 20));
    private static PrimaryAttackInstance Begin(PrimaryAttackRuntime runtime, long requestSeq, ulong tick, int combo)
    {
        Require(runtime.TryQueue(requestSeq, tick, PrimaryAttackRejection.None) &&
            runtime.TryBegin(tick, PrimaryAttackRejection.None, Timing(combo), 40, 75), "Begin combo strike " + (combo + 1));
        return runtime.Current;
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("[PrimaryHitDedup 4-33b] FAIL: " + message); }
}
