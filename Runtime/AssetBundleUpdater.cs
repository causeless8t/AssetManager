using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Causeless3t.AssetBundle;
using Causeless3t.Security;
using UnityEngine;
using UnityEngine.Networking;

namespace Causeless3t
{
    internal sealed class AssetBundleUpdater
    {
        private const int MaxConcurrentDownloads = 5;
        private const int RequestTimeoutSeconds = 15;

        private static readonly string BundleRootPath = Path.Combine(Application.persistentDataPath, "contents");

#if UNITY_EDITOR
        private static readonly string StreamingInfoFilePath = Path.Combine(
                Application.streamingAssetsPath,
                "contents",
                AssetBundleUtil.INFO_FILE_NAME);
#elif UNITY_ANDROID
        private static readonly string StreamingInfoFilePath = $"jar:file://{Application.dataPath}!/assets/contents/{AssetBundleUtil.INFO_FILE_NAME}";
#else
        private static readonly string StreamingInfoFilePath = Path.Combine(Application.dataPath,
                "Raw",
                "contents",
                AssetBundleUtil.INFO_FILE_NAME);
#endif

        private static readonly string PersistentInfoFilePath = Path.Combine(BundleRootPath, AssetBundleUtil.INFO_FILE_NAME);

        private ContentsInfoList _contentsInfoList;
        private string _remoteUrl;

        internal bool IsInitialized { get; private set; }
        internal bool IsDownloading { get; private set; }

        internal void SetRemoteUrl(string url)
        {
            _remoteUrl = url;
        }

        internal async Task Initialize()
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
            {
                throw new InvalidDataException("StreamingAssets contents manifest is invalid.");
            }

            if (!Application.version.Equals(streamingInfoList.AppVersion))
            {
                ClearBundleFiles();
            }

            if (!File.Exists(PersistentInfoFilePath))
            {
                await File.WriteAllTextAsync(PersistentInfoFilePath,infoFileText);
            }

            var persistentInfoText = await File.ReadAllTextAsync(PersistentInfoFilePath);
            _contentsInfoList = JsonUtility.FromJson<ContentsInfoList>(persistentInfoText);
#endif

            IsInitialized = true;
        }

