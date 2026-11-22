using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Rpg.Networking.Protocol;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.SceneManagement;

namespace Rpg.HotUpdate
{
    [Serializable]
    public sealed class ReleaseFile
    {
        public string path;
        public string sha256;
        public long bytes;
    }

    [Serializable]
    public sealed class ResourceRelease
    {
        public string version;
        public string clientVersion;
        public int protocolVersion;
        public int scriptAssemblyVersion;
        public string catalogPath;
        public string catalogSha256;
        public ReleaseFile hotUpdateDll;
        public ReleaseFile[] aotMetadata;
    }

    // MainMenu is built into the client. It remains usable while the remote game
    // scene is checked and downloaded, and presents a retry button on failure.
    public sealed class ResourceUpdateController : MonoBehaviour
    {
        private const string Server = "http://127.0.0.1:8000/";
        public const int ScriptAssemblyVersion = 2;
        private const string RuntimeLabel = "rpg-runtime";
        private const string ManifestName = "resource-release.json";
        private const string BlockedVersionName = "blocked-resource-version.txt";
        private string storage;
        private string status = "Checking game resources...";
        private float progress;
        private bool busy;
        private bool failed;
        private bool rollbackAvailable;
        private bool restartRequired;

        public bool Ready { get; private set; }
        public string Status => status;
        public static string ActiveVersion { get; private set; }

        private void Awake()
        {
            storage = Path.Combine(Application.persistentDataPath, "HotUpdate");
            Directory.CreateDirectory(storage);
            var previous = ReadManifest(Path.Combine(storage, ManifestName + ".previous"));
            rollbackAvailable = Compatible(previous) && previous.version != ReadBlockedVersion() &&
                CodeUpdateLoader.HasCachedFiles(previous, storage);
        }

        private IEnumerator Start()
        {
#if UNITY_EDITOR
            if (!UnityEditor.EditorPrefs.GetBool("RPG.TestRemoteContent", false))
            {
                status = "Editor Asset Database mode";
                ActiveVersion = "editor-asset-database";
                Ready = true;
                yield break;
            }
#endif
            yield return CheckAndDownload();
        }

        private void OnGUI()
        {
            if (Ready)
            {
                if (restartRequired)
                    GUI.Box(new Rect(Mathf.Max(12, Screen.width - 360), 12, 348, 58),
                        "Previous version selected. Restart the client to use it.");
                if (SceneManager.GetActiveScene().name == "MainMenu" &&
                    rollbackAvailable &&
                    GUI.Button(new Rect(Screen.width - 170, 12, 158, 32), "Rollback resources"))
                    StartCoroutine(Rollback());
                return;
            }
            float width = Mathf.Min(600, Screen.width - 24);
            GUILayout.BeginArea(new Rect((Screen.width - width) / 2f, Screen.height / 2f - 75, width, 150), GUI.skin.box);
            GUILayout.Label("Game resource update");
            GUILayout.Label(status);
            GUILayout.Label(Mathf.RoundToInt(progress * 100) + "%");
            if (failed && !busy && GUILayout.Button("Retry")) StartCoroutine(CheckAndDownload());
            GUILayout.EndArea();
        }

