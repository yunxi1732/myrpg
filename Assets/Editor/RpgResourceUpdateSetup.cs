using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Rpg.Editor
{
    // Run from Unity after installing Addressables. MainMenu remains the built-in
    // first scene; MainScene and its runtime dependencies become remote content.
    public static class RpgResourceUpdateSetup
    {
        private const string MenuPath = "Assets/Scenes/MainMenu.unity";
        private const string GamePath = "Assets/Scenes/MainScene.unity";
        private const string ScenesGroup = "RPG Remote Scene";
        private const string AssetsGroup = "RPG Remote Runtime Assets";
        private const string ReleaseIdVariable = "RPG.ReleaseId";
        private const string CatalogLoadVariable = "RPG.CatalogLoadPath";
        private const string RuntimeLabel = "rpg-runtime";

        [MenuItem("Tools/RPG Resource Update/1 Configure Remote Content")]
        public static void ConfigureRemoteContent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before configuring remote content.");
            if (!File.Exists(MenuPath) || !File.Exists(GamePath))
                throw new FileNotFoundException("MainMenu or MainScene is missing.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null) throw new InvalidOperationException("Addressables settings could not be created.");
            ConfigureProfile(settings);
            var sceneGroup = RemoteGroup(settings, ScenesGroup, BundledAssetGroupSchema.BundlePackingMode.PackSeparately);
            var assetGroup = RemoteGroup(settings, AssetsGroup, BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel);

            var dependencies = new HashSet<string>(AssetDatabase.GetDependencies(GamePath, true)) { GamePath };
            var expected = new HashSet<string>();
            var localDependencies = new List<string>();
            foreach (string path in dependencies.OrderBy(value => value, StringComparer.Ordinal))
            {
                if (!ShouldBeRemote(path))
                {
                    if (path.StartsWith("Assets/", StringComparison.Ordinal) && !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        localDependencies.Add(path);
                    continue;
                }
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Asset has no GUID: " + path);
                bool scene = path == GamePath;
                var entry = settings.CreateOrMoveEntry(guid, scene ? sceneGroup : assetGroup, false, false);
                entry.address = scene ? "MainScene" : path;
                entry.SetLabel(RuntimeLabel, true, true, false);
                if (!scene) entry.SetLabel("rpg-" + Category(path), true, true, false);
                expected.Add(guid);
            }
            foreach (var group in new[] { sceneGroup, assetGroup })
                foreach (var entry in group.entries.ToArray())
                    if (!expected.Contains(entry.guid)) settings.RemoveAssetEntry(entry.guid, false);

            // The built-in menu is the first scene and must be available offline.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MenuPath, true) };
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(sceneGroup);
            EditorUtility.SetDirty(assetGroup);
            AssetDatabase.SaveAssets();
            Debug.Log($"[HotUpdate] Configured {expected.Count} remote runtime entries. " +
                      $"MainMenu remains built-in. {localDependencies.Count} built-in/Resources dependencies stay local.");
            foreach (string path in localDependencies) Debug.Log("[HotUpdate] Local dependency: " + path);
        }

        [MenuItem("Tools/RPG Resource Update/2 Build Initial Review Content")]
        public static void BuildInitialReviewContent() => BuildReviewContent(null, NewReleaseId());

        [MenuItem("Tools/RPG Resource Update/3 Build Update Review Content")]
        public static void BuildUpdateReviewContent()
        {
            string state = EditorUtility.OpenFilePanel("Select the original released addressables_content_state.bin", "", "bin");
            if (!string.IsNullOrEmpty(state)) BuildReviewContent(state, NewReleaseId());
        }

        [MenuItem("Tools/RPG Resource Update/Toggle Remote Test In Editor")]
        public static void ToggleRemoteTestInEditor()
        {
            const string key = "RPG.TestRemoteContent";
            bool enabled = !EditorPrefs.GetBool(key, false);
            EditorPrefs.SetBool(key, enabled);
            Debug.Log("[HotUpdate] Remote content test in Editor: " + enabled);
        }

        // GameCI calls this method. It produces an artifact but never publishes it.
        public static void BuildForCi()
        {
            string id = CommandLineValue("-rpgReleaseId");
            string state = CommandLineValue("-rpgContentStatePath");
            if (string.IsNullOrEmpty(id) || !Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,80}$"))
                throw new InvalidOperationException("GameCI must pass a valid -rpgReleaseId through customParameters.");
            BuildReviewContent(state, id);
        }

        private static string CommandLineValue(string option)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; i++)
                if (string.Equals(arguments[i], option, StringComparison.Ordinal) &&
                    !arguments[i + 1].StartsWith("-", StringComparison.Ordinal))
                    return arguments[i + 1];
            return null;
        }

        private static string NewReleaseId() => DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");

        private static void BuildReviewContent(string originalState, string releaseId)
        {
            ProjectConfigData.GenerateBuildLayout = true;
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings == null || settings.FindGroup(ScenesGroup) == null || settings.FindGroup(AssetsGroup) == null)
                throw new InvalidOperationException("Run Configure Remote Content first.");
            if (EditorBuildSettings.scenes.Length != 1 || EditorBuildSettings.scenes[0].path != MenuPath)
                throw new InvalidOperationException("Player Build Settings must contain only MainMenu.");
            if (!string.IsNullOrEmpty(originalState) && !File.Exists(originalState))
                throw new FileNotFoundException("Original content state is missing", originalState);

            EnsureCatalogLoadProfile(settings);
            string previous = settings.profileSettings.GetValueByName(settings.activeProfileId, ReleaseIdVariable);
            string previousCatalogLoadPath = settings.profileSettings.GetValueByName(settings.activeProfileId, CatalogLoadVariable);
            settings.profileSettings.SetValue(settings.activeProfileId, ReleaseIdVariable, releaseId);
            try
            {
                AddressablesPlayerBuildResult result;
                if (string.IsNullOrEmpty(originalState)) AddressableAssetSettings.BuildPlayerContent(out result);
                else
                {
                    var baseState = ContentUpdateScript.LoadContentState(originalState);
                    if (baseState == null || string.IsNullOrEmpty(baseState.remoteCatalogLoadPath))
                        throw new InvalidOperationException("The selected original content state has no remote catalog path.");
                    settings.profileSettings.SetValue(settings.activeProfileId, CatalogLoadVariable,
                        baseState.remoteCatalogLoadPath);
                    result = ContentUpdateScript.BuildContentUpdate(settings, originalState);
                }
                if (result == null || !string.IsNullOrEmpty(result.Error))
                    throw new InvalidOperationException("Addressables content build failed; inspect the Unity Console. " + result?.Error);
                Debug.Log("[HotUpdate] Review content built: releaseId=" + releaseId +
                          ", output=ServerData/releases/" + releaseId + "/" + EditorUserBuildSettings.activeBuildTarget +
                          ". Package with Tools/package_resource_release.py; no content was published.");
            }
            finally
            {
                settings.profileSettings.SetValue(settings.activeProfileId, ReleaseIdVariable, previous);
                settings.profileSettings.SetValue(settings.activeProfileId, CatalogLoadVariable, previousCatalogLoadPath);
                AssetDatabase.SaveAssets();
            }
        }

        private static void ConfigureProfile(AddressableAssetSettings settings)
        {
            var profile = settings.profileSettings;
            profile.CreateValue(ReleaseIdVariable, "unbuilt");
            profile.SetValue(settings.activeProfileId, AddressableAssetSettings.kRemoteBuildPath,
                "ServerData/releases/[RPG.ReleaseId]/[BuildTarget]");
            profile.SetValue(settings.activeProfileId, AddressableAssetSettings.kRemoteLoadPath,
                "http://127.0.0.1:8000/releases/[RPG.ReleaseId]/[BuildTarget]");
            EnsureCatalogLoadProfile(settings);
            settings.BuildRemoteCatalog = true;
            settings.DisableCatalogUpdateOnStartup = true;
            settings.RemoteCatalogBuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
        }

        private static void EnsureCatalogLoadProfile(AddressableAssetSettings settings)
        {
            settings.profileSettings.CreateValue(CatalogLoadVariable,
                "http://127.0.0.1:8000/releases/[RPG.ReleaseId]/[BuildTarget]");
            settings.RemoteCatalogLoadPath.SetVariableByName(settings, CatalogLoadVariable);
        }

        private static AddressableAssetGroup RemoteGroup(AddressableAssetSettings settings, string name,
            BundledAssetGroupSchema.BundlePackingMode mode)
        {
            var group = settings.FindGroup(name) ?? settings.CreateGroup(name, false, false, true, null,
                typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            var bundles = group.GetSchema<BundledAssetGroupSchema>() ?? group.AddSchema<BundledAssetGroupSchema>();
            var updates = group.GetSchema<ContentUpdateGroupSchema>() ?? group.AddSchema<ContentUpdateGroupSchema>();
            bundles.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
            bundles.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
            bundles.BundleMode = mode;
            bundles.UseAssetBundleCache = true;
            updates.StaticContent = false;
            EditorUtility.SetDirty(bundles);
            EditorUtility.SetDirty(updates);
            return group;
        }

        private static bool ShouldBeRemote(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                path.StartsWith("Assets/Editor/", StringComparison.Ordinal) ||
                path.Contains("/Resources/") || AssetDatabase.IsValidFolder(path)) return false;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension != ".cs" && extension != ".dll" && extension != ".asmdef" && extension != ".rsp" &&
                   extension != ".cginc" && extension != ".hlsl";
        }

        private static string Category(string path)
        {
            if (path.StartsWith("Assets/Audio/", StringComparison.Ordinal) || path.EndsWith(".mixer", StringComparison.OrdinalIgnoreCase)) return "audio";
            if (path.StartsWith("Assets/Graphics/UI/", StringComparison.Ordinal) || path.StartsWith("Assets/Prefabs/UI/", StringComparison.Ordinal)) return "ui";
            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return "prefabs";
            if (path.StartsWith("Assets/Animations/", StringComparison.Ordinal)) return "animations";
            if (path.StartsWith("Assets/Data/", StringComparison.Ordinal)) return "data";
            if (path.StartsWith("Assets/Graphics/", StringComparison.Ordinal)) return "graphics";
            return "other";
        }
    }
}