        internal async Task CheckUpdateAsync(Action<float> onProgress = null)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("ResourceManager must be initialized " +
                                                    "before checking for updates.");
            }

            if (IsDownloading)
            {
                throw new InvalidOperationException("An asset update is already in progress.");
            }

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

        internal string GetPathByLabel(string label)
        {
            return _contentsInfoList?.FileInfos.FirstOrDefault(info => info.Label == label)?.Path;
        }

        internal string GetBundleLoadPath(string relativePath)
        {
            var normalizedPath = NormalizeRelativePath(relativePath);
            var persistentPath = Path.Combine(BundleRootPath, normalizedPath);

            return File.Exists(persistentPath)
                ? persistentPath
                : Path.Combine(Application.streamingAssetsPath,
                    "contents",
                    normalizedPath);
        }

        private static void ClearBundleFiles()
        {
            if (!Directory.Exists(BundleRootPath))
                return;

            foreach (var file in Directory.GetFiles(BundleRootPath, "*", SearchOption.AllDirectories))
            {
                File.Delete(file);
            }
        }

        private async Task DownloadUpdatableFiles(Action<float> onProgress)
        {
            if (string.IsNullOrWhiteSpace(_remoteUrl))
            {
                throw new InvalidOperationException("Remote URL must be configured before " +
                                                    "checking for updates.");
            }

            var localInfoText = await File.ReadAllTextAsync(PersistentInfoFilePath);
            var localInfoList = JsonUtility.FromJson<ContentsInfoList>(localInfoText)
                                ?? throw new InvalidDataException("The local contents manifest is invalid.");

            var remoteInfoText = await DownloadTextAsync(CombineRemoteUrl(_remoteUrl, AssetBundleUtil.INFO_FILE_NAME));
            var remoteInfoList = JsonUtility.FromJson<ContentsInfoList>(remoteInfoText) 
                                 ?? throw new InvalidDataException("The remote contents manifest is invalid.");

            if (localInfoList.Revision == remoteInfoList.Revision)
            {
                return;
            }

            var modifiedInfoList = CompareFileInfoList(localInfoList, remoteInfoList);
            var removedPaths = modifiedInfoList.GetRemovableFiles();

            if (modifiedInfoList.FileInfos.Count == 0 && removedPaths.Count == 0)
            {
                await ReplaceManifestAsync(remoteInfoList);
                return;
            }

            var downloadSize = modifiedInfoList.FileInfos.Sum(info => info.Size);
            Debug.Log("Found CDN Downloadable Files " +
                      $"{modifiedInfoList.FileInfos.Count}, " +
                      $"Size {downloadSize / (1024 * 1024)}MB");

            var temporaryFiles = new List<(string TemporaryPath, string FinalPath)>();
            var temporaryLock = new object();
            var completed = 0;

            using var semaphore = new SemaphoreSlim(MaxConcurrentDownloads);

            var tasks = modifiedInfoList.FileInfos.Select(async info =>
                    {
                        await semaphore.WaitAsync();

                        try
                        {
                            var finalPath = GetBundleFilePath(info.Path);
                            var temporaryPath = finalPath + ".download";

                            var directory = Path.GetDirectoryName(finalPath);
                            if (!string.IsNullOrEmpty(directory))
                            {
                                Directory.CreateDirectory(directory);
                            }

                            if (File.Exists(temporaryPath))
                            {
                                File.Delete(temporaryPath);
                            }

                            var data = await DownloadBytesAsync(CombineRemoteUrl(_remoteUrl, info.Path));

                            ValidateDownload(info, data);
                            await File.WriteAllBytesAsync(temporaryPath, data);

                            lock (temporaryLock)
                            {
                                temporaryFiles.Add((temporaryPath, finalPath));
                            }

                            var finished = Interlocked.Increment(ref completed);
                            onProgress?.Invoke(finished / (float)modifiedInfoList.FileInfos.Count);
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
                    {
                        File.Delete(file.TemporaryPath);
                    }
                }

                throw;
            }
        }

        private static ContentsInfoList CompareFileInfoList(ContentsInfoList localList, ContentsInfoList remoteList)
        {
            var result = new ContentsInfoList();
            var localFiles = localList.FileInfos.ToDictionary(item => item.Path,
                item => item);
            var remoteFiles = remoteList.FileInfos.ToDictionary(item => item.Path,
                item => item);

            foreach (var pair in localFiles)
            {
                if (remoteFiles.TryGetValue(pair.Key, out var remoteInfo))
                {
                    if (!pair.Value.HasSameContent(remoteInfo))
                    {
                        result.FileInfos.Add(remoteInfo);
                    }
                }
                else
                {
                    result.AddRemovableFile(pair.Value);
                }
            }

            foreach (var pair in remoteFiles)
            {
                if (!localFiles.ContainsKey(pair.Key))
                    result.FileInfos.Add(pair.Value);
            }

            return result;
        }

        private static void ValidateDownload(ContentsInfo info, byte[] data)
        {
            var hash = CRC32.Compute(data).ToString();

            if (info.Size != data.LongLength || !string.Equals(info.Hash, hash, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Downloaded bundle validation " +
                                               $"failed: {info.Path}");
            }
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
            ConfigureRequest(request);

            await AwaitAsyncOperation(request.SendWebRequest());
            ThrowIfRequestFailed(request);

            return request.downloadHandler.text;
        }

        private static async Task<byte[]> DownloadBytesAsync(string url)
        {
            using var request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerBuffer();
            ConfigureRequest(request);

            await AwaitAsyncOperation(request.SendWebRequest());
            ThrowIfRequestFailed(request);

            return request.downloadHandler.data;
        }

        private static void ConfigureRequest(UnityWebRequest request)
        {
            request.useHttpContinue = false;
            request.timeout = RequestTimeoutSeconds;
        }

        private static async Task ReplaceManifestAsync(ContentsInfoList manifest)
        {
            Directory.CreateDirectory(BundleRootPath);

            var temporaryPath = PersistentInfoFilePath + ".download";
            await File.WriteAllTextAsync(temporaryPath, JsonUtility.ToJson(manifest));

            if (File.Exists(PersistentInfoFilePath))
                File.Delete(PersistentInfoFilePath);

            File.Move(temporaryPath, PersistentInfoFilePath);
        }

        private static string CombineRemoteUrl(string baseUrl, string relativePath)
        {
            return $"{baseUrl.TrimEnd('/')}/" + $"{relativePath.TrimStart('/', '\\')}";
        }

        private static string GetBundleFilePath(string relativePath)
        {
            return Path.Combine(BundleRootPath, NormalizeRelativePath(relativePath));
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new InvalidDataException("Bundle path cannot be empty.");
            }

            var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');

            if (normalizedPath.Split('/').Any(segment => segment == ".."))
            {
                throw new InvalidDataException("Invalid bundle path: " +
                                               $"{relativePath}");
            }

            return normalizedPath;
        }

        private static void ThrowIfRequestFailed(UnityWebRequest request)
        {
            if (request.result !=
                UnityWebRequest.Result.Success)
            {
                throw new IOException($"Request failed " +
                                      $"({request.responseCode}): " +
                                      request.error);
            }
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
