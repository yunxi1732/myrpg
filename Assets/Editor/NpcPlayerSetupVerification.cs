using System;
using UnityEditor;
using UnityEngine;

public static class NpcPlayerSetupVerification
{
    [MenuItem("Tools/RPG/Verify NPC Player Setup")]
    public static void Verify()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("[NpcPlayerSetup] 请退出 Play 后运行配置检查，避免临时碰撞器影响游戏。");
            return;
        }
        NpcInteractable npc = null;
        foreach (var candidate in UnityEngine.Object.FindObjectsOfType<NpcInteractable>())
            if (candidate.name == "TestNPC_Guide") npc = candidate;
        Require(npc != null && npc.interactionPlayer != null, "Open MainScene with configured test NPC");
        Player player = npc.interactionPlayer;
        var body = player.GetComponent<Rigidbody2D>();
        var capsule = player.GetComponent<CapsuleCollider2D>();
        Require(body != null && body.simulated && body.bodyType == RigidbodyType2D.Dynamic,
            "Player root has simulated dynamic Rigidbody2D");
        Require(capsule != null && capsule.enabled && !capsule.isTrigger && capsule.attachedRigidbody == body,
            "Player root has enabled solid capsule attached to root body");
        Require(player.gameObject.layer == LayerMask.NameToLayer("Player") && npc.IsPlayerCollider(capsule),
            "Configured player root is recognized without Tag/name lookup");
        Require(npc.interactionRange != null && npc.interactionRange.isTrigger &&
            npc.interactionRange.transform.parent == npc.transform &&
            npc.interactionRange.GetComponent<NpcInteractionRange>() != null,
            "Independent trigger child has range relay");
        Require(!Physics2D.GetIgnoreLayerCollision(npc.interactionRange.gameObject.layer, player.gameObject.layer),
            "Physics matrix permits player/range contacts");

        var child = new GameObject("TemporaryPlayerCollider");
        var outsider = new GameObject("TemporaryNonPlayer");
        try
        {
            child.transform.SetParent(player.transform, false);
            child.layer = player.gameObject.layer;
            var childCollider = child.AddComponent<BoxCollider2D>();
            Require(npc.IsPlayerCollider(childCollider), "Child collider resolves root player and body");
            childCollider.isTrigger = true;
            Require(!npc.IsPlayerCollider(childCollider), "Player sensor/attack trigger is excluded");
            childCollider.isTrigger = false;
            child.layer = LayerMask.NameToLayer("Enemy");
            Require(!npc.IsPlayerCollider(childCollider), "Wrong collider layer is excluded");
            child.layer = player.gameObject.layer;
            childCollider.enabled = false;
            Require(!npc.IsPlayerCollider(childCollider), "Disabled collider is excluded");
            outsider.layer = player.gameObject.layer;
            outsider.AddComponent<Rigidbody2D>();
            var otherCollider = outsider.AddComponent<BoxCollider2D>();
            Require(!npc.IsPlayerCollider(otherCollider) && !npc.IsPlayerCollider(null),
                "Layer alone and null cannot impersonate the configured player");
            Debug.Log("[NpcPlayerSetup] PASS: root, body, collider, layers, matrix and player identification");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(child);
            UnityEngine.Object.DestroyImmediate(outsider);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[NpcPlayerSetup] FAIL: " + message);
    }
}
