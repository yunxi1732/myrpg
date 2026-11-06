using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DialogueSceneCleanupVerification
{
    private static bool running;

    [MenuItem("Tools/RPG/Verify Dialogue Scene Cleanup (Play Mode)")]
    public static void Verify()
    {
        if (!Application.isPlaying || running)
        {
            Debug.LogWarning("[DialogueSceneCleanup] 请进入 Play，且不要重复启动本检查。");
            return;
        }
        running = true;
        int baseline = GameplayControlLock.HolderCount;
        int cycle = 0;
        Scene scene = default;
        AsyncOperation unloading = null;
        DialogueView active = null, idle = null;
        DialogueRunner activeRunner = null, idleRunner = null;
        UI scopeUi = null;
        InteractionManager scopeInteractions = null;
        int disposedNotifications = 0;
        var asset = ScriptableObject.CreateInstance<DialogueAsset>();
        EditorApplication.CallbackFunction tick = null;
        Action cleanup = () =>
        {
            EditorApplication.update -= tick;
            running = false;
            try
            {
                if (active != null) active.Close();
                if (idle != null) idle.Close();
                activeRunner?.End();
                idleRunner?.End();
                if (!ReferenceEquals(scopeUi, null) && !scopeUi.IsShuttingDown)
                    typeof(UI).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(scopeUi, null);
                scopeInteractions?.Dispose();
            }
            finally
            {
                if (Application.isPlaying && scene.IsValid() && scene.isLoaded && unloading == null)
                    SceneManager.UnloadSceneAsync(scene);
                UnityEngine.Object.DestroyImmediate(asset);
            }
        };
        tick = () =>
        {
            try
            {
                if (!Application.isPlaying) { cleanup(); return; }
                if (unloading == null)
                {
                    scene = SceneManager.CreateScene("DialogueExitCheck_" + Guid.NewGuid().ToString("N"));
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Dialogue/DialogueBox.prefab");
                    GameObject activeRoot = UnityEngine.Object.Instantiate(prefab);
                    GameObject idleRoot = UnityEngine.Object.Instantiate(prefab);
                    SceneManager.MoveGameObjectToScene(activeRoot, scene);
                    SceneManager.MoveGameObjectToScene(idleRoot, scene);
                    active = activeRoot.GetComponent<DialogueView>();
                    idle = idleRoot.GetComponent<DialogueView>();
                    activeRunner = new DialogueRunner();
                    idleRunner = new DialogueRunner();
                    active.Bind(activeRunner);
                    activeRunner.Start(asset, "场景退出测试");
                    idle.Bind(idleRunner); // Remains hidden; never relies on having been enabled.
                    var uiRoot = new GameObject("InactiveSceneOwner");
                    uiRoot.SetActive(false);
                    SceneManager.MoveGameObjectToScene(uiRoot, scene);
                    scopeUi = uiRoot.AddComponent<UI>();
                    scopeInteractions = scopeUi.Interactions;
                    disposedNotifications = 0;
                    scopeInteractions.OnDisposed += () => disposedNotifications++;
                    scopeInteractions.Register(new InteractionOptionData(new object(), "test", "场景退出", () => { }));
                    var menu = new GameObject("IndependentSceneMenu");
                    menu.SetActive(false);
                    SceneManager.MoveGameObjectToScene(menu, scene);
                    scopeUi.RegisterPanel(UIPanelId.Options, menu).OnShow();
                    Require(activeRunner.IsRunning && GameplayControlLock.HolderCount == baseline + 2,
                        "Fresh scene registers menu/candidate/dialogue independently");
                    unloading = SceneManager.UnloadSceneAsync(scene);
                    return;
                }
                if (!unloading.isDone) return;
                Require(!activeRunner.IsRunning && GameplayControlLock.HolderCount == baseline,
                    "Unload ends active dialogue and releases its own lock");
                Require(scopeUi.IsShuttingDown && scopeInteractions.IsDisposed && scopeInteractions.Options.Count == 0 &&
                    scopeUi.GetPanel(UIPanelId.Options) == null && disposedNotifications == 1 && !scopeInteractions.TryExecuteSelected(),
                    "Scene unload disposes inactive UI owner, candidates, events and panel registrations");
                foreach (DialogueRunner runner in new[] { activeRunner, idleRunner })
                {
                    foreach (string name in new[] { "OnNodeChanged", "OnEnded" })
                        Require(typeof(DialogueRunner).GetField(name,
                            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runner) == null,
                            "Unloaded view detached runner event: " + name);
                    runner.Start(asset);
                    runner.End(); // Old external references cannot call destroyed views.
                }
                if (++cycle == 2)
                {
                    cleanup();
                    Debug.Log("[DialogueSceneCleanup] PASS: two scene entries, UI owners, candidates, active/hidden dialogue and no residual locks/events");
                }
                else unloading = null;
            }
            catch (Exception error)
            {
                cleanup();
                Debug.LogException(error);
            }
        };
        EditorApplication.update += tick;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[DialogueSceneCleanup] FAIL: " + message);
    }
}
