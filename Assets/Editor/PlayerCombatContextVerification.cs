using System;
using System.Reflection;
using Rpg.Networking;
using Rpg.Networking.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Temporary configurations only; never start skills, apply damage, edit assets or save the scene.</summary>
public static class PlayerCombatContextVerification
{
    [MenuItem("Tools/RPG/Verify Room Combat Configuration")]
    public static void VerifyRoomConfiguration()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Open MainScene outside Play mode first.");
        Scene source = SceneManager.GetSceneByPath("Assets/Scenes/MainScene.unity");
        if (!source.isLoaded) throw new InvalidOperationException("Open MainScene outside Play mode first.");
        Player template = RoomPlayerEntities.FindSceneTemplate(source);
        int originalDamage = template.GetComponent<PlayerStats>().damage.GetValue();
        Scene preview = EditorSceneManager.NewPreviewScene();
        string room = Guid.NewGuid().ToString("N");
        var host = Identity(room, 1, 101); var guest = Identity(room, 2, 102);
        try
        {
            using (var a = new RoomPresentationContext(room, 1, false, host, guest))
            using (var b = new RoomPresentationContext(room, 2, true, host, guest))
            {
                Player hostBody = Copy(template, preview, "Authority_A"), guestBody = Copy(template, preview, "Authority_B");
                a.RegisterEntity(101, 1, hostBody.transform); a.RegisterEntity(102, 2, guestBody.transform);
                Require(!hostBody.gameObject.activeInHierarchy && !guestBody.gameObject.activeInHierarchy &&
                    EntityRuntimePermissions.Identity(hostBody) == hostBody.GetComponent<NetworkEntityIdentity>() &&
                    EntityRuntimePermissions.Identity(hostBody.GetComponentInChildren<Animator>(true)) == hostBody.GetComponent<NetworkEntityIdentity>() &&
                    hostBody.IsAuthoritative && guestBody.IsAuthoritative,
                    "Inactive staging root and child resolve their bound authority before activation");
                RoomCombatConfiguration.InitializeAuthority(hostBody, true);
                RoomCombatConfiguration.InitializeAuthority(guestBody, false);
                Require(hostBody.Combat.Stats.GetMaxHealthValue() == 140 && guestBody.Combat.Stats.GetMaxHealthValue() == 180 &&
                    hostBody.Combat.Stats.damage.GetValue() == 14 && guestBody.Combat.Stats.damage.GetValue() == 28, "Explicit A/B fixtures override local template stats");
                Require(hostBody.Combat.Skills.Get(RoomSkillId.Sword).Unlocked && !hostBody.Combat.Skills.Get(RoomSkillId.Crystal).Unlocked &&
                    !guestBody.Combat.Skills.Get(RoomSkillId.Sword).Unlocked && guestBody.Combat.Skills.Get(RoomSkillId.Crystal).Unlocked, "Independent unlocks");
                Require(!guestBody.Combat.Skills.Commit<SwordSkill>() && hostBody.Combat.Skills.Commit<DashSkill>(), "Locked skill rejected; A commits its own dash");
                hostBody.Combat.Skills.Advance(0.5f);
                RoomCombatConfiguration.InitializeAuthority(hostBody, true);
                Require(Mathf.Approximately(hostBody.NetworkDashCooldownRemaining, 1), "Repeat setup cannot reset A cooldown");
                var packet = new PlayerInitializationDto
                {
                    roomSessionId = room, loadId = 1, authorityTick = 100, stateSeq = 1,
                    players = new[]
                    {
                        Initial(hostBody, 1, 101), Initial(guestBody, 2, 102)
                    }
                };
                PlayerInitializationDto confirmed;
                using (var stream = new System.IO.MemoryStream(ProtocolCodec.Encode(packet, true)))
                {
                    Require(NetPacket.TryRead(stream, out short id, out byte[] payload), "Initialization packet");
                    confirmed = (PlayerInitializationDto)ProtocolCodec.Decode(id, payload, true);
                }
                Player viewA = Copy(template, preview, "Display_A"), viewB = Copy(template, preview, "Display_B");
                b.RegisterEntity(101, 1, viewA.transform); b.RegisterEntity(102, 2, viewB.transform);
                viewA.Combat.Stats.damage.SetDefaultValue(999); viewB.Combat.Stats.damage.SetDefaultValue(888);
                viewB.Combat.Skills.Configure<DashSkill>(99);
                Require(ApplyDisplay(viewA, confirmed.players[0].combat, room, 100, 1) &&
                    ApplyDisplay(viewB, confirmed.players[1].combat, room, 100, 1), "Apply A-confirmed data to each display");
                Require(viewA.Combat.Stats.damage.GetValue() == 14 && viewB.Combat.Stats.damage.GetValue() == 28 &&
                    viewB.Combat.Stats.GetMaxHealthValue() == 180 && viewB.Combat.Skills.Get<DashSkill>().Cooldown == 2.5f &&
                    Mathf.Approximately(viewA.NetworkDashCooldownRemaining, 1), "B local stats and cooldown configuration cannot become authority");
                Require(viewB.Combat.GetEquipment(EquipmentType.Weapon).name == "test-weapon-b-v1" &&
                    !ReferenceEquals(viewB.Combat.GetEquipment(EquipmentType.Weapon), guestBody.Combat.GetEquipment(EquipmentType.Weapon)), "Display owns separate data-only equipment references");
                Require(!ApplyDisplay(viewB, RoomCombatConfiguration.CreateTestProfile(true), room, 99, 2) &&
                    !ApplyDisplay(viewB, RoomCombatConfiguration.CreateTestProfile(true), room, 100, 1) &&
                    !ApplyDisplay(viewB, RoomCombatConfiguration.CreateTestProfile(true), Guid.NewGuid().ToString("N"), 101, 2), "Old tick, duplicate and foreign session rejected");
                Require(!viewB.Combat.Skills.Commit<DashSkill>(), "Unlock state cannot enable display-side simulation");
                var invalid = RoomCombatConfiguration.CreateTestProfile(true);
                invalid.skills[1].skillId = RoomSkillId.Dash;
                Require(!ProtocolCodec.IsValidCombat(invalid), "Duplicate skill slot rejected");
                invalid = RoomCombatConfiguration.CreateTestProfile(true); invalid.equipment[0].modifiers = null;
                Require(!ProtocolCodec.IsValidCombat(invalid), "Missing equipment data rejected");
                invalid = RoomCombatConfiguration.CreateTestProfile(true); invalid.skills[0].remaining = float.NaN;
                Require(!ProtocolCodec.IsValidCombat(invalid), "Invalid cooldown rejected");
                Require(!ProtocolCodec.IsValidCombat(null) && template.GetComponent<PlayerStats>().damage.GetValue() == originalDamage,
                    "Missing configuration rejected; source assets remain unchanged");
                VerifyStagedPlayerCreation(template, preview);
                VerifyDestroyedIdentityCallback(preview);
            }
        }
        finally
        {
            // Dispose the presentation subscriptions above while scene objects still exist.
            try
            {
                // Preview bodies may never activate, so do not depend on Unity invoking OnDestroy.
                foreach (var root in preview.GetRootGameObjects())
                    foreach (var player in root.GetComponentsInChildren<Player>(true))
                        typeof(PlayerCombatContext).GetMethod("ReleaseRoomEquipment", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(player.Combat, null);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        Debug.Log("[RoomCombatConfiguration] PASS: authoritative A/B fixtures, full initialization round trip, local-data override, equipment/unlock/cooldown isolation, session/tick/sequence guards and destroyed-identity cleanup.");
    }

    private static void VerifyDestroyedIdentityCallback(Scene scene)
    {
        string room = Guid.NewGuid().ToString("N");
        using (var context = new RoomPresentationContext(room, 1, false, Identity(room, 1, 701), Identity(room, 2, 702)))
        {
            var value = new GameObject("Inactive identity cleanup probe");
            value.SetActive(false); SceneManager.MoveGameObjectToScene(value, scene);
            context.RegisterEntity(703, 0, value.transform);
            var identity = value.GetComponent<NetworkEntityIdentity>();
            // Simulate a callback captured in an event invocation before the native object was destroyed.
            var callback = (Action)Delegate.CreateDelegate(typeof(Action), identity,
                typeof(NetworkEntityIdentity).GetMethod("OnPresentationChanged", BindingFlags.Instance | BindingFlags.NonPublic));
            UnityEngine.Object.DestroyImmediate(value);
            callback(); callback();
            Require(identity == null && !identity.IsBound && identity.Role == NetworkEntityRole.Unassigned,
                "Late/repeated presentation notifications safely detach a destroyed inactive identity");
            context.Dispose(); context.Dispose();
        }
    }

    private static void VerifyStagedPlayerCreation(Player template, Scene scene)
    {
        Player stagingTemplate = Copy(template, scene, "Inactive room creation template");
        string room = Guid.NewGuid().ToString("N");
        var host = Identity(room, 1, 501); var guest = Identity(room, 2, 502);
        using (var context = new RoomPresentationContext(room, 1, false, host, guest))
        using (var objects = new RoomNetworkObjects(room, scene))
        using (var players = new RoomPlayerEntities(scene, stagingTemplate, Vector2.zero, Vector2.right * 2.5f, objects))
        {
            players.EnsurePlayers(context, host, guest);
            var a = players.Entities[501]; var b = players.Entities[502];
            Require(players.Entities.Count == 2 && a.IsAuthoritative && b.IsAuthoritative &&
                a.GetComponent<RoomNetworkObject>().IsAssigned && b.GetComponent<RoomNetworkObject>().IsAssigned &&
                a.Combat.RoomConfigurationId == "room-test-v1/A" && b.Combat.RoomConfigurationId == "room-test-v1/B" &&
                a.Combat.PrimaryAttack != null && b.Combat.PrimaryAttack != null,
                "Production room creation binds identity/lifetime before initializing both authority combat profiles");
            players.EnsurePlayers(context, host, guest);
            Require(players.Entities.Count == 2 && ReferenceEquals(a, players.Entities[501]) && ReferenceEquals(b, players.Entities[502]),
                "Repeated room creation preserves the same initialized actors");
        }
    }

    private static InitialPlayerDto Initial(Player player, int playerId, int entityId) => new InitialPlayerDto
    {
        playerId = playerId, entityId = entityId, position = new[] { 0f, 0f }, facingDir = 1,
        health = player.Combat.Stats.GetMaxHealthValue(), maxHealth = player.Combat.Stats.GetMaxHealthValue(), healthVersion = 1,
        combat = player.Combat.CaptureRoomConfiguration()
    };

    private static bool ApplyDisplay(Player player, PlayerCombatDto value, string room, ulong tick, long seq) =>
        (bool)typeof(PlayerCombatContext).GetMethod("ApplyConfirmedRoomConfiguration", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(player.Combat, new object[] { value, room, tick, seq });

    [MenuItem("Tools/RPG/Verify Player Combat Ownership")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Open MainScene outside Play mode first.");
        Scene source = SceneManager.GetSceneByPath("Assets/Scenes/MainScene.unity");
        if (!source.isLoaded) throw new InvalidOperationException("Open MainScene outside Play mode first.");
        Player template = RoomPlayerEntities.FindSceneTemplate(source);
        int originalDamage = template.GetComponent<PlayerStats>().damage.GetValue();
        Scene preview = EditorSceneManager.NewPreviewScene();
        ItemData_Equipment weaponA = null, weaponB = null;
        RoomPresentationContext authority = null, display = null;
        try
        {
            string room = Guid.NewGuid().ToString("N");
            var host = Identity(room, 1, 101);
            var guest = Identity(room, 2, 102);
            authority = new RoomPresentationContext(room, 1, false, host, guest);
            display = new RoomPresentationContext(room, 2, true, host, guest);
            Player a = Copy(template, preview, "Combat_Test_A");
            Player b = Copy(template, preview, "Combat_Test_B");
            Player view = Copy(template, preview, "Combat_Test_B_Display");
            authority.RegisterEntity(101, 1, a.transform);
            authority.RegisterEntity(102, 2, b.transform);
            display.RegisterEntity(102, 2, view.transform);
            var ca = a.Combat; var cb = b.Combat;
            Require(ca.Owner == a && cb.Owner == b && ca.PlayerId == 1 && cb.PlayerId == 2 &&
                ca.EntityId == 101 && cb.EntityId == 102 && ca.RoomSessionId == room, "Entity-bound ownership");
            Require(a.IsAuthoritative && b.IsAuthoritative && !view.IsAuthoritative && !b.IsLocalInputOwner,
                "Authority is separate from local input ownership");
            Require(ca.LegacySkills == null && cb.LegacySkills == null && ca.LegacyInventory == null && cb.LegacyInventory == null,
                "Room actors never inherit global solo managers");
            foreach (StatType type in Enum.GetValues(typeof(StatType)))
            {
                Stat sa = ca.Stats.GetStat(type), sb = cb.Stats.GetStat(type), original = template.GetComponent<PlayerStats>().GetStat(type);
                Require(!ReferenceEquals(sa, sb) && !ReferenceEquals(sa, original) &&
                    !ReferenceEquals(sa.modifiers, sb.modifiers) && !ReferenceEquals(sa.modifiers, original.modifiers),
                    "Independent stat and modifier list: " + type);
            }

            // Two deliberately different temporary test configurations. No persistent asset is changed.
            ca.Stats.damage.SetDefaultValue(11); ca.Stats.damage.modifiers.Clear();
            cb.Stats.damage.SetDefaultValue(29); cb.Stats.damage.modifiers.Clear();
            ca.Stats.currentHealth = 90; cb.Stats.currentHealth = 140;
            weaponA = ScriptableObject.CreateInstance<ItemData_Equipment>();
            weaponA.equipmentType = EquipmentType.Weapon; weaponA.damage = 3;
            weaponB = ScriptableObject.CreateInstance<ItemData_Equipment>();
            weaponB.equipmentType = EquipmentType.Weapon; weaponB.damage = 7;
            ca.SetEquipment(EquipmentType.Weapon, weaponA); cb.SetEquipment(EquipmentType.Weapon, weaponB);
            Require(ca.Stats.damage.GetValue() == 14 && cb.Stats.damage.GetValue() == 36 &&
                ca.GetEquipment(EquipmentType.Weapon) == weaponA && cb.GetEquipment(EquipmentType.Weapon) == weaponB,
                "A=11+3, B=29+7, distinct equipment ownership");
            ca.SetEquipment(EquipmentType.Weapon, weaponA);
            Require(ca.Stats.damage.GetValue() == 14, "Repeated configuration cannot stack equipment modifiers");
            ca.SetEquipment(EquipmentType.Weapon, null);
            Require(ca.Stats.damage.GetValue() == 11 && cb.Stats.damage.GetValue() == 36, "A unequip cannot change B");
            ca.Stats.currentHealth = 50;
            Require(cb.Stats.currentHealth == 140, "Health belongs to its actor");
            ca.SetEquipment(EquipmentType.Weapon, weaponB);
            Require(ca.Stats.damage.GetValue() == 18 && cb.Stats.damage.GetValue() == 36 && weaponB.damage == 7,
                "Shared read-only equipment configuration does not share runtime modifiers");

            ca.Skills.Configure<DashSkill>(1.5f); cb.Skills.Configure<DashSkill>(2.5f);
            Require(ca.Skills.Context == ca && cb.Skills.Context == cb && ca.Skills.Commit<DashSkill>() &&
                cb.Skills.Get<DashSkill>().Remaining == 0, "A cooldown cannot start B cooldown");
            ca.Skills.Advance(0.5f);
            Require(Mathf.Approximately(a.NetworkDashCooldownRemaining, 1) && cb.Skills.Commit<DashSkill>(), "Independent cooldown configuration");
            Require(!ca.Skills.Commit<DashSkill>() && Mathf.Approximately(a.NetworkDashCooldownRemaining, 1), "Rejected repeat cannot refresh cooldown");
            cb.Skills.Advance(0.25f);
            Require(Mathf.Approximately(b.NetworkDashCooldownRemaining, 2.25f) && Mathf.Approximately(a.NetworkDashCooldownRemaining, 1),
                "Independent skill runtime advancement");
            ca.Skills.Configure<SwordSkill>(3);
            Require(ca.Skills.Get<SwordSkill>().Remaining == 0 && ca.Skills.Get<DashSkill>().Remaining == 1, "Skill slots are independent");
            Require(!view.Combat.Skills.Commit<DashSkill>(), "Display cannot start authoritative cooldown");
            bool rejected = false;
            try { view.Combat.SetEquipment(EquipmentType.Weapon, weaponA); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Display cannot apply equipment modifiers");

            authority.RemovePlayer(2);
            Require(!cb.Skills.Commit<SwordSkill>(), "Retired player cannot execute combat runtime");
            Player replacement = Copy(template, preview, "Combat_Test_B_NewSession");
            Require(replacement.Combat.Skills.Get<DashSkill>().Remaining == 0 &&
                replacement.Combat.GetEquipment(EquipmentType.Weapon) == null, "Fresh entity has no old equipment or cooldown state");
            Require(ca.Stats.damage.GetValue() == 18 && template.GetComponent<PlayerStats>().damage.GetValue() == originalDamage,
                "Guest removal leaves A and source template unchanged");
            VerifySoloBinding(template, preview);
            Debug.Log("[PlayerCombatOwnership] PASS: A/B stats, equipment/modifiers, skill context/cooldowns, authority/display, solo binding and fresh-entity isolation. No attacks or skill effects executed.");
        }
        finally
        {
            authority?.Dispose(); display?.Dispose();
            EditorSceneManager.ClosePreviewScene(preview);
            if (weaponA != null) UnityEngine.Object.DestroyImmediate(weaponA);
            if (weaponB != null) UnityEngine.Object.DestroyImmediate(weaponB);
        }
    }

    private static void VerifySoloBinding(Player template, Scene scene)
    {
        Player a = Copy(template, scene, "SoloBinding_A"), b = Copy(template, scene, "SoloBinding_B");
        var skillObject = new GameObject("Solo skill adapter");
        SceneManager.MoveGameObjectToScene(skillObject, scene);
        Skill skill = skillObject.AddComponent<Skill>();
        skill.cooldownTimer = 0.7f;
        skill.BindOwner(a.Combat);
        Require(skill.Combat == a.Combat && Mathf.Approximately(a.Combat.Skills.Get<Skill>().Remaining, 0.7f),
            "Solo adapter uses its owner's skill runtime");
        bool rejected = false;
        try { skill.BindOwner(b.Combat); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected && skill.Combat == a.Combat, "A skill instance cannot be rebound to another actor");
    }

    private static Player Copy(Player source, Scene scene, string name)
    {
        GameObject clone = UnityEngine.Object.Instantiate(source.gameObject);
        clone.name = name; clone.SetActive(false);
        SceneManager.MoveGameObjectToScene(clone, scene);
        return clone.GetComponent<Player>();
    }
    private static RoomPlayerIdentity Identity(string room, int player, int entity) =>
        (RoomPlayerIdentity)Activator.CreateInstance(typeof(RoomPlayerIdentity), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { room, player, entity }, null);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[PlayerCombatOwnership] FAIL: " + message);
    }
}
