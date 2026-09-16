using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using System.Threading.Tasks;
using Causeless3t.Security;
using Causeless3t.AssetBundle;
using UnityEngine.Networking;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Causeless3t
{ 
    public sealed class ResourceManager
    {
        private static readonly string BundleRootPath = Path.Combine(Application.persistentDataPath, "contents");
        
    #if UNITY_EDITOR
        private static readonly string StreamingInfoFilePath = Path.Combine(Application.streamingAssetsPath, "contents", AssetBundleUtil.INFO_FILE_NAME);
        private readonly Dictionary<string, UnityEngine.Object> _cachedLocalObjects = new();
    #elif UNITY_ANDROID
        private static readonly string StreamingInfoFilePath = $"jar:file://{Application.dataPath}!/assets/contents/{AssetBundleUtil.INFO_FILE_NAME}";
    #else
        private static readonly string StreamingInfoFilePath = Path.Combine(Application.dataPath, "Raw", "contents", AssetBundleUtil.INFO_FILE_NAME);
    #endif
        private static readonly string PersistentInfoFilePath = Path.Combine(BundleRootPath, AssetBundleUtil.INFO_FILE_NAME);
        
        private ContentsInfoList _contentsInfoList;

        public class AssetBundleRef 
        {
            public UnityEngine.AssetBundle Bundle;
            public Dictionary<string, UnityEngine.Object> CachedDict = new();
        }

        private readonly Dictionary<string, AssetBundleRef> _cachedBundles = new();

        private string _remoteURL;
        public void SetRemoteURL(string url) => _remoteURL = url;
        
        #region IManager
        
        public bool IsInitialized { get; private set; }

        public async Task Initialize()
        {
            IsInitialized = false;
            Directory.CreateDirectory(BundleRootPath);

#if UNITY_EDITOR
            if (File.Exists(StreamingInfoFilePath))
            {
                var editorInfoText = await File.ReadAllTextAsync(StreamingInfoFilePath);
                _contentsInfoList = JsonUtility.FromJson<ContentsInfoList>(editorInfoText);
            }
#else
            var infoFileText = await ReadStreamingInfoAsync();
            var streamingInfoList = JsonUtility.FromJson<ContentsInfoList>(infoFileText);
            if (streamingInfoList == null)
                throw new InvalidDataException("StreamingAssets contents manifest is invalid.");

            if (!Application.version.Equals(streamingInfoList.AppVersion))
                ClearBundleFiles();

            if (!File.Exists(PersistentInfoFilePath))
                await File.WriteAllTextAsync(PersistentInfoFilePath, infoFileText);

            var persistentInfoText = await File.ReadAllTextAsync(PersistentInfoFilePath);
            _contentsInfoList = JsonUtility.FromJson<ContentsInfoList>(persistentInfoText);
#endif
            IsInitialized = true;
        }
        
        #endregion IManager

        public bool IsDownloading { get; private set; }
        
        private void ClearBundleFiles()
        {
            var files = Directory.GetFiles(BundleRootPath);
            foreach (var file in files)
                File.Delete(file);
        }

        public async Task CheckUpdateAsync(Action<float> onProgress = null)
        {
            if (!IsInitialized)
                throw new InvalidOperationException("ResourceManager must be initialized before checking for updates.");

            if (IsDownloading)
                throw new InvalidOperationException("An asset update is already in progress.");

            IsDownloading = true;

            try
            {
#if !UNITY_EDITOR
                await DownloadUpdatableFiles(onProgress);
                var infoFileText = await File.ReadAllTextAsync(PersistentInfoFilePath);
                _contentsInfoList = JsonUtility.FromJson<ContentsInfoList>(infoFileText);
#endif
            }
            finally
            {
                IsDownloading = false;
            }
        }

        private async Task DownloadUpdatableFiles(Action<float> onProgress = null)
        {
            if (string.IsNullOrWhiteSpace(_remoteURL))
                throw new InvalidOperationException("Remote URL must be configured before checking for updates.");

            var localInfoText = await File.ReadAllTextAsync(PersistentInfoFilePath);
            var localInfoList = JsonUtility.FromJson<ContentsInfoList>(localInfoText)
                ?? throw new InvalidDataException("The local contents manifest is invalid.");

            var remoteInfoText = await DownloadTextAsync(
                CombineRemoteUrl(_remoteURL, AssetBundleUtil.INFO_FILE_NAME));
            var remoteInfoList = JsonUtility.FromJson<ContentsInfoList>(remoteInfoText)
                ?? throw new InvalidDataException("The remote contents manifest is invalid.");

            if (localInfoList.Revision == remoteInfoList.Revision)
                return;

            var modifiedInfoList = CompareFileInfoList(localInfoList, remoteInfoList);
            var removedPaths = modifiedInfoList.GetRemovableFiles();

            if (modifiedInfoList.FileInfos.Count == 0 && removedPaths.Count == 0)
            {
                await ReplaceManifestAsync(remoteInfoList);
                return;
            }

            long downloadSize = modifiedInfoList.FileInfos.Sum(info => info.Size);
            Debug.Log(
                $"Found CDN Downloadable Files {modifiedInfoList.FileInfos.Count}, " +
                $"Size {downloadSize / (1024 * 1024)}MB");

            var temporaryFiles = new List<(string TemporaryPath, string FinalPath)>();
            var temporaryLock = new object();
            var completed = 0;

            using var semaphore = new SemaphoreSlim(5);
            var tasks = modifiedInfoList.FileInfos.Select(async info =>
            {
                await semaphore.WaitAsync();

                try
                {
                    var finalPath = GetBundleFilePath(info.Path);
                    var temporaryPath = finalPath + ".download";

                    var directory = Path.GetDirectoryName(finalPath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);

                    var data = await DownloadBytesAsync(
                        CombineRemoteUrl(_remoteURL, info.Path));

                    var hash = CRC32.Compute(data).ToString();
                    if (info.Size != data.LongLength ||
                        !string.Equals(info.Hash, hash, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"Downloaded bundle validation failed: {info.Path}");
                    }

                    await File.WriteAllBytesAsync(temporaryPath, data);

                    lock (temporaryLock)
                        temporaryFiles.Add((temporaryPath, finalPath));

                    var finished = Interlocked.Increment(ref completed);
                    onProgress?.Invoke(
                        finished / (float)modifiedInfoList.FileInfos.Count);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            try
            {
                await Task.WhenAll(tasks);

                foreach (var file in temporaryFiles)
                {
                    if (File.Exists(file.FinalPath))
                        File.Delete(file.FinalPath);

                    File.Move(file.TemporaryPath, file.FinalPath);
                }

                foreach (var removedPath in removedPaths)
                {
                    var filePath = GetBundleFilePath(removedPath);
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }

                await ReplaceManifestAsync(remoteInfoList);
            }
            catch
            {
                foreach (var file in temporaryFiles)
                {
                    if (File.Exists(file.TemporaryPath))
                        File.Delete(file.TemporaryPath);
                }

                throw;
            }
        }
        
        private ContentsInfoList CompareFileInfoList(ContentsInfoList localList, ContentsInfoList renewalList)
        {
            ContentsInfoList retVal = new ContentsInfoList();
            Dictionary<string, ContentsInfo> localDict = localList.FileInfos.ToDictionary(item => item.Path, item => item);
            Dictionary<string, ContentsInfo> renewalDict = renewalList.FileInfos.ToDictionary(item => item.Path, item => item);
		
            // 수정된 파일 리스트 검사.
            foreach (KeyValuePair<string, ContentsInfo> pair in localDict)
            {
                if (renewalDict.TryGetValue(pair.Key, out var renewalInfo)) //기존파일 존재.
                {
                    ContentsInfo existingInfo = localDict[pair.Key];
                    if (existingInfo.CompareTo(renewalInfo) != 0) // 수정된 파일.
                        retVal.FileInfos.Add(renewalInfo);	// content that must be replaced
                }
                else  // exist in local but none in patch
                    retVal.AddRemovableFile(pair.Value); // content that must be deleted
            }
		
            // 새로운 파일 리스트 검사.
            foreach (KeyValuePair<string, ContentsInfo> pair in renewalDict)
            {
                if (!localDict.TryGetValue(pair.Key, out _))
                    retVal.FileInfos.Add(pair.Value); // content that must be added
            }
            return retVal;
        }

        private bool IsExistsPersistentPath(string path) => File.Exists(GetBundleFilePath(path));
        public string GetPathByLabel(string label) => _contentsInfoList?.FileInfos.FirstOrDefault(info => info.Label == label)?.Path;

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

            var fullPath = IsExistsPersistentPath(path)
                ? GetBundleFilePath(path)
                : Path.Combine(Application.streamingAssetsPath, "contents", path);

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
        
        public async Task<T> LoadAssetByPathAsync<T>(
            string bundlePath,
            string assetPath)
            where T : UnityEngine.Object
        {
    #if UNITY_EDITOR
            var bundleConvertedPath = Path.ChangeExtension(
                bundlePath.Replace('~', '/'),
                null);
            var directoryPath = Path.Combine(
                Application.dataPath,
                bundleConvertedPath);
            var directory = new DirectoryInfo(directoryPath);

            if (!directory.Exists)
                return null;

            var assetFile = directory
                .GetFiles()
                .FirstOrDefault(file => file.Name.Contains(assetPath));

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

            var loadedAsset = AssetDatabase.LoadAssetAtPath<T>(filePath);
            _cachedLocalObjects[filePath] = loadedAsset;
            return loadedAsset;
    #else
            if (!_cachedBundles.TryGetValue(
                    bundlePath,
                    out var bundleReference))
            {
                bundleReference = await LoadCacheByPath(bundlePath);
            }

            if (bundleReference == null)
                return null;

            if (bundleReference.CachedDict.TryGetValue(
                    assetPath,
                    out var cachedAsset))
            {
                return cachedAsset as T;
            }

            var loadedAsset = await LoadAssetFromBundleAsync(
                bundleReference.Bundle,
                assetPath,
                typeof(T));

            if (loadedAsset != null)
                bundleReference.CachedDict[assetPath] = loadedAsset;

            return loadedAsset as T;
    #endif
        }
        
        public async Task<GameObject> InstantiateGameObjectByPathAsync(
            string bundlePath,
            string assetPath,
            Transform parent = null,
            bool instantiateWorldSpace = false)
        {
            var prefab = await LoadAssetByPathAsync<GameObject>(
                bundlePath,
                assetPath);

            return prefab == null
                ? null
                : UnityEngine.Object.Instantiate(
                    prefab,
                    parent,
                    instantiateWorldSpace);
        }

        public async Task UnloadAll(bool unloadBundles = false)
        {
    #if UNITY_EDITOR
            _cachedLocalObjects.Clear();
    #endif
            foreach (var pair in _cachedBundles)
                pair.Value.CachedDict.Clear();
            
            if (!unloadBundles)
                return;

            var unloadTasks = _cachedBundles.Values
                .Where(reference => reference.Bundle != null)
                .Select(reference =>
                    AwaitAsyncOperation(
                        reference.Bundle.UnloadAsync(true)));

            await Task.WhenAll(unloadTasks);
            _cachedBundles.Clear();
        }

        private static async Task<string> ReadStreamingInfoAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return await DownloadTextAsync(StreamingInfoFilePath);
#else
            return await File.ReadAllTextAsync(StreamingInfoFilePath);
#endif
        }

        private static async Task<string> DownloadTextAsync(string url)
        {
            using var request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.useHttpContinue = false;
            request.timeout = 15;

            await SendWebRequestAsync(request);
            ThrowIfRequestFailed(request);

            return request.downloadHandler.text;
        }

        private static async Task<byte[]> DownloadBytesAsync(string url)
        {
            using var request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.useHttpContinue = false;
            request.timeout = 15;

            await SendWebRequestAsync(request);
            ThrowIfRequestFailed(request);

            return request.downloadHandler.data;
        }

        private static async Task ReplaceManifestAsync(
            ContentsInfoList manifest)
        {
            Directory.CreateDirectory(BundleRootPath);

            var temporaryPath = PersistentInfoFilePath + ".download";
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonUtility.ToJson(manifest));

            if (File.Exists(PersistentInfoFilePath))
                File.Delete(PersistentInfoFilePath);

            File.Move(temporaryPath, PersistentInfoFilePath);
        }

        private static string CombineRemoteUrl(
            string baseUrl,
            string relativePath)
        {
            return $"{baseUrl.TrimEnd('/')}/{relativePath.TrimStart('/', '\\')}";
        }

        private static string GetBundleFilePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new InvalidDataException("Bundle path cannot be empty.");

            var normalizedPath = relativePath
                .Replace('\\', '/')
                .TrimStart('/');

            if (normalizedPath
                .Split('/')
                .Any(segment => segment == ".."))
            {
                throw new InvalidDataException(
                    $"Invalid bundle path: {relativePath}");
            }

            return Path.Combine(BundleRootPath, normalizedPath);
        }

        private static void ThrowIfRequestFailed(
            UnityWebRequest request)
        {
            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new IOException(
                    $"Request failed ({request.responseCode}): " +
                    request.error);
            }
        }

        private static Task SendWebRequestAsync(
            UnityWebRequest request)
        {
            return AwaitAsyncOperation(request.SendWebRequest());
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

        private static async Task<UnityEngine.AssetBundle>
            LoadAssetBundleFromFileAsync(string path)
        {
            var request =
                UnityEngine.AssetBundle.LoadFromFileAsync(path);

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
                bundle.LoadAssetAsync(assetPath, assetType);

            await AwaitAsyncOperation(request);
            return request.asset;
        }
    }
}
