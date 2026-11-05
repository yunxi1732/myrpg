using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class NpcRangeVerification
{
    [MenuItem("Tools/RPG/Verify NPC 2D Range (Play Mode)")]
    public static void Verify()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Npc2DRange] 请先进入 Play，使用隔离物理场景验证真实 2D 触发回调。");
            return;
        }
        Scene scene = SceneManager.CreateScene("NpcRangeVerification", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        GameObject npcRoot = null, playerRoot = null;
        try
        {
            npcRoot = new GameObject("TemporaryNPC");
            npcRoot.SetActive(false);
            SceneManager.MoveGameObjectToScene(npcRoot, scene);
            var npc = npcRoot.AddComponent<NpcInteractable>();
            var rangeObject = new GameObject("InteractionRange");
            rangeObject.transform.SetParent(npcRoot.transform, false);
            var trigger = rangeObject.AddComponent<CircleCollider2D>();
            trigger.isTrigger = true;
            trigger.radius = 2;
            var relay = rangeObject.AddComponent<NpcInteractionRange>();
            var serialized = new SerializedObject(relay);
            serialized.FindProperty("owner").objectReferenceValue = npc;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            npc.interactionRange = trigger;

            playerRoot = new GameObject("TemporaryPlayer");
            SceneManager.MoveGameObjectToScene(playerRoot, scene);
            playerRoot.layer = LayerMask.NameToLayer("Player");
            var body = playerRoot.AddComponent<Rigidbody2D>();
            body.gravityScale = 0;
            // Test is synchronous; destroy before the next frame so gameplay Start/Update never runs.
            npc.interactionPlayer = playerRoot.AddComponent<Player>();
            foreach (float offset in new[] { -0.6f, 0.6f })
            {
                var child = new GameObject("PlayerBodyCollider");
                child.layer = playerRoot.layer;
                child.transform.SetParent(playerRoot.transform, false);
                child.transform.localPosition = new Vector3(offset, 0, 0);
                child.AddComponent<CircleCollider2D>().radius = 0.25f;
            }
            var changes = new List<bool>();
            npc.OnPlayerRangeChanged += inside => changes.Add(inside);
            npcRoot.SetActive(true);
            PhysicsScene2D physics = scene.GetPhysicsScene2D();
            Action<float> step = x =>
            {
                body.position = new Vector2(x, 0);
                Physics2D.SyncTransforms();
                Require(physics.Simulate(0.02f), "Local physics scene simulates");
            };
            step(10);
            step(0);
            Require(npc.IsPlayerInRange && relay.ContactCount == 2 && changes.Count == 1,
                "Two colliders enter but notify once");
            step(0);
            Require(relay.ContactCount == 2 && changes.Count == 1, "Stay does not duplicate contacts");
            step(1.9f);
            Require(npc.IsPlayerInRange && relay.ContactCount == 1 && changes.Count == 1,
                "Partial exit does not remove the player");
            step(10);
            Require(!npc.IsPlayerInRange && relay.ContactCount == 0 && changes.Count == 2,
                "Last collider exit clears range once");
            step(0);
            npc.enabled = false;
            Require(!npc.IsPlayerInRange && relay.ContactCount == 0 && changes.Count == 4,
                "Disabling NPC clears every contact and emits one removal");
            step(0);
            Require(relay.ContactCount == 0, "Disabled NPC ignores physics callbacks");
            npc.enabled = true;
            step(0);
            Require(npc.IsPlayerInRange && relay.ContactCount == 2, "Stay recovers after NPC re-enable");
            npcRoot.SetActive(false);
            Require(!npc.IsPlayerInRange && relay.ContactCount == 0, "Root disable clears range");
            int before = changes.Count;
            relay.ClearContacts();
            Require(changes.Count == before, "Repeated cleanup emits no duplicate removal");
            npcRoot.SetActive(true);
            step(0);
            Require(npc.IsPlayerInRange, "Range can re-enter after hierarchy re-enable");
            UnityEngine.Object.DestroyImmediate(relay);
            Require(!npc.IsPlayerInRange && changes[changes.Count - 1] == false, "Destroy clears range");
            Debug.Log("[Npc2DRange] PASS: enter/stay/exit, multiple colliders, disable, re-enable and destroy");
        }
        finally
        {
            if (npcRoot != null) UnityEngine.Object.DestroyImmediate(npcRoot);
            if (playerRoot != null) UnityEngine.Object.DestroyImmediate(playerRoot);
            SceneManager.UnloadSceneAsync(scene);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[Npc2DRange] FAIL: " + message);
    }
}
