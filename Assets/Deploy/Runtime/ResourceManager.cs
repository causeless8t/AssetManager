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
        }

        private readonly AssetBundleUpdater _updater = new();
        private readonly Dictionary<string, AssetBundleRef>
            _cachedBundles = new();

#if UNITY_EDITOR
        private readonly Dictionary<string, UnityEngine.Object>
            _cachedLocalObjects = new();
#endif

        public bool IsInitialized =>
            _updater.IsInitialized;

        public bool IsDownloading =>
            _updater.IsDownloading;

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

        public async Task LoadCacheByLabels(
            IEnumerable<string> labels)
        {
            if (labels == null)
                throw new ArgumentNullException(
                    nameof(labels));

            var paths = labels
                .Select(GetPathByLabel)
                .Where(path =>
                    !string.IsNullOrEmpty(path))
                .Distinct()
                .ToList();

            await Task.WhenAll(
                paths.Select(LoadCacheByPath));
        }

        public async Task<AssetBundleRef>
            LoadCacheByPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Bundle path cannot be empty.",
                    nameof(path));
            }

            if (_cachedBundles.TryGetValue(
                    path,
                    out var cachedBundle))
            {
                return cachedBundle;
            }

            var fullPath =
                _updater.GetBundleLoadPath(path);
            var assetBundle =
                await LoadAssetBundleFromFileAsync(
                    fullPath);

            if (assetBundle == null)
                return null;

            var result = new AssetBundleRef
            {
                Bundle = assetBundle
            };

            _cachedBundles[path] = result;
            return result;
        }

        public async Task<T> LoadAssetByPathAsync<T>(
            string bundlePath,
            string assetPath)
            where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            var bundleConvertedPath =
                Path.ChangeExtension(
                    bundlePath.Replace('~', '/'),
                    null);
            var directoryPath =
                Path.Combine(
                    Application.dataPath,
                    bundleConvertedPath);
            var directory =
                new DirectoryInfo(directoryPath);

            if (!directory.Exists)
                return null;

            var assetFile = directory
                .GetFiles()
                .FirstOrDefault(file =>
                    file.Name.Contains(assetPath));

            if (assetFile == null)
                return null;

            var filePath = Path.Combine(
                    "Assets",
                    bundleConvertedPath,
                    assetFile.Name)
                .Replace('\\', '/');

            if (_cachedLocalObjects.TryGetValue(
                    filePath,
                    out var localObject))
            {
                return localObject as T;
            }

            var loadedAsset =
                AssetDatabase.LoadAssetAtPath<T>(
                    filePath);
            _cachedLocalObjects[filePath] =
                loadedAsset;
            return loadedAsset;
#else
            if (!_cachedBundles.TryGetValue(
                    bundlePath,
                    out var bundleReference))
            {
                bundleReference =
                    await LoadCacheByPath(bundlePath);
            }

            if (bundleReference == null)
                return null;

            if (bundleReference.CachedDict.TryGetValue(
                    assetPath,
                    out var cachedAsset))
            {
                return cachedAsset as T;
            }

            var loadedAsset =
                await LoadAssetFromBundleAsync(
                    bundleReference.Bundle,
                    assetPath,
                    typeof(T));

            if (loadedAsset != null)
            {
                bundleReference.CachedDict[assetPath] =
                    loadedAsset;
            }

            return loadedAsset as T;
#endif
        }

        public async Task<GameObject>
            InstantiateGameObjectByPathAsync(
                string bundlePath,
                string assetPath,
                Transform parent = null,
                bool instantiateWorldSpace = false)
        {
            var prefab =
                await LoadAssetByPathAsync<GameObject>(
                    bundlePath,
                    assetPath);

            return prefab == null
                ? null
                : UnityEngine.Object.Instantiate(
                    prefab,
                    parent,
                    instantiateWorldSpace);
        }

        public async Task UnloadAll(
            bool unloadBundles = false)
        {
#if UNITY_EDITOR
            _cachedLocalObjects.Clear();
#endif
            foreach (var pair in _cachedBundles)
                pair.Value.CachedDict.Clear();

            if (!unloadBundles)
                return;

            var unloadTasks =
                _cachedBundles.Values
                    .Where(reference =>
                        reference.Bundle != null)
                    .Select(reference =>
                        AwaitAsyncOperation(
                            reference.Bundle
                                .UnloadAsync(true)));

            await Task.WhenAll(unloadTasks);
            _cachedBundles.Clear();
        }

        private static async
            Task<UnityEngine.AssetBundle>
            LoadAssetBundleFromFileAsync(string path)
        {
            var request =
                UnityEngine.AssetBundle
                    .LoadFromFileAsync(path);

            await AwaitAsyncOperation(request);
            return request.assetBundle;
        }

        private static async Task<UnityEngine.Object>
            LoadAssetFromBundleAsync(
                UnityEngine.AssetBundle bundle,
                string assetPath,
                Type assetType)
        {
            var request =
                bundle.LoadAssetAsync(
                    assetPath,
                    assetType);

            await AwaitAsyncOperation(request);
            return request.asset;
        }

        private static Task AwaitAsyncOperation(
            AsyncOperation operation)
        {
            if (operation.isDone)
                return Task.CompletedTask;

            var completion =
                new TaskCompletionSource<bool>();

            operation.completed += _ =>
                completion.TrySetResult(true);

            return completion.Task;
        }
    }
}
