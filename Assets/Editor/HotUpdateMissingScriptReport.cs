using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class HotUpdateMissingScriptReport
{
    private const string TriggerScriptGuid = "617c060a6ca5a894bbe58742275200d3";

    [MenuItem("Tools/RPG/Report Missing Scripts In Loaded Scenes")]
    private static void Report()
    {
        var report = new StringBuilder();
        var missingObjects = 0;
        var missingComponents = 0;
        var triggerComponents = 0;

        var path = AssetDatabase.GUIDToAssetPath(TriggerScriptGuid);
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
        var type = script != null ? script.GetClass() : null;
        report.AppendLine($"[MissingScript] EnemyAnimatorTriggers: path={path}, type={type?.FullName ?? "null"}, assembly={type?.Assembly.GetName().Name ?? "null"}");

        for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            var scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded) continue;

            var sceneObjects = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    var gameObject = transform.gameObject;
                    sceneObjects++;
                    foreach (var component in gameObject.GetComponents<Component>())
                    {
                        if (component != null && component.GetType().FullName == "EnemyAnimatorTriggers")
                            triggerComponents++;
                    }
                    var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                    if (count == 0) continue;

                    missingObjects++;
                    missingComponents += count;
                    report.AppendLine($"[MissingScript] scene={scene.path}, object={GetPath(transform)}, missingComponents={count}");
                }
            }

            report.AppendLine($"[MissingScript] Loaded scene={scene.path}, objects={sceneObjects}");
        }

        report.AppendLine($"[MissingScript] Total: objects={missingObjects}, components={missingComponents}, EnemyAnimatorTriggers={triggerComponents}");
        Debug.Log(report.ToString());
    }

    private static string GetPath(Transform transform)
    {
        var path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }
}
