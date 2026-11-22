using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Rpg.HotUpdate
{
    public static class HotUpdateScenes
    {
        private static AsyncOperationHandle<SceneInstance> current;

        public static AsyncOperationHandle<SceneInstance> LoadGame()
        {
            var next = Addressables.LoadSceneAsync("MainScene", LoadSceneMode.Single);
            next.Completed += completed =>
            {
                if (completed.Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogError("[HotUpdate] MainScene load failed: " + completed.OperationException);
                    return;
                }
                ReleaseCurrent();
                current = completed;
            };
            return next;
        }

        public static void ReleaseCurrent()
        {
            if (current.IsValid()) Addressables.Release(current);
            current = default;
        }
    }
}
