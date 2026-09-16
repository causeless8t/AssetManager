using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Causeless3t
{
    public sealed class ResourceManager
    {
        public sealed class AssetBundleRef
        {
            public UnityEngine.AssetBundle Bundle;
            public readonly Dictionary<string, UnityEngine.Object>
                CachedDict = new();

            internal readonly Dictionary<string, Task<UnityEngine.Object>>
                LoadingTasks = new();
        }

        private readonly AssetBundleUpdater _updater = new();
        private readonly Dictionary<string, AssetBundleRef> _cachedBundles = new();
        private readonly Dictionary<string, Task<AssetBundleRef>> _loadingBundles = new();

#if UNITY_EDITOR
        private readonly Dictionary<string, UnityEngine.Object> _cachedLocalObjects = new();
#endif

        public bool IsInitialized => _updater.IsInitialized;

        public bool IsDownloading => _updater.IsDownloading;

        public void SetRemoteURL(string url)
        {
            _updater.SetRemoteUrl(url);
        }

        public Task Initialize()
        {
            return _updater.Initialize();
        }

        public Task CheckUpdateAsync(
            Action<float> onProgress = null)
        {
            return _updater.CheckUpdateAsync(onProgress);
        }

        public string GetPathByLabel(string label)
        {
            return _updater.GetPathByLabel(label);
        }

        public async Task LoadCacheByLabels(IEnumerable<string> labels)
        {
            if (labels == null)
                throw new ArgumentNullException(nameof(labels));

            var paths = labels
                .Select(GetPathByLabel)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct()
                .ToList();

            await Task.WhenAll(paths.Select(LoadCacheByPath));
        }

        public async Task<AssetBundleRef> LoadCacheByPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Bundle path cannot be empty.", nameof(path));

            if (_cachedBundles.TryGetValue(path, out var cachedBundle))
                return cachedBundle;

            if (_loadingBundles.TryGetValue(path, out var pendingLoad))
                return await pendingLoad;

            var loadingTask = LoadAndCacheBundleAsync(path);
            _loadingBundles[path] = loadingTask;

            try
            {
                return await loadingTask;
            }
            finally
            {
                if (_loadingBundles.TryGetValue(path, out var currentTask) &&
                    ReferenceEquals(currentTask, loadingTask))
                {
                    _loadingBundles.Remove(path);
                }
            }
        }

        private async Task<AssetBundleRef> LoadAndCacheBundleAsync(string path)
        {
            var fullPath = _updater.GetBundleLoadPath(path);
            var assetBundle = await LoadAssetBundleFromFileAsync(fullPath);

            if (assetBundle == null)
                return null;

            var result = new AssetBundleRef
            {
                Bundle = assetBundle
            };

            _cachedBundles[path] = result;
            return result;
        }

        public async Task<T> LoadAssetByPathAsync<T>(string bundlePath, string assetPath)
            where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("Asset path cannot be empty.", nameof(assetPath));

            var normalizedAssetPath = assetPath.Replace('\\', '/');

#if UNITY_EDITOR
            var editorAssetPath = GetEditorAssetPath(bundlePath, normalizedAssetPath);

            if (_cachedLocalObjects.TryGetValue(editorAssetPath, out var localObject))
                return localObject as T;

            var loadedAsset = AssetDatabase.LoadAssetAtPath<T>(editorAssetPath);

            if (loadedAsset != null)
                _cachedLocalObjects[editorAssetPath] = loadedAsset;

            return loadedAsset;
#else
            var bundleReference = await LoadCacheByPath(bundlePath);

            if (bundleReference == null)
                return null;

            if (bundleReference.CachedDict.TryGetValue(normalizedAssetPath, out var cachedAsset))
                return cachedAsset as T;

            if (bundleReference.LoadingTasks.TryGetValue(normalizedAssetPath, out var pendingLoad))
                return (await pendingLoad) as T;

            var loadingTask = LoadAndCacheAssetAsync(
                bundleReference,
                normalizedAssetPath,
                typeof(T));
            bundleReference.LoadingTasks[normalizedAssetPath] = loadingTask;

            try
            {
                return (await loadingTask) as T;
            }
            finally
            {
                if (bundleReference.LoadingTasks.TryGetValue(
                        normalizedAssetPath,
                        out var currentTask) &&
                    ReferenceEquals(currentTask, loadingTask))
                {
                    bundleReference.LoadingTasks.Remove(normalizedAssetPath);
                }
            }
#endif
        }

