using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Causeless3t.AssetManager.Sample
{
    public sealed class AssetManagerExample : MonoBehaviour
    {
        [Header("Patch")]
        [SerializeField] private string remoteUrl;

        [Header("Asset")]
        [SerializeField] private string bundleLabel;
        [SerializeField] private string assetPath;
        [SerializeField] private Transform instanceParent;

        private ResourceManager _resourceManager;
        private CancellationTokenSource _lifetimeCancellation;
        private GameObject _instance;

        private async void Start()
        {
            _resourceManager = new ResourceManager();
            _lifetimeCancellation = new CancellationTokenSource();

            try
            {
                await InitializeAndLoadAsync(_lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("AssetManager 작업이 취소되었습니다.");
            }
            catch (InvalidDataException exception)
            {
                Debug.LogError($"AssetBundle 무결성 검사에 실패했습니다: {exception.Message}");
            }
            catch (IOException exception)
            {
                Debug.LogError($"AssetBundle 네트워크 또는 파일 작업에 실패했습니다: {exception.Message}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private async Task InitializeAndLoadAsync(CancellationToken cancellationToken)
        {
            await _resourceManager.Initialize();
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(remoteUrl))
            {
                _resourceManager.SetRemoteURL(remoteUrl);
                await _resourceManager.CheckUpdateAsync(
                    progress => Debug.Log($"패치 진행률: {progress:P0}"),
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var bundlePath = _resourceManager.GetPathByLabel(bundleLabel);
            if (string.IsNullOrEmpty(bundlePath))
            {
                throw new InvalidOperationException(
                    $"레이블에 해당하는 AssetBundle이 없습니다: {bundleLabel}");
            }

            _instance = await _resourceManager.InstantiateGameObjectByPathAsync(
                bundlePath,
                assetPath,
                instanceParent);

            if (_instance == null)
            {
                throw new InvalidOperationException(
                    $"프리팹을 로드하지 못했습니다: {assetPath}");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                Destroy(_instance);
                _instance = null;
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private async void OnDestroy()
        {
            _lifetimeCancellation?.Cancel();

            if (_instance != null)
                Destroy(_instance);

            if (_resourceManager != null)
            {
                try
                {
                    await _resourceManager.UnloadAll(true);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            _lifetimeCancellation?.Dispose();
        }
    }
}
