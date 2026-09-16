using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
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

        public async UniTask Initialize()
        {
            IsInitialized = false;
#if !UNITY_EDITOR
            var infoFileText = await File.ReadAllTextAsync(StreamingInfoFilePath);
            var streamingInfoList = JsonUtility.FromJson<ContentsInfoList>(infoFileText);
            if (!Application.version.Equals(streamingInfoList.AppVersion))
                ClearBundleFiles();
            if (!File.Exists(PersistentInfoFilePath))
                File.Copy(StreamingInfoFilePath, PersistentInfoFilePath);
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

        public async UniTask CheckUpdateAsync(Action<float> onProgress = null)
        {
            IsDownloading = true;
            await UniTask.WaitUntil(() => IsInitialized);
#if !UNITY_EDITOR
            await DownloadUpdatableFiles(onProgress);
            var infoFileText = await File.ReadAllTextAsync(PersistentInfoFilePath);
            _contentsInfoList = JsonUtility.FromJson<ContentsInfoList>(infoFileText);
#endif
            IsDownloading = false;
            await UniTask.CompletedTask;
        }

        private async UniTask DownloadUpdatableFiles(Action<float> onProgress = null)
        {
            // 1. compare with remote fileinfo.dat and local
            var infoFileText = await File.ReadAllTextAsync(PersistentInfoFilePath);
            var localInfoList = JsonUtility.FromJson<ContentsInfoList>(infoFileText);
            ContentsInfoList remoteInfoList;

            using (var www = UnityWebRequest.Get($"{_remoteURL}/{AssetBundleUtil.INFO_FILE_NAME}"))
            {
                www.downloadHandler = new DownloadHandlerBuffer();
                // www.SetRequestHeader("Content-Type", "application/json");
                // www.SetRequestHeader("Authorization", $"Bearer {SessionKey}");
                www.useHttpContinue = false;
                www.timeout = 15;
                await www.SendWebRequest();
                if ((www.result != UnityWebRequest.Result.Success && www.result != UnityWebRequest.Result.InProgress) || www.error != null)
                {
                    Debug.LogError($"network is not reachable !! {www.error}");
                    return;
                }
                while (!www.downloadHandler.isDone)
                    await UniTask.Yield();
                remoteInfoList = JsonUtility.FromJson<ContentsInfoList>(www.downloadHandler.text);
                if (remoteInfoList == null) return;
            }
            if (localInfoList.Revision == remoteInfoList.Revision) return;
            
            // 2. check size to download
            var modifiedInfoList = CompareFileInfoList(localInfoList, remoteInfoList);
            if (modifiedInfoList.FileInfos.Count == 0) return;
            
            // 3. Log the required download size.
            long downloadSize = 0;
            modifiedInfoList.FileInfos.ForEach(info => downloadSize += info.Size);
            
            Debug.Log($"Found CDN Downloadable Files {modifiedInfoList.FileInfos.Count}, Size {downloadSize/(1024*1024)}MB");

            // 4. Remove/Download CDN Files
            List<string> removedKeys = modifiedInfoList.GetRemovableFiles();
            foreach (string path in removedKeys)
            {
                string filePath = Path.Combine(BundleRootPath, path);
                File.Delete(filePath);
                Debug.Log("cdn > removed - " + filePath);
            }
            
            using (var semaphore = new SemaphoreSlim(5))
            {
                int completed = 0;
                var tasks = new List<UniTask>();
                for (int i=0; i<modifiedInfoList.FileInfos.Count; ++i)
                {
                    var info = modifiedInfoList.FileInfos[i];
                    await semaphore.WaitAsync(); // 세마포어 획득
                    var downloadURL = $"{_remoteURL}{info.Path}";
                    var savePath = Path.Combine(BundleRootPath, info.Path);
                    tasks.Add(UniTask.Create(async () =>
                    {
                        try
                        {
                            using var www = UnityWebRequest.Get(downloadURL);
                            www.downloadHandler = new DownloadHandlerBuffer();
                            // www.SetRequestHeader("Content-Type", "application/json");
                            // www.SetRequestHeader("Authorization", $"Bearer {SessionKey}");
                            www.useHttpContinue = false;
                            www.timeout = 15;
                            await www.SendWebRequest();
                            if ((www.result != UnityWebRequest.Result.Success &&
                                 www.result != UnityWebRequest.Result.InProgress) || www.error != null)
                                throw new Exception($"network is not reachable !! {www.error}");
                            while (!www.downloadHandler.isDone)
                                await UniTask.Yield();
                            onProgress?.Invoke(++completed / (float)modifiedInfoList.FileInfos.Count);

                            remoteInfoList = JsonUtility.FromJson<ContentsInfoList>(www.downloadHandler.text);
                            if (remoteInfoList == null) return;

                            var downloadData = www.downloadHandler.data;
                            if (info.Size != downloadData.Length ||
                                !info.Hash.Equals(CRC32.Compute(downloadData).ToString()))
                            {
                                modifiedInfoList.FileInfos.Add(info);
                                throw new Exception($"cdn > download size is different !! {downloadData.Length} != {info.Size}");
                            }

                            await File.WriteAllBytesAsync(savePath, www.downloadHandler.data);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }
                await UniTask.WhenAll(tasks); // 모든 다운로드 완료 대기
            }
            
            // 5. change local fileinfo.dat to remote
            await File.WriteAllTextAsync(PersistentInfoFilePath, JsonUtility.ToJson(remoteInfoList));
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

        private bool IsExistsPersistentPath(string path) => File.Exists(Path.Combine(BundleRootPath, path));
        public string GetPathByLabel(string label) => _contentsInfoList?.FileInfos.FirstOrDefault(info => info.Label == label)?.Path;

        public async UniTask LoadCacheByLabels(IEnumerable<string> labels)
        {
            List<UniTask> opList = new();
            foreach (var label in labels)
            {
                var path = GetPathByLabel(label);
                if (_cachedBundles.ContainsKey(path)) continue;
                if (string.IsNullOrEmpty(path)) continue;
                opList.Add(UniTask.Create(async () =>
                {
                    var fullPath = IsExistsPersistentPath(path)
                        ? Path.Combine(BundleRootPath, path)
                        : Path.Combine(Application.streamingAssetsPath, "contents", path);
                    var assetBundle = await UnityEngine.AssetBundle.LoadFromFileAsync(fullPath);
                    if (assetBundle == null) return;
                    _cachedBundles.Add(path, new AssetBundleRef
                    {
                        Bundle = assetBundle
                    });
                }));
            }
            await UniTask.WhenAll(opList);
        }
        
        public async UniTask<AssetBundleRef> LoadCacheByPath(string path)
        {
            if (_cachedBundles.TryGetValue(path, out var abRef)) return abRef;
            var fullPath = IsExistsPersistentPath(path)
                ? Path.Combine(BundleRootPath, path)
                : Path.Combine(Application.streamingAssetsPath, "contents", path);
            var assetBundle = await UnityEngine.AssetBundle.LoadFromFileAsync(fullPath);
            if (assetBundle == null) return null;
            var retValue = new AssetBundleRef
            {
                Bundle = assetBundle
            };
            _cachedBundles.Add(path, retValue);
            return retValue;
        }
        
        public async UniTask<T> LoadAssetByPathAsync<T>(string bundlePath, string assetPath) where T : UnityEngine.Object
        {
    #if UNITY_EDITOR
            var bundleConvertedPath = bundlePath.Replace('~', '/').Substring(0, bundlePath.Length - 2);
            var dirPath = Path.Combine(Application.dataPath, bundleConvertedPath);
            var files = new DirectoryInfo(dirPath).GetFiles();
            var assetFile = files.FirstOrDefault(file => file.Name.Contains(assetPath));
            if (assetFile == null) return null;
            var filePath = Path.Combine("Assets", bundleConvertedPath, assetFile.Name);
            if (_cachedLocalObjects.TryGetValue(filePath, out var localObject))
                return localObject as T;
            var retObject = AssetDatabase.LoadAssetAtPath<T>(filePath);
            _cachedLocalObjects.Add(filePath, retObject);
            return retObject;
    #else
            if (!_cachedBundles.TryGetValue(bundlePath, out AssetBundleRef bundleRef))
                bundleRef = await LoadCacheByPath(bundlePath);
            if (bundleRef == null) return null;
            if (bundleRef.CachedDict.TryGetValue(assetPath, out var asset))
                return asset as T;
            var result = await bundleRef.Bundle.LoadAssetAsync(assetPath, typeof(T));
            bundleRef.CachedDict.Add(assetPath, result);
            return result as T;
    #endif
        }
        
        public async UniTask<GameObject> InstantiateGameObjectByPathAsync(string bundlePath, string assetPath, Transform parent = null, bool instantiateWorldSpace = false)
        {
            var retValue = await LoadAssetByPathAsync<GameObject>(bundlePath, assetPath);
            if (retValue == null) return null;
            return UnityEngine.Object.Instantiate(retValue, parent, instantiateWorldSpace);
        }

        public async UniTask UnloadAll(bool bUnloadBundle = false)
        {
    #if UNITY_EDITOR
            _cachedLocalObjects.Clear();
    #endif
            foreach (var pair in _cachedBundles)
                pair.Value.CachedDict.Clear();
            
            if (!bUnloadBundle) return;

            foreach (var pair in _cachedBundles)
                await pair.Value.Bundle.UnloadAsync(true);
            _cachedBundles.Clear();
        }
    }
}