#if UNITY_EDITOR
        private static string GetEditorAssetPath(string bundlePath, string assetPath)
        {
            if (assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return assetPath;

            var bundleDirectory = Path.ChangeExtension(
                    bundlePath.Replace('\\', '/').Replace('~', '/'),
                    null)
                .Trim('/');

            return Path.Combine("Assets", bundleDirectory, assetPath)
                .Replace('\\', '/');
        }
#else
        private static async Task<UnityEngine.Object> LoadAndCacheAssetAsync(
            AssetBundleRef bundleReference,
            string assetPath,
            Type assetType)
        {
            var loadedAsset = await LoadAssetFromBundleAsync(
                bundleReference.Bundle,
                assetPath,
                assetType);

            if (loadedAsset != null)
                bundleReference.CachedDict[assetPath] = loadedAsset;

            return loadedAsset;
        }
#endif

        public async Task<GameObject> InstantiateGameObjectByPathAsync(
                string bundlePath,
                string assetPath,
                Transform parent = null,
                bool instantiateWorldSpace = false)
        {
            var prefab = await LoadAssetByPathAsync<GameObject>(bundlePath, assetPath);

            return prefab == null ? null : UnityEngine.Object.Instantiate(prefab, parent, instantiateWorldSpace);
        }

        public async Task UnloadAll(bool unloadBundles = false)
        {
            var pendingLoads = _loadingBundles.Values.ToArray();

            if (pendingLoads.Length > 0)
            {
                try
                {
                    await Task.WhenAll(pendingLoads);
                }
                catch
                {
                    // Individual load callers receive the original exception.
                    // Cleanup must continue for bundles that loaded successfully.
                }
                finally
                {
                    _loadingBundles.Clear();
                }
            }

            var pendingAssetLoads = _cachedBundles.Values
                .SelectMany(reference => reference.LoadingTasks.Values)
                .ToArray();

            if (pendingAssetLoads.Length > 0)
            {
                try
                {
                    await Task.WhenAll(pendingAssetLoads);
                }
                catch
                {
                    // Individual load callers receive the original exception.
                    // Cleanup must continue for assets that loaded successfully.
                }
            }

            foreach (var reference in _cachedBundles.Values)
                reference.LoadingTasks.Clear();

#if UNITY_EDITOR
            _cachedLocalObjects.Clear();
#endif
            foreach (var pair in _cachedBundles)
                pair.Value.CachedDict.Clear();

            if (!unloadBundles)
                return;

            var unloadTasks = _cachedBundles.Values
                .Where(reference => reference.Bundle != null)
                .Select(reference => AwaitAsyncOperation(reference.Bundle.UnloadAsync(true)));

            await Task.WhenAll(unloadTasks);
            _cachedBundles.Clear();
        }

        private static async Task<UnityEngine.AssetBundle> LoadAssetBundleFromFileAsync(string path)
        {
            var request = UnityEngine.AssetBundle.LoadFromFileAsync(path);

            await AwaitAsyncOperation(request);
            return request.assetBundle;
        }

        private static async Task<UnityEngine.Object> LoadAssetFromBundleAsync(UnityEngine.AssetBundle bundle,
                string assetPath,
                Type assetType)
        {
            var request = bundle.LoadAssetAsync(assetPath, assetType);

            await AwaitAsyncOperation(request);
            return request.asset;
        }

        private static Task AwaitAsyncOperation(AsyncOperation operation)
        {
            if (operation.isDone)
                return Task.CompletedTask;

            var completion = new TaskCompletionSource<bool>();

            operation.completed += _ => completion.TrySetResult(true);

            return completion.Task;
        }
    }
}
