using System;
using System.IO;
using System.Reflection;
using Rpg.Networking;
using Rpg.Networking.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DamageResultVerification
{
    [MenuItem("Tools/RPG/Verify Damage Results and Health")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
        VerifyRules();
        var scene = EditorSceneManager.NewPreviewScene();
        var random = UnityEngine.Random.state;
        try { VerifyReceiver(scene); VerifyAuthority(scene); }
        finally { UnityEngine.Random.state = random; EditorSceneManager.ClosePreviewScene(scene); }
        Debug.Log("[DamageResults] PASS: physical/armor/critical/evasion, authoritative hit once, common result IDs, JSON round trip, ID gaps/duplicates/other streams, per-target health version/tick, baselines and no client RNG. Dual-instance acceptance remains required.");
    }

    private static void VerifyRules()
    {
        var input = new PhysicalDamageInput { damage = 20, strength = 5, armor = 8, critPower = 150, health = 100 };
        var normal = PhysicalDamageRules.Calculate(input, () => 0);
        Require(normal.ActualDamage == 17 && normal.FinalHealth == 83 && !normal.Critical && !normal.Evaded, "Zero-percent crit/dodge stay zero even on roll zero");
        input.critChance = 100;
        var crit = PhysicalDamageRules.Calculate(input, () => 99);
        Require(crit.Critical && crit.ActualDamage == 31 && crit.FinalHealth == 69, "A critical uses (damage+strength)*(critPower+strength)% before armor");
        input.evasion = 100; int rolls = 0;
        var evaded = PhysicalDamageRules.Calculate(input, () => { rolls++; return 99; });
        Require(evaded.Evaded && !evaded.Critical && evaded.ActualDamage == 0 && evaded.FinalHealth == 100 && rolls == 1, "Dodge confirms zero damage without a critical re-roll");
        input.evasion = 0; input.invincible = true;
        Require(PhysicalDamageRules.Calculate(input, () => throw new Exception("Unexpected roll")).ActualDamage == 0, "Invincibility cannot roll or subtract health");
        input.invincible = false; input.critChance = 0; input.armor = 1000;
        Require(PhysicalDamageRules.Calculate(input, () => 99).ActualDamage == 0, "Armor cannot create healing");
        input.armor = 0; input.health = 10;
        var overkill = PhysicalDamageRules.Calculate(input, () => 99);
        Require(overkill.ActualDamage == 10 && overkill.FinalHealth == 0, "Actual damage is HP removed; lethal damage clamps at zero");
        input.health = 100; input.armor = 10; input.chilled = true; input.vulnerable = true;
        Require(PhysicalDamageRules.Calculate(input, () => 99).ActualDamage == 19, "Existing chilled armor and vulnerability calculations remain authority-side");
    }

    private static void VerifyReceiver(Scene scene)
    {
        string room = Guid.NewGuid().ToString("N");
        using (var context = Context(room, true))
        using (var receiver = new RoomCombatDamage(context, null))
        {
            var source = Body(scene, "Display source"); context.RegisterEntity(101, 1, source.transform);
            var first = Body(scene, "Display target one"); var firstStats = first.AddComponent<CharaterStats>(); FillStats(firstStats);
            var second = Body(scene, "Display target two"); var secondStats = second.AddComponent<CharaterStats>(); FillStats(secondStats);
            var a = context.RegisterEntity(201, 0, first.transform); var b = context.RegisterEntity(202, 0, second.transform);
            a.ApplyVersionedHealth(room, 10, 1, 100, 100); b.ApplyVersionedHealth(room, 10, 1, 100, 100);
            firstStats.maxHealth.SetDefaultValue(9999); firstStats.evasion.SetDefaultValue(100);
            var high = Damage(room, 100, 201, 2, 20, 10, 90);
            DamageResultDto copy;
            using (var stream = new MemoryStream(ProtocolCodec.Encode(high, true)))
            {
                Require(NetPacket.TryRead(stream, out short id, out byte[] payload), "Damage packet framing");
                copy = (DamageResultDto)ProtocolCodec.Decode(id, payload, true);
            }
            Require(copy.resultId == 100 && copy.roomSessionId == room && copy.authorityTick == 20 && copy.targetStateVersion == 2,
                "Inherited common envelope and damage fields serialize through the only codec");
            var saved = UnityEngine.Random.state;
            float expected = UnityEngine.Random.value; UnityEngine.Random.state = saved;
            Require(receiver.ApplyConfirmedResult(copy) && !receiver.ApplyConfirmedResult(copy), "Exactly one acceptance for the same result ID");
            Require(UnityEngine.Random.value == expected, "B never rolls damage/evasion/critical"); UnityEngine.Random.state = saved;
            Require(firstStats.currentHealth == 90 && firstStats.GetMaxHealthValue() == 100 && a.Health == 90, "B uses confirmed HP/max, ignoring its local armor/evasion/max-health data");
            Require(receiver.Receipts.TryAccept(new OtherResult { roomSessionId = room, resultId = 900, authorityTick = 30 }), "Another/private result stream may have a higher ID");
            Require(receiver.ApplyConfirmedResult(Damage(room, 7, 202, 2, 19, 25, 75)) && b.Health == 75,
                "A smaller ID for another target is accepted immediately despite holes and another stream's ID 900");
            Require(receiver.ApplyConfirmedResult(Damage(room, 101, 201, 1, 22, 5, 95)) && a.Health == 90,
                "A new result ID is handled, but an older target health version cannot overwrite current HP");
            Require(receiver.ApplyConfirmedResult(Damage(room, 102, 201, 3, 19, 10, 80)) && a.Health == 90,
                "An older authority tick cannot override HP even with a larger version");
            Require(receiver.ApplyConfirmedResult(Damage(room, 103, 201, 3, 23, 10, 80)) && a.Health == 80,
                "A new version/tick applies the final value, never subtracting damage twice");
            Require(receiver.ApplyConfirmedResult(Damage(room, 104, 201, 4, 23, 10, 70)) && a.Health == 70,
                "Multiple hits in one authority tick sort by target version");
            Require(!a.ApplyVersionedHealth(room, 25, 1, 100, 100) && !a.ApplyHealth(room, 26, 99999, 100, 100),
                "An old baseline and the legacy health/stateSeq stream cannot undo damage");
            Require(a.ApplyVersionedHealth(room, 25, 4, 70, 100), "An equal-version, equal-value baseline can establish a later tick barrier");
            Require(receiver.ApplyConfirmedResult(Damage(room, 105, 201, 5, 24, 10, 60)) && a.Health == 70, "Baseline tick barrier rejects older final values");
            Require(!receiver.ApplyConfirmedResult(Damage(Guid.NewGuid().ToString("N"), 106, 201, 5, 26, 10, 60)), "Foreign room results cannot mutate health");
            var invalid = Damage(room, 106, 201, 5, 26, 10, 60); invalid.evaded = true;
            ExpectInvalid(() => ProtocolCodec.Encode(invalid, true));
            invalid = Damage(room, 106, 201, 5, 26, 10, 60); invalid.targetStateVersion = 0;
            ExpectInvalid(() => ProtocolCodec.Encode(invalid, true));
            ExpectInvalid(() => ProtocolCodec.Encode(copy));
            context.Dispose();
            Require(!receiver.ApplyConfirmedResult(Damage(room, 106, 201, 5, 26, 10, 60)), "Expired session cannot accept late results");
        }
    }

    private static void VerifyAuthority(Scene scene)
    {
        string room = Guid.NewGuid().ToString("N");
        using (var context = Context(room, false))
        using (var objects = new RoomNetworkObjects(room, scene))
        using (var sequence = new AuthorityResultSequence(room))
        {
            var sourceObject = Body(scene, "Authority damage source");
            var source = sourceObject.AddComponent<Player>(); var stats = sourceObject.AddComponent<PlayerStats>(); FillStats(stats);
            context.RegisterEntity(101, 1, source.transform);
            objects.Register(room, 1, sourceObject, () =>
            {
                typeof(PlayerCombatContext).GetMethod("ReleaseRoomEquipment", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(source.Combat, null);
                context.RemoveEntity(101);
            });
            RoomCombatConfiguration.InitializeAuthority(source, true);
            Set(typeof(Entity), source, "stats", stats); Set(typeof(Player), source, "IsRuntimeInitialized", true);
            Set(typeof(CharaterStats), stats, "IsRuntimeInitialized", true); stats.currentHealth = 140;
            typeof(PlayerCombatContext).GetMethod("BindPrimaryAttack", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(source.Combat, new object[] { objects });
            sourceObject.SetActive(true);
            var targetObject = Body(scene, "Authority damage target"); var target = targetObject.AddComponent<Enemy>();
            var targetStats = targetObject.AddComponent<EnemyStats>(); FillStats(targetStats);
            targetStats.soulsDropAmount = new Stat(); targetStats.armor.SetDefaultValue(2);
            var entry = new RoomSceneMonsters.SceneEntry { Key = "target", Type = typeof(Enemy).FullName, Enemy = target, Active = true };
            using (var monsters = new RoomSceneMonsters(new[] { entry }, objects))
            {
                monsters.BindAuthority(context, () => 201); target.enabled = true;
                int publications = 0; DamageResultDto sent = null;
                using (var service = new RoomCombatDamage(context, monsters, sequence, value => { publications++; sent = value; }))
                {
                    sequence.Allocate(); // Reserved/private result: receiving clients must tolerate this gap.
                    var runtime = source.Combat.PrimaryAttack;
                    Require(runtime.TryQueue(100, 10, PrimaryAttackRejection.None) && runtime.TryBegin(11, PrimaryAttackRejection.None, 20, 40, 1, 6), "Admit authority attack");
                    Require(runtime.TryRegisterHit(runtime.Current, 1, 201, 12), "Admit the target once");
                    var candidate = (PrimaryAttackHitCandidate)Activator.CreateInstance(typeof(PrimaryAttackHitCandidate), BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new object[] { target, targetObject.GetComponent<Collider2D>(), 201 }, null);
                    var hit = (PrimaryAttackConfirmedHit)Activator.CreateInstance(typeof(PrimaryAttackConfirmedHit), BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new object[] { runtime.Current, 1, candidate, 12UL }, null);
                    service.OnPrimaryHit(hit); service.OnPrimaryHit(hit);
                    Require(publications == 1 && sequence.LastAllocated == 2 && sent.resultId == 2 && sent.attackInstanceId == 1 &&
                        targetStats.currentHealth == 84 && sent.actualDamage == 16 && sent.targetStateVersion == 2,
                        "A computes damage once, applies HP once and uses the shared result ID independent of request/attack IDs");
                    Require(context.TryGetEntity(201, out var view) && view.Health == 84 && view.HealthVersion == 2, "A's health bar uses the same confirmed value");
                }
            }
            sequence.Dispose();
            bool rejected = false; try { sequence.Allocate(); } catch (ObjectDisposedException) { rejected = true; }
            Require(rejected, "A closed room's result source cannot allocate late IDs");
        }
    }

    private sealed class OtherResult : AuthorityResultDto { }
    private static DamageResultDto Damage(string room, long id, int target, long version, ulong tick, int damage, int health) => new DamageResultDto
    { roomSessionId = room, resultId = id, authorityTick = tick, sourcePlayerId = 1, sourceEntityId = 101, targetEntityId = target,
        attackInstanceId = 500, hitSegmentId = 1, actualDamage = damage, finalHealth = health, maxHealth = 100, targetStateVersion = version };
    private static RoomPresentationContext Context(string room, bool guest) => new RoomPresentationContext(room, guest ? 2 : 1, guest, Identity(room, 1, 101), Identity(room, 2, 102));
    private static RoomPlayerIdentity Identity(string room, int player, int entity) => (RoomPlayerIdentity)Activator.CreateInstance(typeof(RoomPlayerIdentity),
        BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { room, player, entity }, null);
    private static GameObject Body(Scene scene, string name)
    { var value = new GameObject(name); value.SetActive(false); SceneManager.MoveGameObjectToScene(value, scene); return value; }
    private static void FillStats(CharaterStats stats)
    {
        foreach (var field in typeof(CharaterStats).GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (field.FieldType == typeof(Stat)) field.SetValue(stats, new Stat());
        stats.maxHealth.SetDefaultValue(100);
    }
    private static void Set(Type type, object target, string property, object value) => type.GetProperty(property).GetSetMethod(true).Invoke(target, new[] { value });
    private static void ExpectInvalid(Action action)
    { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } Require(rejected, "Invalid result rejected by codec"); }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("[DamageResults] FAIL: " + message); }
}
