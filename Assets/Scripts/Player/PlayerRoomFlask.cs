using System;
using Rpg.Networking;
using Rpg.Networking.Protocol;
using UnityEngine;

public sealed partial class PlayerCombatContext
{
    private long flaskRequestSeq, flaskStateSeq, flaskVersion = 1;
    private ulong flaskStartTick, flaskEndTick;
    private int flaskDurationTicks;
    private void RoomFlaskEquipmentChanged() => flaskVersion = checked(flaskVersion + 1);
    internal RoomFlaskStateDto CaptureRoomFlaskState()
    {
        if (!Owner.IsAuthoritative) throw new InvalidOperationException("Only A captures flask state.");
        var item = GetEquipment(EquipmentType.Flask);
        return new RoomFlaskStateDto { version = flaskVersion, stateSeq = checked(++flaskStateSeq),
            equippedItemId = item == null ? "" : item.name, definitionId = item == null ? "" : item.itemId,
            cooldownStartTick = flaskStartTick, cooldownEndTick = flaskEndTick, durationTicks = flaskDurationTicks };
    }
    internal FlaskUseResultDto UseRoomFlask(FlaskUseRequestDto request, RoomFlaskCatalog catalog, ulong tick, float fixedDelta, AuthorityResultSequence results)
    {
        if (!ProtocolCodec.ValidFlaskRequest(request) || request.roomSessionId != RoomSessionId ||
            request.requestSeq <= flaskRequestSeq) return null;
        flaskRequestSeq = request.requestSeq;
        var rejection = FlaskUseRejection.None;
        var item = GetEquipment(EquipmentType.Flask); RoomFlaskCatalog.Definition definition = null;
        if (!Owner.IsRuntimeInitialized || !Stats.IsRuntimeInitialized || Stats.isDead || Stats.currentHealth <= 0) rejection = FlaskUseRejection.PlayerUnavailable;
        else if (Owner.NetworkInputLocked || Owner.IsLocalInputOwner && GameplayControlLock.IsActionInputBlocked) rejection = FlaskUseRejection.InputLocked;
        else if (item == null) rejection = FlaskUseRejection.NoFlask;
        else if (!catalog.TryGet(item.itemId, out definition)) rejection = FlaskUseRejection.UnsupportedFlask;
        else if (tick < flaskEndTick) rejection = FlaskUseRejection.Cooldown;
        int actual = 0;
        if (rejection == FlaskUseRejection.None)
        {
            double ticks = Math.Ceiling((double)(definition.Cooldown / fixedDelta));
            if (ticks > int.MaxValue) return null;
            int duration = (int)ticks; ulong end = checked(tick + (ulong)duration);
            long version = checked(flaskVersion + 1);
            int health = definition.Heal(Stats.currentHealth, Stats.GetMaxHealthValue());
            actual = health - Stats.currentHealth;
            Stats.ApplyAuthorityNetworkHealth(health);
            flaskStartTick = tick; flaskEndTick = end; flaskDurationTicks = duration; flaskVersion = version;
            // Full-health use keeps the existing solo semantics: accepted, zero actual healing, starts cooldown.
        }
        return new FlaskUseResultDto { roomSessionId = RoomSessionId, authorityTick = tick, resultId = results.Allocate(),
            playerId = PlayerId, entityId = EntityId, requestSeq = request.requestSeq, accepted = rejection == FlaskUseRejection.None,
            rejection = rejection, actualHealing = actual, health = Stats.currentHealth, maxHealth = Stats.GetMaxHealthValue(),
            healthVersion = Stats.NetworkHealthVersion, flask = CaptureRoomFlaskState() };
    }
}
