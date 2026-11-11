using System.Collections.Generic;
using Rpg.Networking;
using Rpg.Networking.Protocol;

public sealed partial class PlayerCombatContext
{
    private readonly Dictionary<string, RoomMaterialDto> roomMaterials = new Dictionary<string, RoomMaterialDto>();
    private long craftRequestSeq;
    public RoomMaterialDto[] CaptureRoomMaterials()
    {
        var result = new List<RoomMaterialDto>();
        foreach (var value in roomMaterials.Values)
            result.Add(new RoomMaterialDto { materialId = value.materialId, displayName = value.displayName, count = value.count });
        result.Sort((a, b) => string.CompareOrdinal(a.materialId, b.materialId));
        return result.ToArray();
    }
    internal CraftResultDto CraftRoomItem(CraftRequestDto request, RoomCraftCatalog catalog, ulong tick, AuthorityResultSequence results)
    {
        if (!ProtocolCodec.ValidCraftRequest(request) ||
            request.roomSessionId != RoomSessionId || request.requestSeq <= craftRequestSeq) return null;
        craftRequestSeq = request.requestSeq;
        var rejection = CraftRejection.None;
        RoomCraftCatalog.Recipe recipe = null;
        if (!Owner.IsRuntimeInitialized || !Stats.IsRuntimeInitialized || Stats.isDead || Stats.currentHealth <= 0) rejection = CraftRejection.PlayerUnavailable;
        else if (request.expectedVersion != EquipmentVersion) rejection = CraftRejection.StaleInventory;
        else if (!catalog.TryGet(request.recipeId, out recipe)) rejection = CraftRejection.UnknownRecipe;
        else if (roomInventory.Count >= RoomInventoryCapacity) rejection = CraftRejection.InventoryFull;
        else foreach (var cost in recipe.Costs)
            if (!roomMaterials.TryGetValue(cost.Key, out var available) || available.count < cost.Value)
            { rejection = CraftRejection.MissingMaterials; break; }
        string productId = "";
        if (rejection == CraftRejection.None)
        {
            long nextVersion = checked(EquipmentVersion + 1);
            // An independent item identity lets repeated crafts coexist and equip individually.
            productId = "crafted/" + EntityId + "/" + request.requestSeq;
            var definition = recipe.Product;
            var product = RoomCombatConfiguration.CreateEquipment(new RoomEquipmentDto { configurationId = productId,
                definitionId = definition.definitionId, displayName = definition.displayName, slot = definition.slot, modifiers = definition.modifiers });
            ownedRoomEquipment.Add(product);
            foreach (var cost in recipe.Costs)
            {
                var available = roomMaterials[cost.Key]; available.count -= cost.Value;
                if (available.count == 0) roomMaterials.Remove(cost.Key);
            }
            roomInventory.Add(product);
            EquipmentVersion = nextVersion; equipmentTick = tick;
        }
        return new CraftResultDto { roomSessionId = RoomSessionId, authorityTick = tick, resultId = results.Allocate(),
            playerId = PlayerId, entityId = EntityId, requestSeq = request.requestSeq, recipeId = request.recipeId, productId = productId,
            accepted = rejection == CraftRejection.None, rejection = rejection, combat = CaptureRoomConfiguration(), flask = CaptureRoomFlaskState(),
            health = Stats.currentHealth, maxHealth = Stats.GetMaxHealthValue(), healthVersion = Stats.NetworkHealthVersion };
    }
}
