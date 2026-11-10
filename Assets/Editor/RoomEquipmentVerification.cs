using System;
using System.Reflection;
using Rpg.Networking;
using Rpg.Networking.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RoomEquipmentVerification
{
    [MenuItem("Tools/RPG/Verify Room Equipment Changes")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        string room = Guid.NewGuid().ToString("N");
        var aId = Identity(room, 1, 101); var bId = Identity(room, 2, 102);
        var authority = new RoomPresentationContext(room, 1, false, aId, bId);
        var display = new RoomPresentationContext(room, 2, true, aId, bId);
        try
        {
            using (var results = new AuthorityResultSequence(room))
            {
                var a = Player(scene, authority, 1, 101, true);
                var b = Player(scene, authority, 2, 102, false);
                var shown = Player(scene, display, 2, 102, false);
                var receipts = new AuthorityResultReceipts(room);
                var before = a.Combat.CaptureRoomConfiguration();
                var request = Request(room, 1, 1, true, RoomEquipmentSlot.Armor, "test-armor-b-v1");
                var wire = (EquipmentChangeRequestDto)ProtocolCodec.Decode((short)ProtocolId.EquipmentChangeRequest,
                    System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(request)), true);
                Require(wire.requestSeq == 1 && wire.configurationId == request.configurationId, "Request protocol round trip");
                var removed = Change(b, wire, 2, results);
                ProtocolCodec.ValidateMessage(removed, true);
                Require(removed.accepted && b.Combat.RoomInventory.Count == 1 && b.Combat.GetEquipment(EquipmentType.Armor) == null,
                    "Unequip returns exactly one item to that player's bag");
                Require(removed.maxHealth == 150 && removed.health == 150 && removed.healthVersion > 1, "Lower maximum clamps HP with a health version");
                Require(a.Combat.RoomInventory.Count == 0 && a.Combat.CaptureRoomConfiguration().stats[8] == before.stats[8], "Other actor is unchanged");
                Require(Change(b, wire, 2, results) == null && b.Combat.RoomInventory.Count == 1, "Duplicate request cannot return a second item");
                var decoded = (EquipmentChangeResultDto)ProtocolCodec.Decode((short)ProtocolId.EquipmentChangeResult,
                    System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(removed)), true);
                Require(receipts.TryAccept(decoded) && Confirm(shown, decoded) && !receipts.TryAccept(decoded), "Result protocol and exact-ID deduplication");
                display.TryGetEntity(102, out var shownView);
                shownView.ApplyVersionedHealth(room, decoded.authorityTick, decoded.healthVersion, decoded.health, decoded.maxHealth);
                Require(shown.Combat.RoomInventory.Count == 1 && shown.stats.GetMaxHealthValue() == 150, "B applies final inventory and health without recalculation");
                var equipped = Change(b, Request(room, 2, 2, false, RoomEquipmentSlot.Armor, "test-armor-b-v1"), 3, results);
                Require(equipped.accepted && b.Combat.RoomInventory.Count == 0 && equipped.maxHealth == 180 && equipped.health == 150,
                    "Equip consumes the owned item and raises max HP without healing");
                Require(Confirm(shown, equipped) && !Confirm(shown, removed) && shown.Combat.RoomInventory.Count == 0, "Old result cannot restore the previous bag");
                var rejected = Change(b, Request(room, 3, 1, true, RoomEquipmentSlot.Armor, "test-armor-b-v1"), 4, results);
                Require(!rejected.accepted && rejected.rejection == EquipmentChangeRejection.StaleInventory && b.Combat.EquipmentVersion == 3,
                    "Stale inventory version is rejected without mutation");
                rejected = Change(b, Request(room, 4, 3, false, RoomEquipmentSlot.Weapon, "test-weapon-a-v1"), 4, results);
                Require(!rejected.accepted && rejected.rejection == EquipmentChangeRejection.MissingItem, "Another player's item cannot be equipped");
                Require(results.LastAllocated == 4 && receipts.TryAccept(equipped), "Rejected results share room IDs; gaps do not discard another result");
                var old = RoomCombatConfiguration.CreateTestProfile(false);
                Require(!(bool)Invoke(shown.Combat, "ApplyConfirmedRoomConfiguration", old, room, 10UL, 100L), "Old baseline cannot replace newer equipment");
                VerifyFullBag(scene, room, authority, results);
                b.stats.currentHealth = 0;
                rejected = Change(b, Request(room, 5, 3, true, RoomEquipmentSlot.Weapon, "test-weapon-b-v1"), 5, results);
                Require(!rejected.accepted && rejected.rejection == EquipmentChangeRejection.PlayerUnavailable, "Zero-health actor cannot equip/unequip");
            }
        }
        finally
        {
            authority.Dispose(); display.Dispose();
            // Inactive test objects may never run OnDestroy; release their transient ScriptableObjects explicitly.
            foreach (var root in scene.GetRootGameObjects())
                foreach (var player in root.GetComponentsInChildren<Player>(true)) Invoke(player.Combat, "ReleaseRoomEquipment");
            EditorSceneManager.ClosePreviewScene(scene);
        }
        Debug.Log("[RoomEquipment] PASS: isolated player bags, ownership/version checks, equip/unequip, duplicate requests/results, HP clamp/no healing, full-bag atomic swap, old baseline protection and protocol round trips. Dual-instance UI acceptance remains manual.");
    }
    private static void VerifyFullBag(Scene scene, string room, RoomPresentationContext context, AuthorityResultSequence results)
    {
        context.TryGetEntity(101, out var view);
        var player = view.View.GetComponent<Player>();
        var profile = RoomCombatConfiguration.CreateTestProfile(true); profile.inventoryCapacity = 4;
        profile.inventory = new RoomEquipmentDto[4];
        for (int i = 0; i < 4; i++)
        {
            profile.inventory[i] = new RoomEquipmentDto { configurationId = "spare-" + i, slot = RoomEquipmentSlot.Weapon, modifiers = new int[14] };
            profile.inventory[i].modifiers[4] = 2;
        }
        Invoke(player.Combat, "InstallRoomConfiguration", profile, false);
        var reject = Change(player, Request(room, 1, 1, true, RoomEquipmentSlot.Armor, "test-armor-a-v1"), 5, results);
        Require(reject.rejection == EquipmentChangeRejection.InventoryFull && player.Combat.GetEquipment(EquipmentType.Armor) != null,
            "Full bag rejects unequip and retains modifiers/item");
        var swap = Change(player, Request(room, 2, 1, false, RoomEquipmentSlot.Weapon, "spare-0"), 5, results);
        Require(swap.accepted && player.Combat.RoomInventory.Count == 4 && player.Combat.GetEquipment(EquipmentType.Weapon).name == "spare-0",
            "Full bag swap succeeds atomically");
        bool returned = false;
        foreach (var item in player.Combat.RoomInventory) if (item.name == "test-weapon-a-v1") returned = true;
        Require(returned && player.Combat.Stats.damage.GetValue() == 13, "Old weapon is retained and modifiers change once");
    }
    private static Player Player(Scene scene, RoomPresentationContext context, int id, int entity, bool host)
    {
        var go = new GameObject("Equipment test player"); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene);
        var player = go.AddComponent<Player>(); var stats = go.AddComponent<PlayerStats>();
        foreach (var field in typeof(CharaterStats).GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (field.FieldType == typeof(Stat)) field.SetValue(stats, new Stat());
        var view = context.RegisterEntity(entity, id, go.transform);
        if (!context.IsGuest) RoomCombatConfiguration.InitializeAuthority(player, host);
        else Invoke(player.Combat, "ApplyConfirmedRoomConfiguration", RoomCombatConfiguration.CreateTestProfile(host), context.RoomSessionId, 1UL, 1L);
        typeof(Entity).GetProperty("stats").GetSetMethod(true).Invoke(player, new object[] { stats });
        typeof(Player).GetProperty("IsRuntimeInitialized").GetSetMethod(true).Invoke(player, new object[] { true });
        typeof(CharaterStats).GetProperty("IsRuntimeInitialized").GetSetMethod(true).Invoke(stats, new object[] { true });
        stats.currentHealth = host ? 140 : 180; Invoke(stats, "EnsureNetworkHealthVersion");
        view.ApplyVersionedHealth(context.RoomSessionId, 1, 1, stats.currentHealth, stats.currentHealth); view.MarkReady();
        return player;
    }
    private static EquipmentChangeRequestDto Request(string room, long seq, long version, bool remove, RoomEquipmentSlot slot, string item) =>
        new EquipmentChangeRequestDto { roomSessionId = room, requestSeq = seq, expectedVersion = version,
            kind = remove ? EquipmentChangeKind.Unequip : EquipmentChangeKind.Equip, slot = slot, configurationId = item };
    private static EquipmentChangeResultDto Change(Player player, EquipmentChangeRequestDto request, ulong tick, AuthorityResultSequence results) =>
        (EquipmentChangeResultDto)Invoke(player.Combat, "ChangeRoomEquipment", request, tick, results);
    private static bool Confirm(Player player, EquipmentChangeResultDto result) => (bool)Invoke(player.Combat, "ApplyEquipmentConfirmation", result);
    private static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
    private static RoomPlayerIdentity Identity(string room, int id, int entity) => (RoomPlayerIdentity)Activator.CreateInstance(typeof(RoomPlayerIdentity),
        BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { room, id, entity }, null);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("[RoomEquipment] FAIL: " + message); }
}
