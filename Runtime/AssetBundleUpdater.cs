using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
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

        internal async Task CheckUpdateAsync(
            Action<float> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
                await DownloadUpdatableFiles(onProgress, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
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

        internal IReadOnlyList<string> GetDependencies(string path)
        {
            var fileInfo = _contentsInfoList?.FileInfos.FirstOrDefault(
                info => string.Equals(info.Path, path, StringComparison.Ordinal));

            return fileInfo?.Dependencies ?? (IReadOnlyList<string>)Array.Empty<string>();
        }

        internal bool TryGetPersistentBundlePath(
            string relativePath,
            out string path)
        {
            var normalizedPath = NormalizeRelativePath(relativePath);
            path = Path.Combine(BundleRootPath, normalizedPath);

            return File.Exists(path);
        }

        internal string GetStreamingBundlePath(string relativePath)
        {
            var normalizedPath = NormalizeRelativePath(relativePath);

            return $"{Application.streamingAssetsPath.TrimEnd('/')}/" +
                   $"contents/{normalizedPath}";
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

        private async Task DownloadUpdatableFiles(
            Action<float> onProgress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(_remoteUrl))
            {
                throw new InvalidOperationException("Remote URL must be configured before " +
                                                    "checking for updates.");
            }

            var localInfoText = await File.ReadAllTextAsync(PersistentInfoFilePath);
            cancellationToken.ThrowIfCancellationRequested();

            var localInfoList = JsonUtility.FromJson<ContentsInfoList>(localInfoText)
                                ?? throw new InvalidDataException("The local contents manifest is invalid.");

            var remoteInfoText = await DownloadTextAsync(
                CombineRemoteUrl(_remoteUrl, AssetBundleUtil.INFO_FILE_NAME),
                cancellationToken);
            var remoteInfoList = JsonUtility.FromJson<ContentsInfoList>(remoteInfoText)
                                 ?? throw new InvalidDataException("The remote contents manifest is invalid.");

            if (localInfoList.Revision == remoteInfoList.Revision)
            {
                onProgress?.Invoke(1f);
                return;
            }

            var modifiedInfoList = CompareFileInfoList(localInfoList, remoteInfoList);
            var removedPaths = modifiedInfoList.GetRemovableFiles();

            if (modifiedInfoList.FileInfos.Count == 0 && removedPaths.Count == 0)
            {
                await ReplaceManifestAsync(remoteInfoList, cancellationToken);
                onProgress?.Invoke(1f);
                return;
            }

            var temporaryFiles = new List<(string TemporaryPath, string FinalPath)>();
            var temporaryLock = new object();
            var failureLock = new object();
            var progressLock = new object();
            Exception firstFailure = null;
            var downloadedBytes = 0L;
            var totalDownloadBytes = modifiedInfoList.FileInfos.Sum(info =>
                Math.Max(0L, info.Size));

            using var semaphore = new SemaphoreSlim(MaxConcurrentDownloads);
            using var batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            var batchToken = batchCancellation.Token;

            var tasks = modifiedInfoList.FileInfos.Select(async info =>
            {
                await semaphore.WaitAsync(batchToken);
                string temporaryPath = null;

                try
                {
                    var finalPath = GetBundleFilePath(info.Path);
                    temporaryPath = finalPath + ".download";
                    var reportedBytes = 0L;

                    void ReportDownloadedBytes(ulong value)
                    {
                        var expectedBytes = (ulong)Math.Max(0L, info.Size);
                        var currentBytes = (long)Math.Min(value, expectedBytes);

                        lock (progressLock)
                        {
                            if (currentBytes <= reportedBytes)
                                return;

                            downloadedBytes += currentBytes - reportedBytes;
                            reportedBytes = currentBytes;

                            if (totalDownloadBytes > 0)
                            {
                                var progress = Math.Min(
                                    1d,
                                    downloadedBytes / (double)totalDownloadBytes);
                                onProgress?.Invoke((float)progress);
                            }
                        }
                    }

                    var directory = Path.GetDirectoryName(finalPath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);

                    var data = await DownloadBytesAsync(
                        CombineRemoteUrl(_remoteUrl, info.Path),
                        batchToken,
                        ReportDownloadedBytes);

                    ValidateDownload(info, data);
                    ReportDownloadedBytes((ulong)data.LongLength);
                    batchToken.ThrowIfCancellationRequested();
                    await File.WriteAllBytesAsync(temporaryPath, data);
                    batchToken.ThrowIfCancellationRequested();

                    lock (temporaryLock)
                    {
                        temporaryFiles.Add((temporaryPath, finalPath));
                    }

                }
                catch (Exception exception)
                {
                    if (temporaryPath != null && File.Exists(temporaryPath))
                        File.Delete(temporaryPath);

                    if (!(exception is OperationCanceledException && batchToken.IsCancellationRequested))
                    {
                        lock (failureLock)
                        {
                            firstFailure ??= exception;
                        }
                    }

                    batchCancellation.Cancel();
                    throw;
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            try
            {
                await Task.WhenAll(tasks);
                cancellationToken.ThrowIfCancellationRequested();

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

                // Bundle replacement and manifest replacement are one commit phase.
                // Once it starts, finish it to avoid mixing new bundles with an old manifest.
                await ReplaceManifestAsync(remoteInfoList, CancellationToken.None);

                if (totalDownloadBytes <= 0)
                    onProgress?.Invoke(1f);
            }
            catch
            {
                foreach (var info in modifiedInfoList.FileInfos)
                {
                    var temporaryPath = GetBundleFilePath(info.Path) + ".download";
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (firstFailure != null)
                    ExceptionDispatchInfo.Capture(firstFailure).Throw();

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
            return await DownloadTextAsync(StreamingInfoFilePath, CancellationToken.None);
#else
            return await File.ReadAllTextAsync(StreamingInfoFilePath);
#endif
        }

        private static async Task<string> DownloadTextAsync(
            string url,
            CancellationToken cancellationToken)
        {
            using var request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerBuffer();
            ConfigureRequest(request);

            await SendRequestAsync(request, cancellationToken);

            return request.downloadHandler.text;
        }

        private static async Task<byte[]> DownloadBytesAsync(
            string url,
            CancellationToken cancellationToken,
            Action<ulong> onDownloadedBytes)
        {
            using var request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerBuffer();
            ConfigureRequest(request);

            await SendRequestAsync(
                request,
                cancellationToken,
                onDownloadedBytes);

            return request.downloadHandler.data;
        }

        private static async Task SendRequestAsync(
            UnityWebRequest request,
            CancellationToken cancellationToken,
            Action<ulong> onDownloadedBytes = null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (cancellationToken.Register(request.Abort))
            {
                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    onDownloadedBytes?.Invoke(request.downloadedBytes);
                    await Task.Yield();
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            onDownloadedBytes?.Invoke(request.downloadedBytes);
            ThrowIfRequestFailed(request);
        }

        private static void ConfigureRequest(UnityWebRequest request)
        {
            request.useHttpContinue = false;
            request.timeout = RequestTimeoutSeconds;
        }

        private static async Task ReplaceManifestAsync(
            ContentsInfoList manifest,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(BundleRootPath);

            var temporaryPath = PersistentInfoFilePath + ".download";
            try
            {
                await File.WriteAllTextAsync(
                    temporaryPath,
                    JsonUtility.ToJson(manifest));
                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(PersistentInfoFilePath))
                    File.Delete(PersistentInfoFilePath);

                File.Move(temporaryPath, PersistentInfoFilePath);
            }
            catch
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);

                throw;
            }
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

    }
}
