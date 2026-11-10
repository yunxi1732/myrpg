using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Rpg.Networking;
using Rpg.Networking.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MonsterAttackDamageVerification
{
    [MenuItem("Tools/RPG/Verify Monster Attack Damage (4-36)")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play mode.");
        var previous = SceneManager.GetActiveScene();
        var random = UnityEngine.Random.state;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        RoomPresentationContext context = null;
        RoomNetworkObjects objects = null;
        RoomSceneMonsters monsters = null;
        RoomCombatDamage damage = null;
        AuthorityResultSequence results = null;
        Exception failure = null;
        try
        {
            Physics2D.SyncTransforms();
            Vector2 maximum = Vector2.zero;
            foreach (var collider in UnityEngine.Object.FindObjectsOfType<Collider2D>())
                if (collider.enabled && collider.gameObject.scene != scene) maximum = Vector2.Max(maximum, collider.bounds.max);
            var root = new GameObject("Monster damage verification"); SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = new Vector3(maximum.x + 32, maximum.y + 32);
            string room = Guid.NewGuid().ToString("N");
            context = Context(room, false); objects = new RoomNetworkObjects(room, scene); results = new AuthorityResultSequence(room);
            var sourceObject = Body(root.transform, "Authority monster", Vector2.zero);
            var source = sourceObject.AddComponent<EnemySkeleton>();
            var sourceStats = sourceObject.AddComponent<EnemyStats>(); Fill(sourceStats, 100); sourceStats.soulsDropAmount = new Stat();
            sourceStats.damage.SetDefaultValue(30); Set(typeof(Entity), source, "stats", sourceStats);
            var check = Body(source.transform, "Attack check", new Vector2(.4f, 0)); check.SetActive(true);
            source.attackCheck = check.transform; source.attackCheckRadius = 1.2f;
            typeof(Entity).GetField("whatIsGround", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(source, (LayerMask)(1 << 3));
            monsters = new RoomSceneMonsters(new[] { new RoomSceneMonsters.SceneEntry
                { Key = "monster", Type = typeof(EnemySkeleton).FullName, Enemy = source, Active = true, Position = source.transform.position } }, objects);
            monsters.BindAuthority(context, () => 201); source.enabled = true;
            InvokeInternal(source.GetComponent<NetworkEntityIdentity>(), "ReleaseAuthoritativeMovement");
            var a = MakePlayer(root.transform, context, 101, 1, 100, 3, new Vector2(.75f, 0));
            var b = MakePlayer(root.transform, context, 102, 2, 140, 6, new Vector2(.95f, 0));
            var extra = Body(a.transform, "Second player collider", Vector2.zero); extra.AddComponent<BoxCollider2D>().size = Vector2.one * .2f; extra.SetActive(true);
            source.ConfigureNetworkMovement(() => new[] { a, b });
            var machine = new EnemyStateMachine(); var attackState = new SkeletonAttackState(source, machine, "Attack", source);
            Set(typeof(Enemy), source, "stateMachine", machine); Set(typeof(EnemySkeleton), source, "attackState", attackState);
            Set(typeof(EnemyStateMachine), machine, "currentState", attackState); // Data fixture; never call native Animator/lifecycle methods in edit mode.
            var sent = new List<DamageResultDto>(); damage = new RoomCombatDamage(context, monsters, results, sent.Add);
            monsters.BindDamageService(damage); monsters.BindDamageService(damage);
            var hits = source.NetworkHits;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy Skeleton.prefab");
            Require(EnemySkeleton.TryNetworkAttackTiming(prefab.GetComponentInChildren<Animator>(true), out var timing), "Authored SkeletonAttack has valid hit markers");
            InvokeInternal(hits, "Begin", 100UL, 1, timing); var instance = hits.Current;
            var segment = instance.Segments[0];
            Physics2D.SyncTransforms(); hits.Step(segment.StartTick - 1); Require(sent.Count == 0, "Wind-up cannot damage players");
            // Hold one target behind, then out of range, then behind a real solid wall during the same open window.
            var candidates = new List<Player>();
            Action query = () => { candidates.Clear(); Physics2D.SyncTransforms(); AuthorityMeleeQuery.Collect<Player>(source, damage.IsValidPlayerTarget, (p, c, id) => candidates.Add(p)); };
            b.transform.localPosition = new Vector2(-.8f, 0); query(); Require(!candidates.Contains(b), "Facing excludes the rear player");
            b.transform.localPosition = new Vector2(5, 0); query(); Require(!candidates.Contains(b), "Distance/collision excludes distant player");
            b.transform.localPosition = new Vector2(.95f, 0);
            var wall = Body(root.transform, "Solid ground", new Vector2(.35f, 0)); wall.layer = 3;
            wall.AddComponent<BoxCollider2D>().size = new Vector2(.1f, 3); wall.SetActive(true); query(); Require(candidates.Count == 0, "Ground blocks both targets");
            wall.SetActive(false); query(); Require(candidates.Contains(a) && candidates.Contains(b), "Both registered living player entities are valid contacts");
            MonsterAttackConfirmedHit firstHit = null; hits.HitConfirmed += value => { if (firstHit == null) firstHit = value; };
            hits.Step(segment.StartTick); hits.Step(segment.StartTick); hits.Step(segment.EndTick - 1);
            Require(sent.Count == 2 && a.stats.currentHealth == 73 && b.stats.currentHealth == 116 && instance.AcceptedHitCount == 2,
                "A and B use independent armor/HP; parent+child colliders, repeated samples and double binding cannot double damage");
            Require(sent.TrueForAll(value => value.sourcePlayerId == 0 && value.sourceEntityId == 201 && value.attackInstanceId == instance.AttackInstanceId && value.hitSegmentId == 1),
                "Monster source and exact target IDs use the existing damage result envelope");
            damage.OnMonsterHit(firstHit); Require(sent.Count == 2 && results.LastAllocated == 2, "Replayed admitted hit consumes no new result or HP");
            VerifyDisplay(scene, room, sent);
            hits.Step(segment.EndTick); Require(sent.Count == 2, "Window end is exclusive");
            a.stats.currentHealth = 0; Require(!damage.IsValidPlayerTarget(a), "Zero-health players are excluded");
            a.stats.currentHealth = 73;
            InvokeInternal(hits, "Begin", 200UL, 1, timing); Require(hits.Current.AttackInstanceId > instance.AttackInstanceId && instance.IsClosed, "New monster strike has a fresh room melee instance");
            context.RemoveEntity(102); Require(!damage.IsValidPlayerTarget(b), "Departed player cannot be hit through an old collider");
            sourceStats.currentHealth = 0; hits.Step(hits.Current.HitStartTick); Require(hits.Current == null && sent.Count == 2, "Dead monster cannot finish an old damage window");
        }
        catch (Exception error) { failure = error; }
        finally
        {
            Cleanup(() => damage?.Dispose(), ref failure); Cleanup(() => monsters?.Dispose(), ref failure);
            Cleanup(() => objects?.Dispose(), ref failure); Cleanup(() => context?.Dispose(), ref failure); Cleanup(() => results?.Dispose(), ref failure);
            UnityEngine.Random.state = random;
            Cleanup(() => { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }, ref failure);
            Cleanup(() => EditorSceneManager.CloseScene(scene, true), ref failure);
        }
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Debug.Log("[MonsterDamage 4-36] PASS: authored windows, actual collision/facing/range/wall checks, A/B independent HP/armor, per-instance+segment+target dedup, common result IDs, display result replay/no RNG/no animation damage, dead/departed guards. Dual-instance acceptance remains required.");
    }

    private static void VerifyDisplay(Scene scene, string room, List<DamageResultDto> sent)
    {
        using (var context = Context(room, true))
        using (var receiver = new RoomCombatDamage(context, null))
        {
            var root = new GameObject("Display verification"); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, scene);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy Skeleton.prefab");
            var monster = UnityEngine.Object.Instantiate(prefab, root.transform); context.RegisterEntity(201, 0, monster.transform);
            var a = MakePlayer(root.transform, context, 101, 1, 100, 999, Vector2.zero);
            var b = MakePlayer(root.transform, context, 102, 2, 140, 999, Vector2.zero);
            var saved = UnityEngine.Random.state; float expected = UnityEngine.Random.value; UnityEngine.Random.state = saved;
            foreach (var result in sent)
            {
                DamageResultDto copy;
                using (var stream = new MemoryStream(ProtocolCodec.Encode(result, true)))
                { Require(NetPacket.TryRead(stream, out short id, out var bytes), "Damage framing"); copy = (DamageResultDto)ProtocolCodec.Decode(id, bytes, true); }
                Require(receiver.ApplyConfirmedResult(copy) && !receiver.ApplyConfirmedResult(copy), "B accepts each monster damage result once");
            }
            Require(UnityEngine.Random.value == expected && a.stats.currentHealth == 73 && b.stats.currentHealth == 116, "B applies final HP independent of local armor and does not roll");
            UnityEngine.Random.state = saved;
            typeof(EnemySkeletonAnimatorTriggers).GetMethod("AttackTrigger", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(monster.GetComponentInChildren<EnemySkeletonAnimatorTriggers>(true), null);
            Require(a.stats.currentHealth == 73 && b.stats.currentHealth == 116 && !monster.GetComponentInChildren<Animator>(true).fireEvents &&
                !a.rb.simulated && !b.rb.simulated && a.GetComponent<NetworkHitFeedback>().PresentationCount == 1 &&
                b.GetComponent<NetworkHitFeedback>().PresentationCount == 1, "B monster event cannot deal damage and B cannot simulate knockback");
        }
    }

    private static Player MakePlayer(Transform parent, RoomPresentationContext context, int entity, int playerId, int health, int armor, Vector2 position)
    {
        var go = Body(parent, "Player " + playerId, position); var body = go.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic;
        go.AddComponent<BoxCollider2D>().size = Vector2.one * .2f;
        var player = go.AddComponent<Player>(); var stats = go.AddComponent<PlayerStats>(); Fill(stats, health); stats.armor.SetDefaultValue(armor);
        Set(typeof(Entity), player, "stats", stats); Set(typeof(Entity), player, "rb", body);
        Set(typeof(Player), player, "IsRuntimeInitialized", true); Set(typeof(CharaterStats), stats, "IsRuntimeInitialized", true); stats.currentHealth = health;
        var view = context.RegisterEntity(entity, playerId, go.transform); view.ApplyVersionedHealth(context.RoomSessionId, 1, 1, health, health);
        go.SetActive(true); return player;
    }
    private static void Fill(CharaterStats stats, int health)
    {
        foreach (var field in typeof(CharaterStats).GetFields(BindingFlags.Instance | BindingFlags.Public))
            if (field.FieldType == typeof(Stat)) field.SetValue(stats, new Stat());
        stats.maxHealth.SetDefaultValue(health);
    }
    private static GameObject Body(Transform parent, string name, Vector2 position)
    { var go = new GameObject(name); go.SetActive(false); go.transform.SetParent(parent, false); go.transform.localPosition = position; return go; }
    private static RoomPresentationContext Context(string room, bool guest) => new RoomPresentationContext(room, guest ? 2 : 1, guest, Identity(room, 1, 101), Identity(room, 2, 102));
    private static RoomPlayerIdentity Identity(string room, int player, int entity) => (RoomPlayerIdentity)Activator.CreateInstance(typeof(RoomPlayerIdentity), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { room, player, entity }, null);
    private static void Set(Type type, object target, string property, object value) => type.GetProperty(property).GetSetMethod(true).Invoke(target, new[] { value });
    private static void InvokeInternal(object target, string method, params object[] arguments)
    {
        var entry = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        if (entry == null) throw new MissingMethodException(target.GetType().FullName, method);
        try { entry.Invoke(target, arguments); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }
    }
    private static void Cleanup(Action action, ref Exception failure)
    { try { action(); } catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); } }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("[MonsterDamage 4-36] FAIL: " + message); }
}
