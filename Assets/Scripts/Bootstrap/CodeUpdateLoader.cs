using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HybridCLR;
using UnityEngine;
using UnityEngine.Networking;

namespace Rpg.HotUpdate
{
    // Downloads immutable, hash-addressed code files before any game scene is opened.
    internal static class CodeUpdateLoader
    {
        private const string AssemblyName = "Rpg.HotUpdate";
        private static string loadedHash;

        public static bool ValidManifest(ResourceRelease release)
        {
            if (release == null || release.hotUpdateDll == null || release.aotMetadata == null) return false;
            string prefix = "releases/" + release.version + "/StandaloneWindows64/code/";
            if (!ValidFile(release.hotUpdateDll, prefix + AssemblyName + ".dll")) return false;
            foreach (var file in release.aotMetadata)
            {
                if (file == null || string.IsNullOrEmpty(file.path) ||
                    !file.path.StartsWith(prefix + "aot/", StringComparison.Ordinal)) return false;
                string name = file.path.Substring((prefix + "aot/").Length);
                if (!Regex.IsMatch(name, "^[A-Za-z0-9_.-]+\\.dll$") || !ValidFile(file, file.path)) return false;
            }
            return true;
        }

        private static bool ValidFile(ReleaseFile file, string expectedPath) =>
            file != null && file.path == expectedPath && file.bytes > 0 &&
            !string.IsNullOrEmpty(file.sha256) && Regex.IsMatch(file.sha256, "^[0-9a-fA-F]{64}$");

        public static bool HasCachedFiles(ResourceRelease release, string storage)
        {
#if UNITY_EDITOR
            return true;
#else
            if (!ValidManifest(release) || ReadCached(storage, release.hotUpdateDll) == null) return false;
            foreach (var file in release.aotMetadata)
                if (ReadCached(storage, file) == null) return false;
            return true;
#endif
        }

        public static IEnumerator Stage(ResourceRelease release, string storage, string server,
            Action<string> setStatus, Action<bool> complete)
        {
#if UNITY_EDITOR
            complete(true); // The Editor already loaded Rpg.HotUpdate from the asset database.
            yield break;
#else
            if (!ValidManifest(release)) { complete(false); yield break; }
            var files = new ReleaseFile[release.aotMetadata.Length + 1];
            Array.Copy(release.aotMetadata, files, release.aotMetadata.Length);
            files[files.Length - 1] = release.hotUpdateDll;
            foreach (var file in files)
            {
                if (ReadCached(storage, file) != null) continue;
                setStatus("Downloading game code: " + Path.GetFileName(file.path));
                byte[] bytes = null;
                using (var request = UnityWebRequest.Get(server + file.path))
                {
                    request.timeout = 30;
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success) bytes = request.downloadHandler.data;
                    else Debug.LogWarning("[HotUpdate] Code download failed: " + file.path + ": " + request.error);
                }
                if (!Matches(bytes, file))
                {
                    Debug.LogWarning("[HotUpdate] Code file is missing or has an invalid SHA-256: " + file.path);
                    complete(false);
                    yield break;
                }
                try
                {
                    string path = CachePath(storage, file);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path + ".pending", bytes);
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(path + ".pending", path);
                }
                catch (Exception error)
                {
                    Debug.LogWarning("[HotUpdate] Code cache write failed: " + error);
                    complete(false);
                    yield break;
                }
            }
            complete(true);
#endif
        }

        public static bool LoadAndRegister(ResourceRelease release, string storage, out string error)
        {
            error = null;
#if UNITY_EDITOR
            if (MainMenuRuntimeRegistry.IsRegistered && SaveManagerRuntimeRegistry.IsRegistered) return true;
            error = "Editor game code has not registered its runtime entry.";
            return false;
#else
            try
            {
                if (!ValidManifest(release)) throw new InvalidDataException("Invalid code manifest.");
                string hash = release.hotUpdateDll.sha256.ToLowerInvariant();
                if (loadedHash != null)
                {
                    if (loadedHash != hash) throw new InvalidOperationException("A different game DLL is already loaded. Restart the client.");
                    if (MainMenuRuntimeRegistry.IsRegistered && SaveManagerRuntimeRegistry.IsRegistered) return true;
                    throw new InvalidOperationException("The loaded game DLL did not register successfully. Restart the client.");
                }
                foreach (var file in release.aotMetadata)
                {
                    byte[] bytes = ReadCached(storage, file);
                    if (bytes == null) throw new InvalidDataException("AOT metadata is missing or damaged: " + file.path);
                    var result = RuntimeApi.LoadMetadataForAOTAssembly(bytes, HomologousImageMode.SuperSet);
                    if (result != LoadImageErrorCode.OK)
                        throw new InvalidOperationException("AOT metadata load failed for " + file.path + ": " + result);
                }
                byte[] dll = ReadCached(storage, release.hotUpdateDll);
                if (dll == null) throw new InvalidDataException("Game DLL is missing or damaged.");
                Assembly assembly = Assembly.Load(dll);
                loadedHash = hash;
                if (assembly.GetName().Name != AssemblyName)
                    throw new InvalidDataException("Downloaded DLL has the wrong assembly name.");
                Type entry = assembly.GetType("MenuRuntimeEntry", true);
                MethodInfo register = entry.GetMethod("Register", BindingFlags.Public | BindingFlags.Static,
                    null, Type.EmptyTypes, null);
                if (register == null || register.ReturnType != typeof(void))
                    throw new MissingMethodException("MenuRuntimeEntry.Register() is missing.");
                register.Invoke(null, null);
                if (!MainMenuRuntimeRegistry.IsRegistered || !SaveManagerRuntimeRegistry.IsRegistered)
                    throw new InvalidOperationException("Game runtime registries were not initialized.");
                return true;
            }
            catch (Exception exception)
            {
                error = (exception is TargetInvocationException invocation && invocation.InnerException != null
                    ? invocation.InnerException : exception).Message;
                Debug.LogError("[HotUpdate] Code load failed: " + exception);
                return false;
            }
#endif
        }

        private static string CachePath(string storage, ReleaseFile file) =>
            Path.Combine(storage, "Code", file.sha256.ToLowerInvariant() + ".dll");

        private static byte[] ReadCached(string storage, ReleaseFile file)
        {
            try
            {
                string path = CachePath(storage, file);
                if (!File.Exists(path) || new FileInfo(path).Length != file.bytes) return null;
                byte[] bytes = File.ReadAllBytes(path);
                return Matches(bytes, file) ? bytes : null;
            }
            catch (Exception error)
            {
                Debug.LogWarning("[HotUpdate] Code cache read failed: " + error.Message);
                return null;
            }
        }

        private static bool Matches(byte[] bytes, ReleaseFile file)
        {
            if (bytes == null || bytes.LongLength != file.bytes) return false;
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "")
                    .Equals(file.sha256, StringComparison.OrdinalIgnoreCase);
        }
    }
}