        private IEnumerator CheckAndDownload()
        {
            if (busy) yield break;
            busy = true;
            failed = false;
            Ready = false;
            progress = 0;
            ActiveVersion = null;

            var initialize = Addressables.InitializeAsync(false);
            yield return initialize;
            if (initialize.Status != AsyncOperationStatus.Succeeded)
            {
                Fail("Addressables initialization failed: " + initialize.OperationException);
                Addressables.Release(initialize);
                yield break;
            }
            Addressables.Release(initialize);

            ResourceRelease saved = ReadManifest(Path.Combine(storage, ManifestName));
            IResourceLocator savedLocator = null;
            if (Compatible(saved))
            {
                string path = CatalogFile(saved);
                bool valid = false;
                try { valid = File.Exists(path) && MatchesHash(File.ReadAllBytes(path), saved.catalogSha256); }
                catch (Exception error) { Debug.LogWarning("[HotUpdate] Saved catalog read failed: " + error.Message); }
                if (valid)
                {
                    var load = Addressables.LoadContentCatalogAsync(path, false);
                    yield return load;
                    if (load.Status == AsyncOperationStatus.Succeeded) savedLocator = load.Result;
                    else Debug.LogWarning("[HotUpdate] Saved catalog load failed: " + load.OperationException);
                    Addressables.Release(load);
                }
            }

            status = "Checking for updates...";
            ResourceRelease latest = null;
            using (var request = UnityWebRequest.Get(Server + "latest.json"))
            {
                request.timeout = 10;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try { latest = JsonUtility.FromJson<ResourceRelease>(request.downloadHandler.text); }
                    catch (Exception error) { Debug.LogWarning("[HotUpdate] Invalid latest.json: " + error.Message); }
                }
                else if (savedLocator != null)
                    Debug.Log("[HotUpdate] Update server unavailable; checking cached resources: " + request.error);
                else Debug.LogWarning("[HotUpdate] Update check failed: " + request.error);
            }

            IResourceLocator chosen = savedLocator;
            ResourceRelease newlyDownloaded = null;
            string blockedVersion = ReadBlockedVersion();
            if (Compatible(latest) && latest.version != blockedVersion &&
                (savedLocator == null || latest.version != saved.version))
            {
                status = "Downloading catalog...";
                byte[] catalog = null;
                using (var request = UnityWebRequest.Get(Server + latest.catalogPath))
                {
                    request.timeout = 15;
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success) catalog = request.downloadHandler.data;
                    else Debug.LogWarning("[HotUpdate] Catalog download failed: " + request.error);
                }
                if (catalog != null && MatchesHash(catalog, latest.catalogSha256))
                {
                    string path = CatalogFile(latest);
                    bool staged = false;
                    try
                    {
                        File.WriteAllBytes(path + ".pending", catalog);
                        if (File.Exists(path)) File.Delete(path);
                        File.Move(path + ".pending", path);
                        staged = true;
                    }
                    catch (Exception error) { Debug.LogWarning("[HotUpdate] Catalog staging failed: " + error); }
                    if (staged)
                    {
                        var load = Addressables.LoadContentCatalogAsync(path, false);
                        yield return load;
                        if (load.Status == AsyncOperationStatus.Succeeded)
                        {
                            var candidate = load.Result;
                            if (HasGameScene(candidate))
                            {
                                yield return Download(candidate);
                                if (downloadSucceeded)
                                {
                                    bool codeStaged = false;
                                    yield return CodeUpdateLoader.Stage(latest, storage, Server,
                                        message => status = message, result => codeStaged = result);
                                    if (codeStaged)
                                    {
                                        chosen = candidate;
                                        newlyDownloaded = latest;
                                    }
                                }
                            }
                            else Debug.LogWarning("[HotUpdate] Catalog has no MainScene or runtime label");
                            if (chosen != candidate) Addressables.RemoveResourceLocator(candidate);
                        }
                        else Debug.LogWarning("[HotUpdate] Catalog load failed: " + load.OperationException);
                        Addressables.Release(load);
                    }
                }
                else Debug.LogWarning("[HotUpdate] Catalog SHA-256 mismatch or no catalog was downloaded");
            }
            else if (latest != null && !Compatible(latest))
                Debug.LogWarning("[HotUpdate] Resource release is incompatible with this client");

