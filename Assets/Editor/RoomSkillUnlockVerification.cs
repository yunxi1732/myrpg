using System;
using System.Collections.Generic;
using System.Reflection;
using Rpg.Networking;
using Rpg.Networking.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class RoomSkillUnlockVerification
{
    [MenuItem("Tools/RPG/Verify Room Skill Unlocks")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
        var scene = EditorSceneManager.NewPreviewScene(); var room = Guid.NewGuid().ToString("N");
        var host = Identity(room, 1, 101); var guest = Identity(room, 2, 102);
        var authority = new RoomPresentationContext(room, 1, false, host, guest);
        var display = new RoomPresentationContext(room, 2, true, host, guest);
        try
        {
            var parry = Node(scene, "Parry", 7);
            var upgrade = Node(scene, "Upgrade", 5); var alternative = Node(scene, "Alternative", 3);
            var expensive = Node(scene, "Expensive", 99);
            Field(upgrade, "shouldBeUnlocked", new[] { parry }); Field(upgrade, "shouldBeLocked", new[] { alternative });
            Field(alternative, "shouldBeUnlocked", new[] { parry }); Field(alternative, "shouldBeLocked", new[] { upgrade });
            var catalog = new RoomSkillTreeCatalog(new[] { parry, upgrade, alternative, expensive });
            var a = MakePlayer(scene, authority, 1, 101); var b = MakePlayer(scene, authority, 2, 102);
            MakePlayer(scene, display, 1, 101);
            var shown = MakePlayer(scene, display, 2, 102);
            Invoke(a.Combat, "InitializeRoomSkillTree", catalog, 20); Invoke(b.Combat, "InitializeRoomSkillTree", catalog, 20);
            // The display receives a complete baseline, including independent tree state and currency.
            Invoke(shown.Combat, "InstallRoomConfiguration", b.Combat.CaptureRoomConfiguration(), true);
            display.TryGetEntity(102, out var view);
            Invoke(view, "ApplySkillTreePresentation", shown.Combat.CaptureRoomSkillTree(), 1UL);
            Field(parry, "skillCost", 0); parry.unlocked = true; // Client/UI edits cannot change A's frozen cost or unlock conditions.
            using (var sequence = new AuthorityResultSequence(room))
            {
                var oldInventory = (EquipmentChangeResultDto)Invoke(b.Combat, "ChangeRoomEquipment", new EquipmentChangeRequestDto {
                    roomSessionId = room, requestSeq = 1, expectedVersion = 1, kind = EquipmentChangeKind.Unequip,
                    slot = RoomEquipmentSlot.Armor, configurationId = "test-armor-b-v1" }, 2UL, sequence);
                Require(oldInventory.accepted, "Prepare an actual older equipment result for cross-stream ordering");
                Require(b.Combat.Skills.Commit<DashSkill>(), "Start an independent successful dash cooldown");
                float remaining = b.Combat.Skills.Get(RoomSkillId.Dash).Remaining;
                var rejected = Unlock(b, Request(room, 1, 1, "Upgrade"), catalog, 3, sequence);
                Require(rejected.rejection == SkillUnlockRejection.Prerequisite && b.Combat.RoomCurrency == 20, "Missing prerequisite rejects without cost");
                var request = Request(room, 2, 1, "Parry");
                var decoded = (SkillUnlockRequestDto)ProtocolCodec.Decode((short)ProtocolId.SkillUnlockRequest,
                    System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(request)), true);
                var first = Unlock(b, decoded, catalog, 4, sequence);
                Require(first.accepted && b.Combat.RoomCurrency == 13 && b.Combat.IsRoomNodeUnlocked("Parry") && b.Combat.Skills.Get(RoomSkillId.Parry).Unlocked,
                    "A uses frozen cost and commits currency/node/base-skill permission together");
                Require(a.Combat.RoomCurrency == 20 && !a.Combat.IsRoomNodeUnlocked("Parry") && !a.Combat.Skills.Get(RoomSkillId.Parry).Unlocked,
                    "Other player's currency and unlocks are independent");
                Require(Unlock(b, request, catalog, 4, sequence) == null && b.Combat.RoomCurrency == 13, "Duplicate request never charges twice");
                Require(Unlock(b, Request(room, 3, 2, "Parry"), catalog, 4, sequence).rejection == SkillUnlockRejection.AlreadyUnlocked,
                    "A new request for an already-unlocked node does not charge");
                var second = Unlock(b, Request(room, 4, 2, "Upgrade"), catalog, 5, sequence);
                Require(second.accepted && b.Combat.RoomCurrency == 8 && b.Combat.SkillTreeVersion == 3, "Valid child unlock uses its own cost");
                Require(Unlock(b, Request(room, 5, 3, "Alternative"), catalog, 5, sequence).rejection == SkillUnlockRejection.Excluded,
                    "Mutually exclusive node rejects");
                Require(Unlock(b, Request(room, 6, 3, "Expensive"), catalog, 5, sequence).rejection == SkillUnlockRejection.InsufficientCurrency,
                    "Insufficient currency rejects without changing state");
                Require(Unlock(b, Request(room, 7, 1, "Expensive"), catalog, 5, sequence).rejection == SkillUnlockRejection.StaleState,
                    "Stale state cannot spend against an old balance");
                Require(Unlock(b, Request(room, 8, 3, "unknown"), catalog, 5, sequence).rejection == SkillUnlockRejection.UnknownNode,
                    "Client cannot supply an unknown node or its own price");
                Require(b.Combat.Skills.Get(RoomSkillId.Dash).Remaining == remaining && b.Combat.Skills.Get(RoomSkillId.Parry).Remaining == 0,
                    "Unlocks and rejections do not reset/start cooldowns");
                var wire = (SkillUnlockResultDto)ProtocolCodec.Decode((short)ProtocolId.SkillUnlockResult,
                    System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(second)), true);
                var receipts = new AuthorityResultReceipts(room);
                Require(receipts.TryAccept(wire) && Confirm(shown, wire) && !receipts.TryAccept(wire) && !Confirm(shown, first),
                    "Result protocol, exact receipt deduplication and per-target version/tick sorting");
                Require(receipts.TryAccept(oldInventory) && (bool)Invoke(shown.Combat, "ApplyInventoryConfirmation", oldInventory) &&
                    shown.Combat.SkillTreeVersion == 3 && shown.Combat.RoomCurrency == 8 && shown.Combat.Skills.Get(RoomSkillId.Parry).Unlocked,
                    "Older equipment state can apply independently without undoing a newer skill unlock");
                Require(shown.Combat.RoomInventory.Count == 1 && shown.Combat.GetEquipment(EquipmentType.Armor) == null,
                    "Unlock confirmations never restore or overwrite equipment/materials");
                Invoke(view, "ApplySkillTreePresentation", wire.state, wire.authorityTick);
                Invoke(view, "ApplySkillTreePresentation", oldInventory.combat.skillTree, oldInventory.authorityTick);
                view.ApplyHud(room, 10, 100, 20, null, 0, new float[5]);
                Require(view.Currency == 8, "Old baseline/HUD currency cannot restore spent currency");
                var detached = shown.Combat.CaptureRoomSkillTree(); detached.currency = 999; detached.nodes[0].unlocked = !detached.nodes[0].unlocked;
                Require(shown.Combat.RoomCurrency == 8, "Received/captured state does not alias the actor's mutable state");
                parry.BindNetwork(view); Require(parry.unlocked && parry.GetComponent<Button>().interactable, "Existing slot displays local confirmed unlock");
                display.TryGetEntity(101, out var other);
                parry.BindNetwork(other); Require(!parry.unlocked && !parry.GetComponent<Button>().interactable, "Remote actor binding is refused");
                parry.BindNetwork(null); Require(!parry.unlocked, "Close/unbind clears UI without solo fallback");
                b.stats.currentHealth = 0;
                Require(Unlock(b, Request(room, 9, 3, "Expensive"), catalog, 6, sequence).rejection == SkillUnlockRejection.PlayerUnavailable,
                    "Zero-health player cannot unlock");
                Require(Unlock(b, Request(Guid.NewGuid().ToString("N"), 10, 3, "Parry"), catalog, 6, sequence) == null,
                    "Old/wrong session cannot change unlocks");
                Require(receipts.TryAccept(first), "Sparse room result IDs do not drop other result streams");
            }
        }
        finally
        {
            authority.Dispose(); display.Dispose();
            foreach (var root in scene.GetRootGameObjects()) foreach (var player in root.GetComponentsInChildren<Player>(true)) Invoke(player.Combat, "ReleaseRoomEquipment");
            EditorSceneManager.ClosePreviewScene(scene);
        }
        Debug.Log("[RoomSkillUnlock] PASS: frozen requirements/costs, player isolation, atomic currency/unlock, prerequisites/exclusions/insufficient funds, request/result dedup, old state/HUD/equipment isolation, unchanged cooldowns and local UI binding. K-key/dual-instance acceptance remains manual.");
    }
    private static SkillUnlockRequestDto Request(string room, long seq, long version, string node) => new SkillUnlockRequestDto {
        roomSessionId = room, requestSeq = seq, expectedVersion = version, nodeId = node };
    private static SkillUnlockResultDto Unlock(Player player, SkillUnlockRequestDto request, RoomSkillTreeCatalog catalog, ulong tick, AuthorityResultSequence sequence) =>
        (SkillUnlockResultDto)Invoke(player.Combat, "UnlockRoomSkill", request, catalog, tick, sequence);
    private static bool Confirm(Player player, SkillUnlockResultDto result) => (bool)Invoke(player.Combat, "ApplyRoomSkillTreeSnapshot", result.state, result.authorityTick);
    private static UI_SkillTreeSlot Node(Scene scene, string id, int cost)
    {
        var go = new GameObject("Tree node", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button)); go.SetActive(false);
        SceneManager.MoveGameObjectToScene(go, scene); var slot = go.AddComponent<UI_SkillTreeSlot>();
        Field(slot, "skillName", id); Field(slot, "skillCost", cost); return slot;
    }
    private static Player MakePlayer(Scene scene, RoomPresentationContext context, int id, int entity)
    {
        var go = new GameObject("Unlock test actor"); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene);
        var player = go.AddComponent<Player>(); var stats = go.AddComponent<PlayerStats>();
        foreach (var field in typeof(CharaterStats).GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (field.FieldType == typeof(Stat)) field.SetValue(stats, new Stat());
        var view = context.RegisterEntity(entity, id, go.transform);
        Invoke(player.Combat, "InstallRoomConfiguration", RoomCombatConfiguration.CreateTestProfile(false), context.IsGuest);
        typeof(Entity).GetProperty("stats").GetSetMethod(true).Invoke(player, new object[] { stats });
        typeof(Player).GetProperty("IsRuntimeInitialized").GetSetMethod(true).Invoke(player, new object[] { true });
        typeof(CharaterStats).GetProperty("IsRuntimeInitialized").GetSetMethod(true).Invoke(stats, new object[] { true });
        stats.currentHealth = 180; Invoke(stats, "EnsureNetworkHealthVersion");
        view.ApplyVersionedHealth(context.RoomSessionId, 1, 1, 180, 180); view.MarkReady(); return player;
    }
    private static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static void Field(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static RoomPlayerIdentity Identity(string room, int player, int entity) => (RoomPlayerIdentity)Activator.CreateInstance(typeof(RoomPlayerIdentity),
        BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { room, player, entity }, null);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("[RoomSkillUnlock] FAIL: " + message); }
}
