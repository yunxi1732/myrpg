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
    [MenuItem("Tools/RPG/Verify Hit Feedback (4-35)")]
    [MenuItem("Tools/RPG/Verify Direct Element Damage (4-37)")]
    [MenuItem("Tools/RPG/Verify Burn Synchronization (4-38)")]
    [MenuItem("Tools/RPG/Verify Chill Synchronization (4-39)")]
    [MenuItem("Tools/RPG/Verify Shock Chains (4-40)")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
        VerifyRules(); VerifyMagicRules(); VerifyFeedbackAssets();
        var scene = EditorSceneManager.NewPreviewScene();
        var random = UnityEngine.Random.state;
        try
        {
            VerifyReceiver(scene);
            foreach (AuthorityDamageCase kind in Enum.GetValues(typeof(AuthorityDamageCase))) VerifyAuthority(scene, kind);
            VerifyAuthority(scene, AuthorityDamageCase.Mixed, true);
        }
        finally { UnityEngine.Random.state = random; EditorSceneManager.ClosePreviewScene(scene); }
        Debug.Log("[DamageResults/Elements 4-40] PASS: shared burn/chill/shock lifecycle, A chain target tie-break/delay/damage, inherited ownership across A/B, chain snapshots/result dedup and display callback guard, no automatic recursion. Dual-instance acceptance remains required.");
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

    private static void VerifyMagicRules()
    {
        Require(DamageCalculations.SumDirectMagic(10, 6, 4, 3) == 23 && DamageCalculations.ApplyResistance(23, 5, 1) == 15,
            "Original fire+ice+lightning+INT, minus flat resistance and target INT*3 once");
        Require(DamageCalculations.ApplyResistance(23, 100, 0) == 0 && DamageCalculations.SumDirectMagic(0, 0, 0, 9) == 9,
            "Resistance cannot heal; intelligence-only magic follows the original formula");
        Require(DamageCalculations.ApplyVulnerability(15, true) == Mathf.RoundToInt(15 * 1.1f), "Original vulnerability rounding is shared");
        Require(DamageCalculations.SumDirectMagic(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue) == int.MaxValue &&
            DamageCalculations.ApplyResistance(int.MaxValue, int.MaxValue, int.MaxValue) == 0, "Large values cannot overflow into healing");
    }

    private static void VerifyReceiver(Scene scene)
    {
        string room = Guid.NewGuid().ToString("N");
        using (var context = Context(room, true))
        using (var receiver = new RoomCombatDamage(context, null))
        {
            var source = Body(scene, "Display source"); context.RegisterEntity(101, 1, source.transform);
            var first = Body(scene, "Display target one"); var displayBody = first.AddComponent<Rigidbody2D>();
            var firstStats = first.AddComponent<CharaterStats>(); FillStats(firstStats);
            var second = Body(scene, "Display target two"); var secondStats = second.AddComponent<CharaterStats>(); FillStats(secondStats);
            var a = context.RegisterEntity(201, 0, first.transform); var b = context.RegisterEntity(202, 0, second.transform);
            a.ApplyVersionedHealth(room, 10, 1, 100, 100); b.ApplyVersionedHealth(room, 10, 1, 100, 100);
            firstStats.maxHealth.SetDefaultValue(9999); firstStats.evasion.SetDefaultValue(100);
            var high = Damage(room, 100, 201, 2, 20, 10, 90);
            high.components = new DirectDamageDto { physicalDamage = 3, magicDamage = 7, rawFire = 10, rawIce = 6, rawLightning = 4, rawIntelligence = 3 };
            firstStats.magicResistance.SetDefaultValue(9999);
            DamageResultDto copy;
            using (var stream = new MemoryStream(ProtocolCodec.Encode(high, true)))
            {
                Require(NetPacket.TryRead(stream, out short id, out byte[] payload), "Damage packet framing");
                copy = (DamageResultDto)ProtocolCodec.Decode(id, payload, true);
            }
            Require(copy.resultId == 100 && copy.roomSessionId == room && copy.authorityTick == 20 && copy.targetStateVersion == 2,
                "Inherited common envelope and damage fields serialize through the only codec");
            Require(copy.components.physicalDamage == 3 && copy.components.magicDamage == 7 && copy.components.rawLightning == 4,
                "Mixed attack components and A's elemental inputs survive the sole codec");
            var saved = UnityEngine.Random.state;
            float expected = UnityEngine.Random.value; UnityEngine.Random.state = saved;
            Require(receiver.ApplyConfirmedResult(copy) && !receiver.ApplyConfirmedResult(copy), "Exactly one acceptance for the same result ID");
            var feedback = first.GetComponent<NetworkHitFeedback>();
            Require(feedback.PresentationCount == 1 && feedback.LastResultId == copy.resultId && !feedback.IsAuthorityKnockbackActive &&
                !displayBody.simulated && displayBody.velocity == Vector2.zero, "One main feedback batch per result ID; B cannot write or simulate knockback");
            Require(copy.impact.knockbackTicks == 4 && copy.impact.reactionTicks == 10 && copy.impact.facingDir == -1 && copy.impact.knockbackVelocity[0] == -2,
                "A's impact/knockback metadata round-trips through the unique DTO and codec");
            Require(UnityEngine.Random.value == expected, "B never rolls damage/evasion/critical"); UnityEngine.Random.state = saved;
            Require(firstStats.currentHealth == 90 && firstStats.GetMaxHealthValue() == 100 && a.Health == 90, "B uses confirmed HP/max, ignoring its local armor/evasion/max-health data");
            Require(receiver.Receipts.TryAccept(new OtherResult { roomSessionId = room, resultId = 900, authorityTick = 30 }), "Another/private result stream may have a higher ID");
            Require(receiver.ApplyConfirmedResult(Damage(room, 7, 202, 2, 19, 25, 75)) && b.Health == 75,
                "A smaller ID for another target is accepted immediately despite holes and another stream's ID 900");
            Require(receiver.ApplyConfirmedResult(Damage(room, 101, 201, 1, 22, 5, 95)) && a.Health == 90,
                "A new result ID is handled, but an older target health version cannot overwrite current HP");
            Require(receiver.ApplyConfirmedResult(Damage(room, 102, 201, 3, 19, 10, 80)) && a.Health == 90,
                "An older authority tick cannot override HP even with a larger version");
            Require(feedback.ReactionStartTick == 20 && feedback.ReactionVersion == 2 && feedback.PresentationCount == 3,
                "New IDs can show their number, but older tick/version cannot rewind an existing hurt reaction");
            Require(receiver.ApplyConfirmedResult(Damage(room, 103, 201, 3, 23, 10, 80)) && a.Health == 80,
                "A new version/tick applies the final value, never subtracting damage twice");
            Require(receiver.ApplyConfirmedResult(Damage(room, 104, 201, 4, 23, 10, 70)) && a.Health == 70,
                "Multiple hits in one authority tick sort by target version");
            Require(feedback.ReactionVersion == 4 && feedback.ReactionStartTick == 23 && feedback.IsReactingAt(23) &&
                !feedback.IsReactingAt(22) && !feedback.IsReactingAt(33), "Hurt timing uses A ticks, including same-tick hits and the exclusive end");
            Require(!a.ApplyVersionedHealth(room, 25, 1, 100, 100) && !a.ApplyHealth(room, 26, 99999, 100, 100),
                "An old baseline and the legacy health/stateSeq stream cannot undo damage");
            Require(a.ApplyVersionedHealth(room, 25, 4, 70, 100), "An equal-version, equal-value baseline can establish a later tick barrier");
            Require(receiver.ApplyConfirmedResult(Damage(room, 105, 201, 5, 24, 10, 60)) && a.Health == 70, "Baseline tick barrier rejects older final values");
            Require(!receiver.ApplyConfirmedResult(Damage(Guid.NewGuid().ToString("N"), 106, 201, 5, 26, 10, 60)), "Foreign room results cannot mutate health");
            var invalid = Damage(room, 106, 201, 5, 26, 10, 60); invalid.evaded = true;
            ExpectInvalid(() => ProtocolCodec.Encode(invalid, true));
            invalid = Damage(room, 106, 201, 5, 26, 10, 60); invalid.targetStateVersion = 0;
            ExpectInvalid(() => ProtocolCodec.Encode(invalid, true));
            invalid = Damage(room, 106, 201, 5, 26, 10, 60); invalid.impact.knockbackVelocity[0] = float.NaN;
            ExpectInvalid(() => ProtocolCodec.Encode(invalid, true));
            invalid = Damage(room, 106, 201, 5, 26, 10, 60); invalid.impact.reactionTicks = 0;
            ExpectInvalid(() => ProtocolCodec.Encode(invalid, true));
            invalid = Damage(room, 106, 201, 5, 26, 10, 60); invalid.components.magicDamage = 1;
            ExpectInvalid(() => ProtocolCodec.Encode(invalid, true));
            invalid = Damage(room, 106, 201, 5, 26, 10, 60); invalid.components.rawFire = -1;
            ExpectInvalid(() => ProtocolCodec.Encode(invalid, true));
            ExpectInvalid(() => ProtocolCodec.Encode(copy));
            context.Dispose();
            Require(!receiver.ApplyConfirmedResult(Damage(room, 106, 201, 5, 26, 10, 60)), "Expired session cannot accept late results");
        }
    }

    private enum AuthorityDamageCase { Physical, Mixed, Resisted, ArmorBlocked, MagicOverkill, Evaded, Invincible, CriticalMixed, Chilled, Shocked }
    private static void VerifyAuthority(Scene scene, AuthorityDamageCase kind, bool verifyBurn = false)
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
            if (kind != AuthorityDamageCase.Physical)
            {
                stats.fireDamage.SetDefaultValue(kind == AuthorityDamageCase.MagicOverkill ? 1000 : 10);
                stats.iceDamage.SetDefaultValue(6); stats.lightningDamage.SetDefaultValue(4); stats.intelligence.SetDefaultValue(3);
            }
            if (kind == AuthorityDamageCase.CriticalMixed) stats.critChance.SetDefaultValue(100);
            if (kind == AuthorityDamageCase.Chilled)
            { stats.fireDamage.SetDefaultValue(0); stats.iceDamage.SetDefaultValue(10); stats.lightningDamage.SetDefaultValue(0); stats.intelligence.SetDefaultValue(0); }
            if (kind == AuthorityDamageCase.Shocked)
            {
                stats.fireDamage.SetDefaultValue(0); stats.iceDamage.SetDefaultValue(0); stats.lightningDamage.SetDefaultValue(30); stats.intelligence.SetDefaultValue(0);
                typeof(CharaterStats).GetField("shockStrikePrefab", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(stats,
                    AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ShockStrike.prefab"));
            }
            Set(typeof(Entity), source, "stats", stats); Set(typeof(Player), source, "IsRuntimeInitialized", true);
            Set(typeof(CharaterStats), stats, "IsRuntimeInitialized", true); stats.currentHealth = 140;
            typeof(PlayerCombatContext).GetMethod("BindPrimaryAttack", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(source.Combat, new object[] { objects });
            sourceObject.SetActive(true);
            var targetObject = Body(scene, "Authority damage target"); var authorityBody = targetObject.AddComponent<Rigidbody2D>();
            var target = targetObject.AddComponent<Enemy>();
            target.moveSpeed = 10;
            typeof(Entity).GetField("knockbackPower", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, new Vector2(2, 2));
            typeof(Entity).GetField("knockbackDuration", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, .07f);
            var targetStats = targetObject.AddComponent<EnemyStats>(); FillStats(targetStats);
            targetStats.soulsDropAmount = new Stat(); targetStats.armor.SetDefaultValue(2);
            if (kind != AuthorityDamageCase.Physical) { targetStats.magicResistance.SetDefaultValue(5); targetStats.intelligence.SetDefaultValue(1); }
            if (kind == AuthorityDamageCase.Resisted) targetStats.magicResistance.SetDefaultValue(100);
            if (kind == AuthorityDamageCase.ArmorBlocked) targetStats.armor.SetDefaultValue(100);
            if (kind == AuthorityDamageCase.Evaded) targetStats.evasion.SetDefaultValue(100);
            if (kind == AuthorityDamageCase.Chilled) targetStats.armor.SetDefaultValue(10);
            var entry = new RoomSceneMonsters.SceneEntry { Key = "target", Type = typeof(Enemy).FullName, Enemy = target, Active = true };
            var entries = new System.Collections.Generic.List<RoomSceneMonsters.SceneEntry> { entry };
            if (kind == AuthorityDamageCase.Shocked)
                for (int i = 0; i < 2; i++)
                {
                    var go = Body(scene, "Chain candidate " + i); var other = go.AddComponent<Enemy>(); var otherStats = go.AddComponent<EnemyStats>();
                    FillStats(otherStats); otherStats.soulsDropAmount = new Stat(); otherStats.armor.SetDefaultValue(999); otherStats.magicResistance.SetDefaultValue(999); otherStats.evasion.SetDefaultValue(100);
                    go.AddComponent<BoxCollider2D>().size = Vector2.one * .2f;
                    entries.Add(new RoomSceneMonsters.SceneEntry { Key = "z-target-" + i, Type = typeof(Enemy).FullName, Enemy = other, Active = true,
                        Position = new Vector2(i == 0 ? 2 : -2, 0) });
                }
            using (var monsters = new RoomSceneMonsters(entries.ToArray(), objects))
            {
                int nextEntity = 200; monsters.BindAuthority(context, () => ++nextEntity);
                foreach (var e in entries) e.Enemy.enabled = true;
                if (kind == AuthorityDamageCase.Invincible) targetStats.MakeInvincible(true);
                bool noImpact = kind == AuthorityDamageCase.Evaded || kind == AuthorityDamageCase.Invincible;
                int physical = noImpact || kind == AuthorityDamageCase.ArmorBlocked ? 0 : kind == AuthorityDamageCase.CriticalMixed ? 26 : kind == AuthorityDamageCase.Chilled ? 8 : 16;
                int magic = kind == AuthorityDamageCase.Mixed || kind == AuthorityDamageCase.ArmorBlocked || kind == AuthorityDamageCase.CriticalMixed ? 15 : kind == AuthorityDamageCase.MagicOverkill ? 84 : kind == AuthorityDamageCase.Chilled ? 2 : kind == AuthorityDamageCase.Shocked ? 22 : 0;
                int expectedHealth = 100 - physical - magic;
                bool shouldBurn = kind == AuthorityDamageCase.Mixed || kind == AuthorityDamageCase.Resisted || kind == AuthorityDamageCase.ArmorBlocked || kind == AuthorityDamageCase.CriticalMixed;
                bool shouldChill = kind == AuthorityDamageCase.Chilled;
                bool shouldShock = kind == AuthorityDamageCase.Shocked;
                long expectedResults = shouldBurn || shouldChill || shouldShock ? 3 : 2;
                int publications = 0; DamageResultDto sent = null;
                var burnStates = new System.Collections.Generic.List<ElementStatesDto>();
                using (var service = new RoomCombatDamage(context, monsters, sequence, value => { publications++; sent = value; }, burnStates.Add))
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
                    Require(publications == 1 && sequence.LastAllocated == expectedResults && sent.resultId == 2 && sent.attackInstanceId == 1 &&
                        targetStats.currentHealth == expectedHealth && sent.actualDamage == physical + magic && sent.targetStateVersion == 2 &&
                        sent.components.physicalDamage == physical && sent.components.magicDamage == magic,
                        "A computes damage once, applies HP once and uses the shared result ID independent of request/attack IDs");
                    Require(context.TryGetEntity(201, out var view) && view.Health == expectedHealth && view.HealthVersion == 2 &&
                        targetStats.isIgnited == shouldBurn && targetStats.isChilled == shouldChill && targetStats.isShocked == shouldShock, "A selects one supported elemental state");
                    var feedback = target.GetComponent<NetworkHitFeedback>();
                    Require(feedback.PresentationCount == 1 && (noImpact ? sent.impact == null && !feedback.IsAuthorityKnockbackActive :
                        sent.impact != null && feedback.IsAuthorityKnockbackActive && authorityBody.velocity == new Vector2(2, 2)),
                        "The real authority damage entry starts exactly one hit feedback and applies original target knockback away from the source");
                    if (!noImpact) Require(feedback.AdvanceAuthorityKnockback(12) && !feedback.AdvanceAuthorityKnockback(12 + (ulong)sent.impact.knockbackTicks) &&
                        authorityBody.velocity == new Vector2(0, 2), "A's non-scaled tick alone ends knockback; vertical physics velocity is preserved");
                    Require(service.RepeatLastResultForVerification() && publications == 2 && feedback.PresentationCount == 1 &&
                        !feedback.IsAuthorityKnockbackActive && targetStats.currentHealth == expectedHealth && sequence.LastAllocated == expectedResults,
                        "The manual replay entry resends the same result without A replaying feedback, knockback, damage or allocating a new ID");
                    if (shouldShock)
                    {
                        Physics2D.SyncTransforms();
                        Require(monsters.FindShockTarget(target) == entries[1].Enemy && burnStates[0].effects[0].tickDamage == 3,
                            "A excludes the origin and breaks equal-distance ties by entity ID; original lightning*10% formula is frozen");
                        entries[1].Enemy.GetComponent<EnemyStats>().currentHealth = 0;
                        Require(monsters.FindShockTarget(target) == entries[2].Enemy, "Dead candidate is excluded without retargeting a launched strike");
                        entries[1].Enemy.GetComponent<EnemyStats>().currentHealth = 100;
                        var guestObject = Body(scene, "Authority B chain trigger"); var guest = guestObject.AddComponent<Player>(); var guestStats = guestObject.AddComponent<PlayerStats>(); FillStats(guestStats);
                        context.RegisterEntity(102, 2, guest.transform);
                        objects.Register(room, 2, guestObject, () =>
                        {
                            typeof(PlayerCombatContext).GetMethod("ReleaseRoomEquipment", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(guest.Combat, null);
                            context.RemoveEntity(102);
                        });
                        RoomCombatConfiguration.InitializeAuthority(guest, false);
                        guestStats.strength.SetDefaultValue(0); guestStats.damage.SetDefaultValue(0); guestStats.lightningDamage.SetDefaultValue(30);
                        Set(typeof(Entity), guest, "stats", guestStats); Set(typeof(Player), guest, "IsRuntimeInitialized", true);
                        Set(typeof(CharaterStats), guestStats, "IsRuntimeInitialized", true); guestStats.currentHealth = 180; guestObject.SetActive(true);
                        typeof(PlayerCombatContext).GetMethod("BindPrimaryAttack", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(guest.Combat, new object[] { objects });
                        var guestAttack = guest.Combat.PrimaryAttack;
                        Require(guestAttack.TryQueue(1, 60, PrimaryAttackRejection.None) && guestAttack.TryBegin(61, PrimaryAttackRejection.None, 20, 40, 1, 6) &&
                            guestAttack.TryRegisterHit(guestAttack.Current, 1, 201, 62), "B triggers a second lightning hit against A's shocked monster");
                        var trigger = (PrimaryAttackConfirmedHit)Activator.CreateInstance(typeof(PrimaryAttackConfirmedHit), BindingFlags.Instance | BindingFlags.NonPublic,
                            null, new object[] { guestAttack.Current, 1, candidate, 62UL }, null);
                        service.OnPrimaryHit(trigger); service.OnPrimaryHit(trigger);
                        var launch = burnStates[burnStates.Count - 1];
                        Require(launch.chains.Length == 1 && launch.chains[0].sourcePlayerId == 1 && launch.chains[0].sourceEntityId == 101 &&
                            launch.chains[0].originEntityId == 201 && launch.chains[0].targetEntityId == 202,
                            "One derived strike inherits the original shock owner A even when B triggers it");
                        for (ulong t = 63; t <= 150; t++) service.AdvanceElements(t);
                        Require(sent.kind == DamageResultKind.ShockChain && sent.sourcePlayerId == 1 && sent.sourceEntityId == 101 && sent.targetEntityId == 202 &&
                            sent.actualDamage == 3 && sent.components.shockDamage == 3 && sent.finalHealth == 97 && sent.chainInstanceId == launch.chains[0].chainId &&
                            entries[1].Enemy.GetComponent<EnemyStats>().isShocked && entries[2].Enemy.GetComponent<EnemyStats>().currentHealth == 100 && targetStats.currentHealth == 40,
                            "Only A's chosen target takes one delayed chain hit; resistance/evasion do not reroll and the hit does not recursively chain");
                        var finished = burnStates[burnStates.Count - 1]; Require(finished.chains.Length == 0, "A removes the strike after original impact lifetime");
                        VerifyShockReceiver(scene, room, launch, finished, sent);
                        long last = sequence.LastAllocated; service.AdvanceElements(150); Require(sequence.LastAllocated == last, "Repeated chain simulation tick cannot settle twice");
                        Require(guestAttack.TryQueue(2, 151, PrimaryAttackRejection.None) && guestAttack.TryBegin(152, PrimaryAttackRejection.None, 20, 40, 1, 6) &&
                            guestAttack.TryRegisterHit(guestAttack.Current, 1, 201, 153), "Admit another chain trigger");
                        trigger = (PrimaryAttackConfirmedHit)Activator.CreateInstance(typeof(PrimaryAttackConfirmedHit), BindingFlags.Instance | BindingFlags.NonPublic,
                            null, new object[] { guestAttack.Current, 1, candidate, 153UL }, null);
                        service.OnPrimaryHit(trigger); Require(burnStates[burnStates.Count - 1].chains.Length == 1, "Second strike has its own chain instance");
                        context.RemoveEntity(202); service.AdvanceElements(154);
                        Require(burnStates[burnStates.Count - 1].chains.Length == 0 && entries[2].Enemy.GetComponent<EnemyStats>().currentHealth == 100,
                            "A canceled target cannot redirect the already selected chain to another monster");
                    }
                    if (shouldChill)
                    {
                        var start = burnStates[0]; var chill = start.effects[0];
                        Require(chill.kind == ElementEffectKind.Chill && target.moveSpeed == 8 && targetStats.armor.GetValue() == 10,
                            "Shared component slows A movement; armor reduction is applied in the shared formula without mutating the base stat");
                        service.PublishElementBaseline(12);
                        Require(target.moveSpeed == 8 && burnStates[1].effects[0].effectId == chill.effectId && burnStates[1].effects[0].endTick == chill.endTick,
                            "Repeated snapshots preserve effect lifetime and do not stack slow");
                        Require(runtime.TryQueue(101, 60, PrimaryAttackRejection.None) && runtime.TryBegin(61, PrimaryAttackRejection.None, 20, 40, 1, 6) &&
                            runtime.TryRegisterHit(runtime.Current, 1, 201, 62), "Admit second strike against a chilled target");
                        var secondHit = (PrimaryAttackConfirmedHit)Activator.CreateInstance(typeof(PrimaryAttackConfirmedHit), BindingFlags.Instance | BindingFlags.NonPublic,
                            null, new object[] { runtime.Current, 1, candidate, 62UL }, null);
                        service.OnPrimaryHit(secondHit);
                        Require(sent.components.physicalDamage == 10 && targetStats.currentHealth == 78 && burnStates.Count == 2,
                            "Next strike uses 80% armor; an already chilled target does not refresh or stack the effect");
                        VerifyChillReceiver(scene, room, start);
                        service.AdvanceElements(chill.endTick);
                        Require(!targetStats.isChilled && target.moveSpeed == 10 && targetStats.currentHealth == 78,
                            "A ends chill without periodic damage and restores the original movement exactly");
                        Require(DamageCalculations.ApplyChilledArmor(targetStats.armor.GetValue(), targetStats.isChilled) == 10, "Armor penalty ends without a residual modifier");
                    }
                    if (verifyBurn)
                    {
                        var start = burnStates[0];
                        Require(start.effects[0].tickDamage == 2 && start.effects[0].active, "Burn freezes the original fire*20% tick damage");
                        service.AdvanceElements(12); Require(targetStats.currentHealth == expectedHealth, "No tick at the application instant");
                        service.AdvanceElements(13); var first = sent;
                        Require(first.kind == DamageResultKind.Burn && first.components.burnDamage == 2 && first.finalHealth == expectedHealth - 2 && first.impact == null,
                            "A's first following simulation step settles periodic damage without a new melee impact");
                        long last = sequence.LastAllocated; service.AdvanceElements(13); Require(sequence.LastAllocated == last, "Repeated simulation tick cannot deal another burn tick");
                        service.PublishElementBaseline(13);
                        var baseline = burnStates[burnStates.Count - 1];
                        Require(baseline.effects[0].effectId == start.effects[0].effectId && baseline.effects[0].endTick == start.effects[0].endTick,
                            "Rejoin baseline retains the effect instance and original expiry rather than starting a fresh duration");
                        var state = start.effects[0];
                        ulong end = state.endTick;
                        int count = checked((int)((end - state.startTick - 2) / (ulong)state.intervalTicks + 1));
                        service.AdvanceElements(end);
                        Require(!targetStats.isIgnited && sent.effectTick == count && targetStats.currentHealth == expectedHealth - 2 * count &&
                            !burnStates[burnStates.Count - 1].effects[0].active, "All due ticks precede the exclusive A end, which clears the burn");
                        VerifyBurnReceiver(scene, room, start, baseline, burnStates[burnStates.Count - 1], first, sent);
                        last = sequence.LastAllocated; service.AdvanceElements(end + 1); Require(sequence.LastAllocated == last, "No damage or end replay after expiry");
                        Require(runtime.TryQueue(101, end + 2, PrimaryAttackRejection.None) && runtime.TryBegin(end + 3, PrimaryAttackRejection.None, 20, 40, 1, 6), "A new strike can ignite again after expiry");
                        Require(runtime.TryRegisterHit(runtime.Current, 1, 201, end + 4), "Admit the new ignition hit");
                        var again = (PrimaryAttackConfirmedHit)Activator.CreateInstance(typeof(PrimaryAttackConfirmedHit), BindingFlags.Instance | BindingFlags.NonPublic,
                            null, new object[] { runtime.Current, 1, candidate, end + 4 }, null);
                        service.OnPrimaryHit(again); Require(targetStats.isIgnited, "New post-expiry hit starts a fresh burn");
                        service.RemovePlayerElements(1, end + 5); Require(!targetStats.isIgnited, "Leaving source player clears active burns");
                        last = sequence.LastAllocated; int hp = targetStats.currentHealth; service.AdvanceElements(end + 300);
                        Require(sequence.LastAllocated == last && targetStats.currentHealth == hp, "Removed source's effect cannot keep dealing ghost damage");
                    }
                }
            }
            sequence.Dispose();
            bool rejected = false; try { sequence.Allocate(); } catch (ObjectDisposedException) { rejected = true; }
            Require(rejected, "A closed room's result source cannot allocate late IDs");
        }
    }

    private static void VerifyShockReceiver(Scene scene, string room, ElementStatesDto launch, ElementStatesDto finished, DamageResultDto hit)
    {
        using (var context = Context(room, true))
        using (var receiver = new RoomCombatDamage(context, null))
        {
            var source = Body(scene, "Shock source display"); context.RegisterEntity(101, 1, source.transform);
            var origin = Body(scene, "Shock origin display"); var originStats = origin.AddComponent<CharaterStats>(); FillStats(originStats); context.RegisterEntity(201, 0, origin.transform);
            var target = Body(scene, "Shock destination display"); var stats = target.AddComponent<CharaterStats>(); FillStats(stats);
            context.RegisterEntity(202, 0, target.transform).ApplyVersionedHealth(room, 1, 1, 100, 100);
            ElementStatesDto copy;
            using (var stream = new MemoryStream(ProtocolCodec.Encode(launch, true)))
            { Require(NetPacket.TryRead(stream, out short id, out var data), "Chain framing"); copy = (ElementStatesDto)ProtocolCodec.Decode(id, data, true); }
            Require(receiver.ApplyElementStates(copy) && !receiver.ApplyElementStates(copy) && originStats.isShocked && stats.currentHealth == 100 &&
                copy.chains[0].targetEntityId == 202, "B receives the exact A-selected chain destination, never local target search or damage");
            Require(receiver.ApplyElementStates(finished), "A's full state removes finished chain views");
            Require(receiver.ApplyConfirmedResult(hit) && !receiver.ApplyConfirmedResult(hit) && stats.currentHealth == 97,
                "A chain HP result below a later state result ID still applies exactly once");
            var invalid = copy.chains[0]; invalid.targetEntityId = invalid.originEntityId;
            ExpectInvalid(() => ProtocolCodec.Encode(copy, true));
            var controllerObject = Body(scene, "Network chain callback guard"); var controller = controllerObject.AddComponent<ShockStrikeController>();
            typeof(ShockStrikeController).GetField("targetStats", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, stats);
            typeof(ShockStrikeController).GetField("networkView", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, true);
            typeof(ShockStrikeController).GetMethod("DamageAndSelfDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            Require(stats.currentHealth == 97, "Display strike callback cannot call legacy damage");
        }
    }

    private static void VerifyChillReceiver(Scene scene, string room, ElementStatesDto start)
    {
        using (var context = Context(room, false))
        {
            var go = Body(scene, "Chill authority player"); var player = go.AddComponent<Player>();
            var stats = go.AddComponent<PlayerStats>(); FillStats(stats); context.RegisterEntity(101, 1, player.transform);
            player.moveSpeed = 8; player.jumpForce = 12; player.dashSpeed = 40;
            var component = go.AddComponent<NetworkElementState>();
            var state = new ElementStateDto { kind = ElementEffectKind.Chill, active = true,
                movementMultiplier = DamageCalculations.ChillMultiplier, armorMultiplier = DamageCalculations.ChillMultiplier };
            component.ApplyConfirmed(state); component.ApplyConfirmed(state);
            Require(Mathf.Approximately(player.moveSpeed, 6.4f) && Mathf.Approximately(player.jumpForce, 9.6f) && player.dashSpeed == 32,
                "The shared A component applies original player move/jump/dash multipliers once");
            component.ApplyConfirmed(null); component.ApplyConfirmed(null);
            Require(player.moveSpeed == 8 && player.jumpForce == 12 && player.dashSpeed == 40 && !stats.isChilled,
                "Shared cleanup restores all player parameters exactly and is idempotent");
        }
        using (var context = Context(room, true))
        using (var receiver = new RoomCombatDamage(context, null))
        {
            var target = Body(scene, "Chill display target"); var enemy = target.AddComponent<Enemy>(); enemy.moveSpeed = 10;
            var stats = target.AddComponent<CharaterStats>(); FillStats(stats); context.RegisterEntity(201, 0, target.transform);
            ElementStatesDto copy;
            using (var stream = new MemoryStream(ProtocolCodec.Encode(start, true)))
            { Require(NetPacket.TryRead(stream, out short id, out var data), "Chill framing"); copy = (ElementStatesDto)ProtocolCodec.Decode(id, data, true); }
            Require(receiver.ApplyElementStates(copy) && !receiver.ApplyElementStates(copy) && stats.isChilled && enemy.moveSpeed == 10,
                "B uses the common component to display chill without changing its own movement parameters");
            receiver.AdvanceElements(copy.effects[0].endTick + 1); Require(stats.isChilled, "B cannot expire chill on a local timer");
            var s = copy.effects[0];
            var end = new ElementStatesDto { roomSessionId = room, authorityTick = s.endTick, resultId = copy.resultId + 100,
                stateSeq = copy.stateSeq + 1, effects = new[] { new ElementStateDto { effectId = s.effectId, kind = s.kind,
                    movementMultiplier = s.movementMultiplier, armorMultiplier = s.armorMultiplier, sourcePlayerId = s.sourcePlayerId,
                    sourceEntityId = s.sourceEntityId, targetEntityId = s.targetEntityId, startTick = s.startTick, endTick = s.endTick,
                    intervalTicks = s.intervalTicks } } };
            Require(receiver.ApplyElementStates(end) && !stats.isChilled && enemy.moveSpeed == 10, "A-confirmed end clears B state");
            copy.resultId = end.resultId + 1;
            Require(!receiver.ApplyElementStates(copy) && !stats.isChilled, "A fresh result ID with an older state sequence cannot resurrect chill");
            copy.effects[0].movementMultiplier = .5f; ExpectInvalid(() => ProtocolCodec.Encode(copy, true));
        }
    }

    private static void VerifyBurnReceiver(Scene scene, string room, ElementStatesDto start, ElementStatesDto baseline, ElementStatesDto end, DamageResultDto first, DamageResultDto last)
    {
        using (var context = Context(room, true))
        using (var receiver = new RoomCombatDamage(context, null))
        {
            var source = Body(scene, "Burn display source"); context.RegisterEntity(101, 1, source.transform);
            var target = Body(scene, "Burn display target"); var stats = target.AddComponent<CharaterStats>(); FillStats(stats);
            var view = context.RegisterEntity(201, 0, target.transform); view.ApplyVersionedHealth(room, 1, 1, 100, 100);
            ElementStatesDto copy;
            using (var stream = new MemoryStream(ProtocolCodec.Encode(start, true)))
            { Require(NetPacket.TryRead(stream, out short id, out var data), "Burn framing"); copy = (ElementStatesDto)ProtocolCodec.Decode(id, data, true); }
            Require(receiver.ApplyElementStates(copy) && stats.isIgnited && stats.currentHealth == 100 && !receiver.ApplyElementStates(copy), "B only displays the state; exact start replay does not restart it");
            receiver.AdvanceElements(start.effects[0].endTick + 100); Require(stats.currentHealth == 100 && stats.isIgnited, "B never expires or ticks burn locally");
            Require(receiver.ApplyConfirmedResult(first) && !receiver.ApplyConfirmedResult(first), "Periodic HP/number result is applied exactly once");
            Require(receiver.ApplyElementStates(end) && !stats.isIgnited && !receiver.ApplyElementStates(start), "A's end tombstone rejects an older start");
            Require(!receiver.ApplyElementStates(baseline) && !stats.isIgnited, "A previously unseen old baseline cannot resurrect an ended burn");
            Require(receiver.ApplyConfirmedResult(last) && stats.currentHealth == last.finalHealth, "A lower result ID in the health stream is still valid after the burn-state end");
        }
    }
    private sealed class OtherResult : AuthorityResultDto { }
    private static DamageResultDto Damage(string room, long id, int target, long version, ulong tick, int damage, int health) => new DamageResultDto
    { roomSessionId = room, resultId = id, authorityTick = tick, sourcePlayerId = 1, sourceEntityId = 101, targetEntityId = target,
        attackInstanceId = 500, hitSegmentId = 1, actualDamage = damage, components = new DirectDamageDto { physicalDamage = damage }, finalHealth = health, maxHealth = 100, targetStateVersion = version,
        impact = new DamageImpactDto { position = new[] { 3f, 2f }, facingDir = -1, knockbackVelocity = new[] { -2f, 2f }, knockbackTicks = 4, reactionTicks = 10 } };
    private static void VerifyFeedbackAssets()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy Skeleton.prefab");
        Require(prefab != null, "Existing monster prefab available");
        var fx = prefab.GetComponentInChildren<EntityFX>(true); Require(fx != null, "Monster has the existing FX entry");
        var data = new SerializedObject(fx);
        foreach (string field in new[] { "hitFx", "criticalHitFx", "popUpTextPrefab" })
        {
            var effect = data.FindProperty(field).objectReferenceValue as GameObject;
            Require(effect != null && (field == "popUpTextPrefab" ? effect.GetComponent<PopUpTextFX>() != null : effect.GetComponent<HitEffectFX>() != null),
                "Original " + field + " supports third-stage pool playback");
        }
        bool hurt = false;
        foreach (var clip in prefab.GetComponentInChildren<Animator>(true).runtimeAnimatorController.animationClips)
            if (clip.name == "SkeletonStunned") { hurt = true; Require(clip.events.Length == 0, "Reused hurt pose has no damage events"); }
        Require(hurt, "Existing hurt pose is available without entering the counter gameplay state");
    }
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