            if (chosen == null)
            {
                if (latest != null && !Compatible(latest))
                    Fail("No compatible game resources. Server release targets client " + latest.clientVersion +
                        ", protocol " + latest.protocolVersion + ", script assemblies " + latest.scriptAssemblyVersion +
                        "; this client needs " + Application.version + ", " + ProtocolSchema.SchemaVersion +
                        ", " + ScriptAssemblyVersion + ". Build and publish matching resources.");
                else Fail("No usable game resources. Check the local HTTP service and the published release, then retry.");
                yield break;
            }
            if (newlyDownloaded == null)
            {
                // Cached bundles should complete without network access. If any are
                // missing, the game stays in MainMenu until a retry succeeds.
                yield return Download(chosen);
                if (!downloadSucceeded)
                {
                    Fail("Cached game resources are incomplete. Connect and retry.");
                    yield break;
                }
                bool codeStaged = false;
                yield return CodeUpdateLoader.Stage(saved, storage, Server,
                    message => status = message, result => codeStaged = result);
                if (!codeStaged)
                {
                    Fail("Cached code is unavailable or failed verification. Connect and retry.");
                    yield break;
                }
            }

            status = "Loading game code...";
            ResourceRelease selected = newlyDownloaded ?? saved;
            if (!CodeUpdateLoader.LoadAndRegister(selected, storage, out string codeError))
            {
                Fail("Game code could not be loaded: " + codeError);
                yield break;
            }

