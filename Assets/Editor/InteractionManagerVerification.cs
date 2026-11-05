using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class InteractionManagerVerification
{
    [MenuItem("Tools/RPG/Verify Interaction Selection")]
    public static void VerifySelection()
    {
        var manager = new InteractionManager();
        var owner = new object();
        int executed = 0, changes = 0;
        manager.OnSelectionChanged += () => changes++;
        Require(manager.SelectedIndex == -1 && manager.GetSelected() == null &&
            !manager.MoveSelection(1) && !manager.TryExecuteSelected(), "Empty list cannot select or execute");
        var a = new InteractionOptionData(owner, "a", "A", () => executed++);
        var b = new InteractionOptionData(owner, "b", "B", () => executed += 10);
        var c = new InteractionOptionData(new object(), "c", "C", null);
        manager.Register(a);
        manager.Register(b);
        manager.Register(c);
        Require(manager.GetSelected() == a && changes == 1 && a.key == KeyCode.E && a.keyTip == "E",
            "First candidate selected; default confirm avoids F skill");
        manager.MoveSelection(-1);
        Require(manager.GetSelected() == c && !manager.TryExecuteSelected(), "Previous wraps; null action is safe");
        manager.MoveSelection(1);
        Require(manager.GetSelected() == a, "Next wraps");
        manager.MoveSelection(1);
        Require(manager.TryExecuteSelected() && executed == 10, "Only current candidate executes");
        int before = changes;
        manager.Register(b);
        Require(changes == before, "Duplicate registration preserves selection notification count");
        manager.Unregister(a);
        Require(manager.GetSelected() == b && manager.SelectedIndex == 0, "Removal before selection preserves object");
        manager.Unregister(b);
        Require(manager.GetSelected() == c && manager.SelectedIndex == 0, "Removed selection chooses valid neighbor");
        before = changes;
        Require(!manager.MoveSelection(1) && changes == before, "One candidate cannot produce fake selection changes");
        manager.Clear();
        Require(manager.SelectedIndex == -1 && !manager.TryExecuteSelected(), "Clear resets selection");

        manager.Register(a);
        manager.Register(b);
        manager.Register(c);
        manager.MoveSelection(1);
        manager.MoveSelection(1);
        manager.Unregister(c);
        Require(manager.GetSelected() == b && manager.SelectedIndex == 1, "Removing last selected item clamps to last index");
        manager.UnregisterOwner(owner);
        Require(manager.SelectedIndex == -1 && manager.GetSelected() == null, "Owner removal can empty selection");

        var selfRemoving = new InteractionOptionData(owner, "self", "自移除", () =>
        {
            Require(!manager.TryExecuteSelected(), "Recursive execution is blocked");
            manager.UnregisterOwner(owner);
        });
        manager.Register(selfRemoving);
        Require(manager.TryExecuteSelected() && manager.Options.Count == 0, "Callback may remove its own candidate safely");
        var throwing = new InteractionOptionData(owner, "throw", "失败", () => { throw new InvalidOperationException("expected"); });
        manager.Register(throwing);
        bool propagated = false;
        try { manager.TryExecuteSelected(); }
        catch (InvalidOperationException error) { propagated = error.Message == "expected"; }
        manager.Clear();
        manager.Register(a);
        Require(propagated && manager.TryExecuteSelected() && executed == 11, "Execution guard resets after exception");
        manager.Clear();
        Debug.Log("[InteractionSelection] PASS: wrap, execution, removal correction, empty list and reentrancy");
    }

    // Equal values must still represent distinct scene owners.
    private sealed class Owner
    {
        public override bool Equals(object obj) => obj is Owner;
        public override int GetHashCode() => 1;
    }

    [MenuItem("Tools/RPG/Verify Interaction Candidates")]
    public static void Verify()
    {
        var manager = new InteractionManager();
        var a = new Owner();
        var b = new Owner();
        int executed = 0, notifications = 0;
        var observedCounts = new List<int>();
        Action changed = () => { notifications++; observedCounts.Add(manager.Options.Count); };
        manager.OnListChanged += changed;
        try
        {
            var talk = new InteractionOptionData(a, "talk", "对话", () => executed++, "E", KeyCode.E);
            var duplicate = new InteractionOptionData(a, "talk", "重复对话", () => executed += 100);
            var trade = new InteractionOptionData(a, "trade", "交易", null);
            var otherTalk = new InteractionOptionData(b, "talk", "另一个NPC对话", null);
            Require(!manager.Register(null) && notifications == 0, "Null registration is ignored");
            Require(manager.Register(talk) && notifications == 1 && ReferenceEquals(manager.Options[0], talk),
                "Registration retains data and notifies once");
            Require(!manager.Register(talk) && !manager.Register(duplicate) && notifications == 1,
                "Same object and same owner/ID deduplicate without notification");
            Require(manager.Register(trade) && manager.Register(otherTalk) && notifications == 3,
                "Different IDs or owner references remain separate");
            Require(ReferenceEquals(manager.Options[0], talk) && ReferenceEquals(manager.Options[1], trade) &&
                ReferenceEquals(manager.Options[2], otherTalk), "Insertion order is preserved");
            Require(talk.id == "talk" && talk.displayName == "对话" && talk.keyTip == "E" && talk.key == KeyCode.E,
                "ID, display and key metadata are preserved");
            manager.Options[0].onExecute?.Invoke();
            Require(executed == 1, "Duplicate cannot replace original callback");
            Require(!manager.Unregister(duplicate) && !manager.Unregister(null) && notifications == 3,
                "Stale/unknown removal is a no-op");
            bool readOnly = false;
            try { ((IList<InteractionOptionData>)manager.Options).Clear(); }
            catch (NotSupportedException) { readOnly = true; }
            Require(readOnly && manager.Options.Count == 3, "Public list cannot bypass change notification");
            Require(manager.UnregisterOwner(a) == 2 && notifications == 4 &&
                ReferenceEquals(manager.Options[0], otherTalk), "Owner removal is atomic and uses reference identity");
            Require(manager.UnregisterOwner(a) == 0 && manager.UnregisterOwner(null) == 0 && notifications == 4,
                "Repeated owner removal is a no-op");
            Require(manager.Unregister(otherTalk) && notifications == 5 && manager.Options.Count == 0,
                "Individual removal notifies after mutation");
            Require(!manager.Clear() && notifications == 5, "Empty clear is a no-op");
            manager.Register(talk);
            manager.Register(trade);
            Require(manager.Clear() && notifications == 8 && manager.Options.Count == 0,
                "Clear notifies once for multiple candidates");
            Require(string.Join(",", observedCounts) == "1,2,3,1,0,1,2,0", "Observers see completed mutations");
            Require(new InteractionManager().Options.Count == 0, "Instances do not share state");
            bool invalidId = false, invalidOwner = false;
            try { new InteractionOptionData(a, " ", "", null); }
            catch (ArgumentException) { invalidId = true; }
            try { new InteractionOptionData(null, "talk", "", null); }
            catch (ArgumentNullException) { invalidOwner = true; }
            Require(invalidId && invalidOwner, "Invalid identity fails explicitly");
            manager.OnListChanged -= changed;
            manager.Register(talk);
            Require(notifications == 8, "Unsubscribed listener receives no changes");
            Debug.Log("[InteractionCandidates] PASS: identity, deduplication, removal, callbacks and notifications");
        }
        finally
        {
            manager.OnListChanged -= changed;
            manager.Clear();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[InteractionCandidates] FAIL: " + message);
    }
}
