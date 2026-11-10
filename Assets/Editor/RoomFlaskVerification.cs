using System;
using System.Reflection;
using Rpg.Networking;
using Rpg.Networking.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class RoomFlaskVerification
{
    [MenuItem("Tools/RPG/Verify Room Flask Use")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
        var scene = EditorSceneManager.NewPreviewScene(); string room = Guid.NewGuid().ToString("N");
        var aId = Identity(room, 1, 101); var bId = Identity(room, 2, 102);
        var authority = new RoomPresentationContext(room, 1, false, aId, bId);
        var display = new RoomPresentationContext(room, 2, true, aId, bId);
        var heal = ScriptableObject.CreateInstance<HealEffect>(); Field(heal, "healPercent", .3f);
        var extra = ScriptableObject.CreateInstance<ItemEffect>();
        var flask = ScriptableObject.CreateInstance<ItemData_Equipment>(); flask.name = "Verification healing flask";
        flask.itemName = flask.name; flask.itemType = ItemType.Equipment; flask.equipmentType = EquipmentType.Flask;
        flask.itemCooldown = 15; flask.itemEffects = new ItemEffect[] { heal };
        var authored = AssetDatabase.LoadAssetAtPath<ItemData_Equipment>("Assets/Data/Items/Equipment/Flask/Healing Flask.asset");
        flask.itemIcon = authored.itemIcon;
        var mixed = ScriptableObject.CreateInstance<ItemData_Equipment>(); mixed.name = "Verification mixed flask";
        mixed.itemType = ItemType.Equipment; mixed.equipmentType = EquipmentType.Flask; mixed.itemCooldown = 15;
        mixed.itemEffects = new ItemEffect[] { heal, extra }; mixed.itemId = RoomCraftCatalog.RecipeId(mixed);
        try
        {
            var catalog = new RoomFlaskCatalog(new[] { flask, mixed });
            var a = Player(scene, authority, 1, 101, Profile(true, flask, "owned-a"));
            var b = Player(scene, authority, 2, 102, RoomCombatConfiguration.CreateTestProfile(false));
            var shown = Player(scene, display, 2, 102, Profile(false, flask, "owned-b"));
            display.TryGetEntity(102, out var view);
            var hud = Hud(scene, flask, out var slider, out var overlay, out var icon, out var flaskRoot);
            hud.BindNetwork(view); Invoke(hud, "OnEnable");
            using (var sequence = new AuthorityResultSequence(room))
            {
                SetHealth(b, 50);
                var noFlask = Use(b, Request(room, 1), catalog, 2, sequence);
                Require(noFlask.rejection == FlaskUseRejection.NoFlask && noFlask.flask.cooldownEndTick == 0 && b.stats.currentHealth == 50,
                    "No equipped flask rejects without heal/cooldown");
                Invoke(b.Combat, "InstallRoomConfiguration", Profile(false, flask, "owned-b"), false);
                b.SetNetworkInputLocked(true);
                Require(Use(b, Request(room, 2), catalog, 3, sequence).rejection == FlaskUseRejection.InputLocked, "Menu/input lock rejects");
                b.SetNetworkInputLocked(false);
                var before = (RoomFlaskStateDto)Invoke(b.Combat, "CaptureRoomFlaskState");
                view.ApplyFlaskState(room, 3, before);
                Require(flaskRoot.activeSelf && icon.sprite == flask.itemIcon && overlay.fillAmount == 0, "HUD shows equipped confirmed icon/ready state");
                Field(heal, "healPercent", 1f); flask.itemCooldown = 1; // Frozen A configuration remains 30%, 15 seconds.
                var request = Request(room, 3);
                var decoded = (FlaskUseRequestDto)ProtocolCodec.Decode((short)ProtocolId.FlaskUseRequest,
                    System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(request)), true);
                var first = Use(b, decoded, catalog, 10, sequence);
                Require(first.accepted && first.actualHealing == 54 && first.health == 104 && first.flask.durationTicks == 750 && first.flask.cooldownEndTick == 760,
                    "A uses current B max HP, frozen heal percentage and non-scaled simulation ticks");
                Require(Use(b, request, catalog, 10, sequence) == null && b.stats.currentHealth == 104, "Duplicate press cannot heal/start cooldown twice");
                var cooldown = Use(b, Request(room, 4), catalog, 11, sequence);
                Require(cooldown.rejection == FlaskUseRejection.Cooldown && cooldown.flask.cooldownEndTick == 760 && cooldown.actualHealing == 0,
                    "Cooldown rejection leaves the successful end tick unchanged");
                SetHealth(a, 50);
                var hostResult = Use(a, Request(room, 1), catalog, 11, sequence);
                Require(hostResult.accepted && hostResult.actualHealing == 42 && hostResult.health == 92 && b.stats.currentHealth == 104,
                    "Players have independent healing, cooldowns and request streams");
                a.Combat.SetEquipment(EquipmentType.Flask, mixed);
                var unsupported = Use(a, Request(room, 2), catalog, 12, sequence);
                Require(unsupported.rejection == FlaskUseRejection.UnsupportedFlask && a.stats.currentHealth == 92 && unsupported.flask.cooldownEndTick == 761,
                    "Mixed/unadapted effects reject entirely without partial healing/cooldown reset");
                var wire = (FlaskUseResultDto)ProtocolCodec.Decode((short)ProtocolId.FlaskUseResult,
                    System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(first)), true);
                var receipts = new AuthorityResultReceipts(room);
                Require(receipts.TryAccept(wire) && !receipts.TryAccept(wire) && receipts.TryAccept(noFlask), "Protocol/receipt dedup allows sparse room IDs");
                Require(view.ApplyVersionedHealth(room, wire.authorityTick, wire.healthVersion, wire.health, wire.maxHealth) &&
                    view.ApplyFlaskState(room, wire.authorityTick, wire.flask) && slider.value == 104 && overlay.fillAmount == 1,
                    "B applies final HP and confirmed cooldown to its existing local HUD");
                var halfway = (RoomFlaskStateDto)Invoke(b.Combat, "CaptureRoomFlaskState");
                Require(view.ApplyFlaskState(room, 385, halfway) && Mathf.Approximately(overlay.fillAmount, .5f), "A tick snapshot advances cooldown presentation");
                Require(!view.ApplyFlaskState(room, 10, wire.flask) && !view.ApplyFlaskState(room, 3, before), "Old result/baseline cannot reset the cooldown");
                long damageVersion = SetHealth(b, 20);
                view.ApplyVersionedHealth(room, 20, damageVersion, 20, 180);
                Require(!view.ApplyVersionedHealth(room, 10, first.healthVersion, first.health, 180) && slider.value == 20,
                    "Older healing cannot overwrite a later damage result");
                var removed = Change(b, room, 1, 1, true, "owned-b", 386, sequence);
                Require(removed.accepted && removed.flask.cooldownEndTick == 760 && view.ApplyFlaskState(room, 386, removed.flask) && !flaskRoot.activeSelf,
                    "Unequip hides HUD icon without resetting cooldown");
                Require(Use(b, Request(room, 5), catalog, 387, sequence).rejection == FlaskUseRejection.NoFlask, "Unequipped flask cannot heal");
                var restored = Change(b, room, 2, 2, false, "owned-b", 388, sequence);
                Require(restored.accepted && restored.flask.cooldownEndTick == 760 && view.ApplyFlaskState(room, 388, restored.flask) && flaskRoot.activeSelf,
                    "Re-equip retains the same slot cooldown");
                Require(Use(b, Request(room, 6), catalog, 400, sequence).rejection == FlaskUseRejection.Cooldown, "Re-equip cannot bypass cooldown");
                Require(Use(b, Request(room, 7), catalog, 760, sequence).accepted, "Exact authority cooldown boundary accepts");
                SetHealth(b, 175);
                var capped = Use(b, Request(room, 8), catalog, 1510, sequence);
                Require(capped.actualHealing == 5 && capped.health == 180, "Healing clamps to current maximum");
                var full = Use(b, Request(room, 9), catalog, 2260, sequence);
                Require(full.accepted && full.actualHealing == 0 && full.flask.cooldownEndTick == 3010, "Full health preserves solo use/cooldown semantics");
                SetHealth(b, 0);
                Require(Use(b, Request(room, 10), catalog, 3010, sequence).rejection == FlaskUseRejection.PlayerUnavailable && b.stats.currentHealth == 0,
                    "Zero-health players cannot revive through a flask");
                Require(Use(b, Request(Guid.NewGuid().ToString("N"), 11), catalog, 3010, sequence) == null, "Wrong-session request is ignored");
                var baseline = (RoomFlaskStateDto)Invoke(b.Combat, "CaptureRoomFlaskState");
                // The same A state can initialize a newly admitted display without restarting the timer.
                Require(baseline.cooldownEndTick == 3010 && baseline.durationTicks == 750, "Captured rejoin state retains authority cooldown ticks");
            }
            Invoke(hud, "OnDisable"); hud.UnbindNetwork();
        }
        finally
        {
            authority.Dispose(); display.Dispose();
            foreach (var root in scene.GetRootGameObjects()) foreach (var player in root.GetComponentsInChildren<Player>(true)) Invoke(player.Combat, "ReleaseRoomEquipment");
            EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Object.DestroyImmediate(mixed); UnityEngine.Object.DestroyImmediate(flask);
            UnityEngine.Object.DestroyImmediate(heal); UnityEngine.Object.DestroyImmediate(extra);
        }
        Debug.Log("[RoomFlask] PASS: equipped/alive/input/cooldown gates, frozen healing/max-HP clamp, independent players, request/result dedup, old health/cooldown protection, equip retention, full-health semantics and existing HUD. Alpha1/menus/rejoin dual-instance acceptance remains manual.");
    }
    private static FlaskUseRequestDto Request(string room, long seq) => new FlaskUseRequestDto { roomSessionId = room, requestSeq = seq };
    private static FlaskUseResultDto Use(Player player, FlaskUseRequestDto request, RoomFlaskCatalog catalog, ulong tick, AuthorityResultSequence sequence) =>
        (FlaskUseResultDto)Invoke(player.Combat, "UseRoomFlask", request, catalog, tick, .02f, sequence);
    private static long SetHealth(Player player, int value) => (long)Invoke(player.stats, "ApplyAuthorityNetworkHealth", value);
    private static EquipmentChangeResultDto Change(Player player, string room, long seq, long version, bool remove, string id, ulong tick, AuthorityResultSequence results)
    {
        return (EquipmentChangeResultDto)Invoke(player.Combat, "ChangeRoomEquipment", new EquipmentChangeRequestDto {
            roomSessionId = room, requestSeq = seq, expectedVersion = version, kind = remove ? EquipmentChangeKind.Unequip : EquipmentChangeKind.Equip,
            slot = RoomEquipmentSlot.Flask, configurationId = id }, tick, results);
    }
    private static PlayerCombatDto Profile(bool host, ItemData_Equipment flask, string instance)
    {
        var profile = RoomCombatConfiguration.CreateTestProfile(host);
        profile.equipment[3] = new RoomEquipmentDto { configurationId = instance, definitionId = RoomCraftCatalog.RecipeId(flask),
            displayName = flask.itemName, slot = RoomEquipmentSlot.Flask, modifiers = new int[14] };
        return profile;
    }
    private static Player Player(Scene scene, RoomPresentationContext context, int id, int entity, PlayerCombatDto profile)
    {
        var go = Body(scene, "Flask test actor"); var player = go.AddComponent<Player>(); var stats = go.AddComponent<PlayerStats>();
        foreach (var field in typeof(CharaterStats).GetFields(BindingFlags.Public | BindingFlags.Instance)) if (field.FieldType == typeof(Stat)) field.SetValue(stats, new Stat());
        var view = context.RegisterEntity(entity, id, go.transform);
        Invoke(player.Combat, "InstallRoomConfiguration", profile, context.IsGuest);
        typeof(Entity).GetProperty("stats").GetSetMethod(true).Invoke(player, new object[] { stats });
        typeof(Player).GetProperty("IsRuntimeInitialized").GetSetMethod(true).Invoke(player, new object[] { true });
        typeof(CharaterStats).GetProperty("IsRuntimeInitialized").GetSetMethod(true).Invoke(stats, new object[] { true });
        int max = profile.stats[7] + 5 * profile.stats[3]; stats.currentHealth = max; Invoke(stats, "EnsureNetworkHealthVersion");
        view.ApplyVersionedHealth(context.RoomSessionId, 1, 1, max, max);
        view.ApplyHud(context.RoomSessionId, 1, 1, 0, null, 0, new float[5]); view.MarkReady(); return player;
    }
    private static UI_InGame Hud(Scene scene, ItemData_Equipment flask, out Slider slider, out Image overlay, out Image icon, out GameObject flaskRoot)
    {
        var body = Body(scene, "Flask HUD"); var hud = body.AddComponent<UI_InGame>();
        slider = Body(scene, "Health", body.transform).AddComponent<Slider>();
        flaskRoot = Body(scene, "Flask", body.transform);
        overlay = Body(scene, "Cooldown", flaskRoot.transform).AddComponent<Image>(); icon = Body(scene, "Icon", flaskRoot.transform).AddComponent<Image>();
        Field(hud, "slider", slider); Field(hud, "flaskUI", flaskRoot); Field(hud, "flaskImage", overlay); Field(hud, "flaskIcon", icon);
        hud.ConfigureRoomFlaskIcons(new[] { flask }); return hud;
    }
    private static GameObject Body(Scene scene, string name, Transform parent = null)
    { var go = new GameObject(name); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene); if (parent != null) go.transform.SetParent(parent); return go; }
    private static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static void Field(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static RoomPlayerIdentity Identity(string room, int player, int entity) => (RoomPlayerIdentity)Activator.CreateInstance(typeof(RoomPlayerIdentity), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { room, player, entity }, null);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("[RoomFlask] FAIL: " + message); }
}
