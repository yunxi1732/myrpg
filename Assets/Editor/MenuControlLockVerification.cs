using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class MenuControlLockVerification
{
    [MenuItem("Tools/RPG/Verify Menu Control Locks (Play Mode)")]
    public static void Verify()
    {
        if (!Application.isPlaying || GameManager.instance == null)
        {
            Debug.LogWarning("[MenuControlLocks] 请在 MainScene Play 模式运行。");
            return;
        }
        var root = new GameObject("TemporaryMenuLockVerification");
        var registry = new UISystem();
        var objects = new Dictionary<UIPanelId, GameObject>();
        var dialogueOwner = new object();
        GameManager game = GameManager.instance;
        int baseline = GameplayControlLock.HolderCount;
        try
        {
            foreach (UIPanelId id in Enum.GetValues(typeof(UIPanelId)))
            {
                var go = new GameObject(id.ToString());
                go.SetActive(false);
                go.transform.SetParent(root.transform, false);
                registry.RegisterPanel(id, go);
                objects.Add(id, go);
            }
            bool switching = false, observed = false;
            var old = objects[UIPanelId.Character];
            var watcher = old.AddComponent<InventoryViewLifecycle>();
            watcher.Changed = () =>
            {
                if (!switching || old.activeInHierarchy) return;
                // Explicit release makes the assertion independent of Unity callback ordering.
                old.GetComponent<MenuControlLock>().Release();
                Require(GameplayControlLock.HolderCount == baseline + 1 && Time.timeScale == 0,
                    "Target already owns lock/pause when old menu disables");
                observed = true;
            };
            registry.ShowExclusive(UIPanelId.Character);
            registry.ShowExclusive(UIPanelId.Character);
            Require(GameplayControlLock.HolderCount == baseline + 1, "Repeated menu open is idempotent");
            switching = true;
            registry.ShowExclusive(UIPanelId.Options);
            switching = false;
            Require(observed && GameplayControlLock.HolderCount == baseline + 1 && Time.timeScale == 0,
                "Menu switch never drops all requests");
            registry.GetPanel(UIPanelId.Character).enabled = false;
            Require(!registry.ShowExclusive(UIPanelId.Character) && objects[UIPanelId.Options].activeSelf &&
                GameplayControlLock.HolderCount == baseline + 1,
                "Unavailable target preserves previous menu and its lock");
            registry.GetPanel(UIPanelId.Character).enabled = true;
            GameplayControlLock.Acquire(dialogueOwner);
            game.SetPauseOwner(dialogueOwner, true);
            registry.ShowExclusive(UIPanelId.InGame);
            Require(GameplayControlLock.HolderCount == baseline + 1 && Time.timeScale == 0,
                "Closing menu preserves dialogue input/pause");
            registry.CloseMenuGroup();
            Require(GameplayControlLock.HolderCount == baseline + 1, "Repeated menu close cannot release dialogue");
            registry.ShowExclusive(UIPanelId.Craft);
            GameplayControlLock.Release(dialogueOwner);
            game.SetPauseOwner(dialogueOwner, false);
            Require(GameplayControlLock.HolderCount == baseline + 1 && Time.timeScale == 0,
                "Closing dialogue preserves menu input/pause");
            objects[UIPanelId.Craft].SetActive(false);
            Require(GameplayControlLock.HolderCount == baseline, "Direct menu disable releases own request");
            registry.ShowExclusive(UIPanelId.SkillTree);
            UnityEngine.Object.DestroyImmediate(objects[UIPanelId.SkillTree]);
            Require(GameplayControlLock.HolderCount == baseline, "Destroy releases menu request");
            registry.ShowExclusive(UIPanelId.Options);
            registry.Clear();
            Require(GameplayControlLock.HolderCount == baseline && objects[UIPanelId.Options].activeSelf,
                "Registry cleanup releases requests without destroying scene panels");
            registry.RegisterPanel(UIPanelId.Options, objects[UIPanelId.Options]);
            registry.Dispose();
            registry.Dispose();
            bool rejected = false;
            try { registry.RegisterPanel(UIPanelId.Options, objects[UIPanelId.Options]); }
            catch (ObjectDisposedException) { rejected = true; }
            Require(rejected && registry.GetPanel(UIPanelId.Options) == null &&
                GameplayControlLock.HolderCount == baseline, "Disposed registry cannot reacquire locks");
            var fresh = new UISystem();
            fresh.RegisterPanel(UIPanelId.Options, objects[UIPanelId.Options]);
            Require(fresh.ShowExclusive(UIPanelId.Options) && GameplayControlLock.HolderCount == baseline + 1,
                "Fresh scene registry can register again");
            fresh.Dispose();
            Debug.Log("[MenuControlLocks] PASS: no-gap switch, independent menu/dialogue owners and lifecycle cleanup");
        }
        finally
        {
            registry.Clear();
            GameplayControlLock.Release(dialogueOwner);
            if (game != null) game.SetPauseOwner(dialogueOwner, false);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[MenuControlLocks] FAIL: " + message);
    }
}