            Addressables.ClearResourceLocators();
            Addressables.AddResourceLocator(chosen);
            if (newlyDownloaded != null)
            {
                try { SaveManifest(newlyDownloaded); }
                catch (Exception error)
                {
                    Fail("Resource update could not be saved: " + error.Message);
                    yield break;
                }
            }
            progress = 1;
            status = "Game code and resources ready";
            ActiveVersion = newlyDownloaded != null ? newlyDownloaded.version : saved.version;
            Ready = true;
            busy = false;
        }

        private IEnumerator Rollback()
        {
            if (busy || !Ready) yield break;
            var previous = ReadManifest(Path.Combine(storage, ManifestName + ".previous"));
            var current = ReadManifest(Path.Combine(storage, ManifestName));
            if (!Compatible(previous) || !Compatible(current) ||
                !CodeUpdateLoader.HasCachedFiles(previous, storage)) yield break;
            string catalogPath = CatalogFile(previous);
            bool valid = false;
            try { valid = File.Exists(catalogPath) && MatchesHash(File.ReadAllBytes(catalogPath), previous.catalogSha256); }
            catch (Exception error) { Debug.LogWarning("[HotUpdate] Rollback catalog read failed: " + error.Message); }
            if (!valid) yield break;

            busy = true;
            status = "Checking previous game resources...";
            var load = Addressables.LoadContentCatalogAsync(catalogPath, false);
            yield return load;
            if (load.Status == AsyncOperationStatus.Succeeded)
            {
                var previousLocator = load.Result;
                if (HasGameScene(previousLocator))
                {
                    yield return Download(previousLocator);
                    if (downloadSucceeded)
                    {
                        // A loaded managed assembly cannot be replaced in this process.
                        // Keep the current code and catalog together until restart.
                        try
                        {
                            File.WriteAllText(Path.Combine(storage, BlockedVersionName), current.version);
                            SaveManifest(previous);
                            rollbackAvailable = false;
                            restartRequired = true;
                            status = "Previous version selected. Restart to use it.";
                        }
                        catch (Exception error)
                        {
                            Debug.LogError("[HotUpdate] Rollback save failed: " + error);
                            try { File.Delete(Path.Combine(storage, BlockedVersionName)); }
                            catch (Exception cleanup) { Debug.LogWarning("[HotUpdate] Rollback cleanup failed: " + cleanup.Message); }
                        }
                    }
                }
                Addressables.RemoveResourceLocator(previousLocator);
            }
            else Debug.LogWarning("[HotUpdate] Previous resource catalog could not be loaded");
            Addressables.Release(load);
            busy = false;
        }

        private string ReadBlockedVersion()
        {
            try
            {
                string path = Path.Combine(storage, BlockedVersionName);
                return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            }
            catch (Exception error)
            {
                Debug.LogWarning("[HotUpdate] Blocked version could not be read: " + error.Message);
                return null;
            }
        }

        private bool downloadSucceeded;

        private IEnumerator Download(IResourceLocator locator)
        {
            downloadSucceeded = false;
            if (!locator.Locate(RuntimeLabel, null, out IList<IResourceLocation> locations) || locations.Count == 0)
                yield break;
            status = "Downloading game resources...";
            var operation = Addressables.DownloadDependenciesAsync(locations, false);
            while (!operation.IsDone)
            {
                var bytes = operation.GetDownloadStatus();
                progress = bytes.TotalBytes > 0 ? (float)bytes.DownloadedBytes / bytes.TotalBytes : operation.PercentComplete;
                yield return null;
            }
            downloadSucceeded = operation.Status == AsyncOperationStatus.Succeeded;
            if (!downloadSucceeded) Debug.LogWarning("[HotUpdate] Resource download failed: " + operation.OperationException);
            Addressables.Release(operation);
        }

        private static bool HasGameScene(IResourceLocator locator) =>
            locator.Locate("MainScene", null, out var scenes) && scenes.Count > 0 &&
            locator.Locate(RuntimeLabel, null, out var resources) && resources.Count > 0;

        private bool Compatible(ResourceRelease release) =>
            release != null && !string.IsNullOrWhiteSpace(release.version) &&
            System.Text.RegularExpressions.Regex.IsMatch(release.version, "^[A-Za-z0-9_-]{1,80}$") &&
            release.clientVersion == Application.version && release.protocolVersion == ProtocolSchema.SchemaVersion &&
            release.scriptAssemblyVersion == ScriptAssemblyVersion &&
            !string.IsNullOrWhiteSpace(release.catalogSha256) && release.catalogSha256.Length == 64 && IsHex(release.catalogSha256) &&
            !string.IsNullOrWhiteSpace(release.catalogPath) &&
            (release.catalogPath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
             release.catalogPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) &&
            release.catalogPath.StartsWith("releases/" + release.version + "/StandaloneWindows64/", StringComparison.Ordinal) &&
            !release.catalogPath.Contains("..") &&
            CodeUpdateLoader.ValidManifest(release);

        private static bool IsHex(string value)
        {
            foreach (char character in value) if (!Uri.IsHexDigit(character)) return false;
            return true;
        }

        private string CatalogFile(ResourceRelease release) => Path.Combine(storage,
            "catalog-" + release.catalogSha256.ToLowerInvariant() + Path.GetExtension(release.catalogPath).ToLowerInvariant());

        private static bool MatchesHash(byte[] data, string expected)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        private static ResourceRelease ReadManifest(string path)
        {
            if (!File.Exists(path)) return null;
            try { return JsonUtility.FromJson<ResourceRelease>(File.ReadAllText(path)); }
            catch (Exception error) { Debug.LogWarning("[HotUpdate] Saved release is invalid: " + error.Message); return null; }
        }

        private void SaveManifest(ResourceRelease release)
        {
            string path = Path.Combine(storage, ManifestName);
            string pending = path + ".pending";
            File.WriteAllText(pending, JsonUtility.ToJson(release, true));
            if (File.Exists(path))
            {
                string previous = path + ".previous";
                if (File.Exists(previous)) File.Delete(previous);
                File.Replace(pending, path, previous);
            }
            else File.Move(pending, path);
            var previousRelease = ReadManifest(path + ".previous");
            rollbackAvailable = Compatible(previousRelease) &&
                CodeUpdateLoader.HasCachedFiles(previousRelease, storage) &&
                previousRelease.version != ReadBlockedVersion();
        }

        private void Fail(string message)
        {
            Debug.LogError("[HotUpdate] " + message);
            status = message;
            Ready = false;
            busy = false;
            failed = true;
        }
    }
}
