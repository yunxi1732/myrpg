using System;
using System.Collections.Generic;
using System.Reflection;
using Rpg.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PrimaryHitWindowVerification
{
    [MenuItem("Tools/RPG/Verify Primary Hit Window (4-33a)")]
    public static void Verify()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Open MainScene outside Play mode first.");
        Scene sourceScene = SceneManager.GetSceneByPath("Assets/Scenes/MainScene.unity");
        if (!sourceScene.isLoaded) throw new InvalidOperationException("Open MainScene first.");
        Player template = RoomPlayerEntities.FindSceneTemplate(sourceScene);
        Scene previousActive = SceneManager.GetActiveScene();
        // Unity 2021 permits LocalPhysicsMode scene creation only in Play mode.
        // Keep MainScene open and use the supported editor API for this disposable additive scene.
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        RoomPresentationContext context = null;
        RoomNetworkObjects objects = null;
        RoomSceneMonsters monsters = null;
        Exception failure = null;
        try
        {
            // Editor additive scenes share 2D physics. Put fixtures beyond all loaded collision bounds
            // so existing map walls cannot affect the test's linecasts, without disabling any scene object.
            Physics2D.SyncTransforms();
            var root = new GameObject("Primary hit verification fixtures");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = EmptyTestOrigin(scene);
            string room = Guid.NewGuid().ToString("N");
            context = new RoomPresentationContext(room, 1, false, Identity(room, 1, 101), Identity(room, 2, 102));
            objects = new RoomNetworkObjects(room, scene);
            var clone = UnityEngine.Object.Instantiate(template.gameObject, root.transform);
            clone.transform.localPosition = Vector3.zero; clone.transform.localRotation = Quaternion.identity;
            Player player = clone.GetComponent<Player>(); player.enabled = true;
            foreach (var collider in clone.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
            var check = new GameObject("Hit origin"); check.transform.SetParent(clone.transform, false); check.transform.localPosition = new Vector3(.4f, 0, 0);
            player.attackCheck = check.transform; player.attackCheckRadius = 1.2f;
            var serialized = new SerializedObject(player); serialized.FindProperty("whatIsGround").intValue = 1 << 3; serialized.ApplyModifiedPropertiesWithoutUndo();
            context.RegisterEntity(101, 1, player.transform);
            clone.SetActive(true);
            Enemy front = MakeEnemy(scene, root.transform, "Front", .8f), back = MakeEnemy(scene, root.transform, "Back", -.8f);
            monsters = new RoomSceneMonsters(new[] { Entry(front), Entry(back) }, objects);
            int nextId = 200;
            monsters.BindAuthority(context, () => ++nextId);
            front.enabled = back.enabled = true;
            var candidates = new List<PrimaryAttackHitCandidate>();
            Action query = () => { Physics2D.SyncTransforms(); PrimaryAttackHitDetection.CollectCandidates(player, monsters.IsValidPrimaryAttackTarget, candidates); };
            query(); Require(candidates.Count == 1 && candidates[0].Target == front, "Only the front authority monster intersects the valid range");
            int health = front.GetComponent<EnemyStats>().currentHealth;
            Require(health == 100 && !front.GetComponent<EnemyStats>().enabled, "A initializes target health without enabling legacy stats updates");
            front.transform.localPosition = new Vector3(4, 0, 0); query(); Require(candidates.Count == 0, "Out-of-range target is excluded");
            front.transform.localPosition = new Vector3(.8f, 0, 0);
            var wall = new GameObject("Blocking ground"); SceneManager.MoveGameObjectToScene(wall, scene);
            wall.transform.SetParent(root.transform, false);
            wall.layer = 3; wall.transform.localPosition = new Vector3(.35f, 0, 0);
            var wallCollider = wall.AddComponent<BoxCollider2D>(); wallCollider.size = new Vector2(.1f, 3);
            query(); Require(candidates.Count == 0, "Solid ground between attacker and contact blocks the target");
            wallCollider.isTrigger = true; query(); Require(candidates.Count == 1, "A trigger is not a solid wall");
            wall.SetActive(false);
            front.GetComponent<EnemyStats>().currentHealth = 0; query(); Require(candidates.Count == 0, "Zero-health target is invalid");
            front.GetComponent<EnemyStats>().currentHealth = health;
            front.enabled = false; query(); Require(candidates.Count == 0, "Disabled target is invalid"); front.enabled = true;
            var targetCollider = front.GetComponent<BoxCollider2D>(); targetCollider.enabled = false;
            query(); Require(candidates.Count == 0, "No enabled collision means no target"); targetCollider.enabled = true;
            targetCollider.isTrigger = true; query(); Require(candidates.Count == 0, "Trigger-only target is excluded"); targetCollider.isTrigger = false;
            player.FlipController(-1); query(); Require(candidates.Count == 1 && candidates[0].Target == back, "Authority facing and attack attachment flip together");
            player.FlipController(1);
            var child = new GameObject("Second target collider"); child.transform.SetParent(front.transform, false); child.AddComponent<BoxCollider2D>().size = new Vector2(.2f, .2f);
            query(); Require(candidates.Count == 2 && candidates[0].Target == front && candidates[1].Target == front,
                "Raw geometry preserves both colliders with the same parent target entity ID");
            VerifyColliderDedup(candidates, room);
            var unregistered = MakeEnemy(scene, root.transform, "Unregistered", .7f);
            context.RegisterEntity(300, 0, unregistered.transform);
            typeof(EnemyStats).GetMethod("InitializeAuthorityCombatStats", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(unregistered.GetComponent<EnemyStats>(), null);
            query(); Require(candidates.Count == 2, "Unregistered room-monster target is excluded despite a bound identity");
            var otherPlayerObject = new GameObject("Other player");
            otherPlayerObject.transform.SetParent(root.transform, false);
            otherPlayerObject.transform.localPosition = new Vector3(.7f, 0, 0);
            otherPlayerObject.AddComponent<BoxCollider2D>().size = new Vector2(.2f, .2f);
            otherPlayerObject.AddComponent<Player>(); context.RegisterEntity(102, 2, otherPlayerObject.transform);
            query(); Require(candidates.Count == 2, "A nearby authority player is never a normal-attack target");
            Require(front.GetComponent<EnemyStats>().currentHealth == health, "Detection never damages a target");
            VerifyWindow(player.GetComponentInChildren<Animator>(true), room);
            context.RemoveEntity(101); query(); Require(candidates.Count == 0, "Retired attacker cannot query targets");
        }
        catch (Exception error) { failure = error; }
        finally
        {
            Cleanup(() => monsters?.Dispose(), ref failure);
            Cleanup(() => objects?.Dispose(), ref failure);
            Cleanup(() => context?.Dispose(), ref failure);
            Cleanup(() => { if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive); }, ref failure);
            Cleanup(() => EditorSceneManager.CloseScene(scene, true), ref failure);
        }
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Debug.Log("[PrimaryHitWindow 4-33a] PASS: authored tick window, range/facing/collision, solid-wall blocking, child collider ownership, target validity and no damage. Raw multi-collider candidates also feed 4-33b hit admission once per entity.");
    }

    private static Vector3 EmptyTestOrigin(Scene testScene)
    {
        Vector2 maximum = Vector2.zero;
        foreach (var collider in UnityEngine.Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
            if (collider.enabled && collider.gameObject.scene.IsValid() && collider.gameObject.scene != testScene)
                maximum = Vector2.Max(maximum, collider.bounds.max);
        return new Vector3(maximum.x + 32, maximum.y + 32, 0);
    }

    private static void Cleanup(Action action, ref Exception failure)
    {
        try { action(); }
        catch (Exception error) { failure = failure == null ? error : new AggregateException("Primary hit validation and cleanup errors", failure, error); }
    }

    private static void VerifyColliderDedup(System.Collections.Generic.IReadOnlyList<PrimaryAttackHitCandidate> candidates, string room)
    {
        var runtime = new PrimaryAttackRuntime(room, 1, 101, () => 1);
        Require(runtime.TryQueue(1, 10, PrimaryAttackRejection.None) && runtime.TryBegin(11, PrimaryAttackRejection.None, 20, 40, 2, 6),
            "Begin hit admission for actual geometry candidates");
        int confirmations = 0;
        foreach (ulong tick in new[] { 13UL, 14UL, 15UL })
            foreach (var candidate in candidates)
                if (runtime.TryRegisterHit(runtime.Current, 1, candidate.EntityId, tick)) confirmations++;
        Require(confirmations == 1 && runtime.Current.AcceptedHitCount == 1,
            "Actual parent/child collision candidates across multiple samples produce one admitted hit");
    }

    private static void VerifyWindow(Animator animator, string room)
    {
        Require(PlayerPrimaryAttack.TryNetworkTiming(animator, out int duration, out int start, out int end) && start > 0 && end > start && end <= duration,
            "Existing attack marker creates a finite one-authored-frame window before animation completion");
        var runtime = new PrimaryAttackRuntime(room, 1, 101, () => 1);
        Require(runtime.TryQueue(1, 100, PrimaryAttackRejection.None) && runtime.TryBegin(101, PrimaryAttackRejection.None, duration, 40, start, end), "Begin timed authority attack");
        var attack = runtime.Current;
        Require(!attack.IsHitWindowOpen(attack.HitStartTick - 1) && attack.IsHitWindowOpen(attack.HitStartTick) &&
            attack.IsHitWindowOpen(attack.HitEndTick - 1) && !attack.IsHitWindowOpen(attack.HitEndTick) && !attack.IsHitWindowOpen(attack.EndTick),
            "Window is start-inclusive/end-exclusive; ending an attack closes its window");
    }
    private static Enemy MakeEnemy(Scene scene, Transform parent, string name, float x)
    {
        var value = new GameObject(name); SceneManager.MoveGameObjectToScene(value, scene);
        value.transform.SetParent(parent, false); value.transform.localPosition = new Vector3(x, 0, 0);
        value.AddComponent<BoxCollider2D>().size = new Vector2(.2f, .2f);
        var enemy = value.AddComponent<Enemy>(); var stats = value.AddComponent<EnemyStats>();
        foreach (var field in typeof(CharaterStats).GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (field.FieldType == typeof(Stat)) field.SetValue(stats, new Stat());
        stats.soulsDropAmount = new Stat(); stats.maxHealth.SetDefaultValue(100);
        return enemy;
    }
    private static RoomSceneMonsters.SceneEntry Entry(Enemy enemy) => new RoomSceneMonsters.SceneEntry
    { Enemy = enemy, Key = enemy.name, Type = enemy.GetType().FullName, Active = true, Position = enemy.transform.position };
    private static RoomPlayerIdentity Identity(string room, int player, int entity) =>
        (RoomPlayerIdentity)Activator.CreateInstance(typeof(RoomPlayerIdentity), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { room, player, entity }, null);
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("[PrimaryHitWindow 4-33a] FAIL: " + message); }
}
