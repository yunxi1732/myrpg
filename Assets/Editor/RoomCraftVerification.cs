using System;
using System.Collections.Generic;
using System.Reflection;
using Rpg.Networking;
using Rpg.Networking.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RoomCraftVerification
{
    [MenuItem("Tools/RPG/Verify Room Crafting")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        var room = Guid.NewGuid().ToString("N");
        var host = Identity(room, 1, 101); var guest = Identity(room, 2, 102);
        var authority = new RoomPresentationContext(room, 1, false, host, guest);
        var display = new RoomPresentationContext(room, 2, true, host, guest);
        var iron = ScriptableObject.CreateInstance<ItemData>(); iron.name = "Craft test iron"; iron.itemName = "Iron";
        var skin = ScriptableObject.CreateInstance<ItemData>(); skin.name = "Craft test skin"; skin.itemName = "Skin";
        var product = ScriptableObject.CreateInstance<ItemData_Equipment>(); product.name = "Craft test sword";
        product.itemName = "Test sword"; product.itemType = ItemType.Equipment; product.damage = 4;
        product.craftingMaterials = new List<InventoryItem> {
            new InventoryItem(iron) { stackSize = 2 }, new InventoryItem(iron) { stackSize = 2 }, new InventoryItem(skin) };
        var starting = new List<ItemData>(); for (int i = 0; i < 10; i++) starting.Add(iron); for (int i = 0; i < 3; i++) starting.Add(skin);
        var profile = RoomCombatConfiguration.CreateTestProfile(false); profile.inventoryCapacity = 4;
        profile.materials = RoomCraftCatalog.StartingMaterials(starting);
        var catalog = new RoomCraftCatalog(new[] { product });
        try
        {
            using (var results = new AuthorityResultSequence(room))
            {
                var a = MakePlayer(scene, authority, 1, 101, profile);
                var b = MakePlayer(scene, authority, 2, 102, profile);
                var shown = MakePlayer(scene, display, 2, 102, profile);
                product.damage = 99999; product.craftingMaterials[0].stackSize = 100;
                var request = Request(room, 1, 1, product);
                var decodedRequest = (CraftRequestDto)ProtocolCodec.Decode((short)ProtocolId.CraftRequest,
                    System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(request)), true);
                var first = Craft(b, decodedRequest, catalog, 2, results);
                ProtocolCodec.ValidateMessage(first, true);
                Require(first.accepted && Count(b, iron) == 6 && Count(b, skin) == 2 && b.Combat.RoomInventory.Count == 1,
                    "A sums duplicate material rows, validates all costs and adds one product");
                Require(b.Combat.RoomInventory[0].damage == 4 && b.Combat.Stats.damage.GetValue() == 28,
                    "Catalog is frozen on A; crafting does not equip or change stats");
                Require(Count(a, iron) == 10 && a.Combat.RoomInventory.Count == 0, "Other player's materials/bag are isolated");
                Require(Craft(b, request, catalog, 2, results) == null && Count(b, iron) == 6, "Repeated request cannot consume or produce again");
                var second = Craft(b, Request(room, 2, 2, product), catalog, 3, results);
                Require(second.accepted && second.productId != first.productId && b.Combat.RoomInventory.Count == 2 && Count(b, iron) == 2,
                    "Separate successful crafts have distinct item identities");
                var receipts = new AuthorityResultReceipts(room);
                var wire = (CraftResultDto)ProtocolCodec.Decode((short)ProtocolId.CraftResult,
                    System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(second)), true);
                Require(receipts.TryAccept(wire) && Confirm(shown, wire) && !receipts.TryAccept(wire), "Result round trip and exact-ID idempotency");
                Require(!Confirm(shown, first) && shown.Combat.RoomInventory.Count == 2 && Count(shown, iron) == 2,
                    "B replaces final bag/materials and refuses older results");
                var rejected = Craft(b, Request(room, 3, 3, product), catalog, 4, results);
                Require(rejected.rejection == CraftRejection.MissingMaterials && Count(b, iron) == 2 && Count(b, skin) == 1 && b.Combat.EquipmentVersion == 3,
                    "Insufficient materials reject without partial deduction or output");
                var stale = Craft(b, Request(room, 4, 1, product), catalog, 4, results);
                Require(stale.rejection == CraftRejection.StaleInventory && receipts.TryAccept(first), "Stale requests reject; result ID gaps never suppress unrelated receipts");
                var unknownRequest = Request(room, 5, 3, product); unknownRequest.recipeId = "recipe/unknown";
                Require(Craft(b, unknownRequest, catalog, 4, results).rejection == CraftRejection.UnknownRecipe, "Client cannot invent a recipe");
                var equip = (EquipmentChangeResultDto)Invoke(b.Combat, "ChangeRoomEquipment", new EquipmentChangeRequestDto {
                    roomSessionId = room, requestSeq = 1, expectedVersion = 3, kind = EquipmentChangeKind.Equip,
                    slot = RoomEquipmentSlot.Weapon, configurationId = first.productId }, 5UL, results);
                Require(equip.accepted && Confirm(shown, equip) && !Confirm(shown, second) &&
                    shown.Combat.GetEquipment(EquipmentType.Weapon).name == first.productId,
                    "Crafted gear uses the existing equipment request path; old craft state cannot undo a later equip");
                var full = RoomCombatConfiguration.CreateTestProfile(false); full.inventoryCapacity = 4;
                full.materials = RoomCraftCatalog.StartingMaterials(starting); full.inventory = new RoomEquipmentDto[4];
                for (int i = 0; i < 4; i++) full.inventory[i] = new RoomEquipmentDto { configurationId = "occupied/" + i, modifiers = new int[14] };
                Invoke(a.Combat, "InstallRoomConfiguration", full, false);
                rejected = Craft(a, Request(room, 1, 1, product), catalog, 6, results);
                Require(rejected.rejection == CraftRejection.InventoryFull && Count(a, iron) == 10 && a.Combat.RoomInventory.Count == 4,
                    "Full bag preserves every material and existing item");
                b.stats.currentHealth = 0;
                Require(Craft(b, Request(room, 6, 4, product), catalog, 6, results).rejection == CraftRejection.PlayerUnavailable,
                    "Zero-health players cannot craft");
                var invalid = Request(Guid.NewGuid().ToString("N"), 7, 4, product);
                Require(Craft(b, invalid, catalog, 6, results) == null, "Wrong-session request is ignored");
                // A's real authored catalog must be buildable without enabling a scene or local inventory.
                var authored = new List<ItemData_Equipment>();
                foreach (var guid in AssetDatabase.FindAssets("t:ItemData_Equipment", new[] { "Assets/Data/Items" }))
                    authored.Add(AssetDatabase.LoadAssetAtPath<ItemData_Equipment>(AssetDatabase.GUIDToAssetPath(guid)));
                new RoomCraftCatalog(authored);
            }
        }
        finally
        {
            authority.Dispose(); display.Dispose();
            foreach (var root in scene.GetRootGameObjects()) foreach (var player in root.GetComponentsInChildren<Player>(true)) Invoke(player.Combat, "ReleaseRoomEquipment");
            EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Object.DestroyImmediate(product); UnityEngine.Object.DestroyImmediate(iron); UnityEngine.Object.DestroyImmediate(skin);
        }
        Debug.Log("[RoomCraft] PASS: A-owned frozen recipes, summed material costs, atomic deduction/output, per-player isolation, repeat crafts/requests/results, missing/full/stale/unknown/dead/session rejection, equipment cross-stream ordering and protocol. B-key/C-key dual-instance acceptance remains manual.");
    }
    private static CraftRequestDto Request(string room, long seq, long version, ItemData_Equipment recipe) => new CraftRequestDto {
        roomSessionId = room, requestSeq = seq, expectedVersion = version, recipeId = RoomCraftCatalog.RecipeId(recipe) };
    private static CraftResultDto Craft(Player player, CraftRequestDto request, RoomCraftCatalog catalog, ulong tick, AuthorityResultSequence results) =>
        (CraftResultDto)Invoke(player.Combat, "CraftRoomItem", request, catalog, tick, results);
    private static bool Confirm(Player player, RoomInventoryResultDto result) => (bool)Invoke(player.Combat, "ApplyInventoryConfirmation", result);
    private static int Count(Player player, ItemData material)
    { foreach (var value in player.Combat.CaptureRoomMaterials()) if (value.materialId == RoomCraftCatalog.MaterialId(material)) return value.count; return 0; }
    private static Player MakePlayer(Scene scene, RoomPresentationContext context, int id, int entityId, PlayerCombatDto profile)
    {
        var go = new GameObject("Craft test actor"); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene);
        var player = go.AddComponent<Player>(); var stats = go.AddComponent<PlayerStats>();
        foreach (var field in typeof(CharaterStats).GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (field.FieldType == typeof(Stat)) field.SetValue(stats, new Stat());
        var view = context.RegisterEntity(entityId, id, go.transform);
        Invoke(player.Combat, "InstallRoomConfiguration", profile, context.IsGuest);
        typeof(Entity).GetProperty("stats").GetSetMethod(true).Invoke(player, new object[] { stats });
        typeof(Player).GetProperty("IsRuntimeInitialized").GetSetMethod(true).Invoke(player, new object[] { true });
        typeof(CharaterStats).GetProperty("IsRuntimeInitialized").GetSetMethod(true).Invoke(stats, new object[] { true });
        stats.currentHealth = 180; Invoke(stats, "EnsureNetworkHealthVersion");
        view.ApplyVersionedHealth(context.RoomSessionId, 1, 1, 180, 180); view.MarkReady(); return player;
    }
    private static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static RoomPlayerIdentity Identity(string room, int player, int entity) => (RoomPlayerIdentity)Activator.CreateInstance(typeof(RoomPlayerIdentity),
        BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { room, player, entity }, null);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("[RoomCraft] FAIL: " + message); }
}
