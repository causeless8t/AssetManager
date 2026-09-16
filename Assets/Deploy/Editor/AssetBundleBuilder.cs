#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Causeless3t.Utilities;
using UnityEditor;
using UnityEngine;

namespace Causeless3t.AssetBundle.Editor
{
    internal sealed class AssetBundleBuilder
    {
        private const string BuildSnapshotFileName = "AssetFileInfo.txt";

        private readonly AssetBundleBuildSettings _settings;
        private Dictionary<string, List<ContentsInfo>> _buildSnapshots;

        internal AssetBundleBuilder(AssetBundleBuildSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        internal string Build(BuildTarget platform, Action<float> onProgress = null)
        {
            Validate();
            _settings.EnsureCollections();
            _buildSnapshots = LoadBuildSnapshots();

            var bundleRoot = _settings.GetBundleRoot(platform);
            PrepareOutputDirectory(bundleRoot, platform);

            for (var i = 0; i < _settings.AssetPathes.Count; i++)
            {
                var targetPath = NormalizeTargetPath(_settings.AssetPathes[i]);
                if (!string.IsNullOrEmpty(targetPath))
                    BuildTargetFolder(targetPath, bundleRoot, platform);

                onProgress?.Invoke((i + 1f) / Math.Max(1, _settings.AssetPathes.Count));
            }

            WriteBuildSnapshots();
            WriteContentsManifest(bundleRoot, platform);
            CleanBuildOutput(bundleRoot);
            AssetDatabase.Refresh();
            return bundleRoot;
        }

        private void Validate()
        {
            if (string.IsNullOrWhiteSpace(_settings.OutputPath))
                throw new InvalidOperationException("Bundle output path is not configured.");

            if (_settings.AssetPathes == null || _settings.AssetPathes.Count == 0)
                throw new InvalidOperationException("At least one AssetBundle target folder is required.");
        }

        private void PrepareOutputDirectory(string bundleRoot, BuildTarget platform)
        {
            if (Directory.Exists(bundleRoot))
            {
                if (_settings.IsIntegralBuild(platform))
                {
                    var previousRoot = GetPreviousIntegralRoot(platform);
                    if (Directory.Exists(previousRoot))
                        Directory.Delete(previousRoot, true);

                    Directory.Move(bundleRoot, previousRoot);
                }
                else
                {
                    Directory.Delete(bundleRoot, true);
                }
            }

            Directory.CreateDirectory(bundleRoot);
        }

        private void BuildTargetFolder(string targetPath, string bundleRoot, BuildTarget platform)
        {
            var assetRoot = $"Assets/{targetPath}";
            var assetPaths = FindAssetPaths(assetRoot);
            if (assetPaths.Count == 0)
                return;

            var currentSnapshot = CreateSnapshot(assetPaths);
            var requiresBuild = HasSnapshotChanged(targetPath, currentSnapshot);
            _buildSnapshots[targetPath] = currentSnapshot;

            var bundleFileName = GetBundleFileName(targetPath);
            if (!requiresBuild && CopyPreviousBundle(bundleFileName, bundleRoot, platform))
                return;

            var build = new AssetBundleBuild
            {
                assetBundleName = Path.GetFileNameWithoutExtension(bundleFileName),
                assetBundleVariant = AssetBundleUtil.ASSET_BUNDLE_EXTENSION_NAME,
                assetNames = assetPaths.ToArray()
            };

            var manifest = BuildPipeline.BuildAssetBundles(
                bundleRoot,
                new[] { build },
                BuildAssetBundleOptions.DisableWriteTypeTree |
                BuildAssetBundleOptions.UncompressedAssetBundle |
                BuildAssetBundleOptions.ForceRebuildAssetBundle,
                platform);

            if (manifest == null)
                throw new InvalidOperationException($"AssetBundle build failed: {targetPath}");

            NormalizeBuiltBundleName(bundleRoot, bundleFileName);
            Debug.Log($"Built {assetPaths.Count} assets into {Path.Combine(bundleRoot, bundleFileName)}");
        }

        private static List<string> FindAssetPaths(string assetRoot)
        {
            if (!AssetDatabase.IsValidFolder(assetRoot))
                return new List<string>();

            return AssetDatabase.FindAssets(string.Empty, new[] { assetRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path =>
                    !AssetDatabase.IsValidFolder(path) &&
                    File.Exists(path) &&
                    AssetDatabase.LoadMainAssetAtPath(path) != null)
                .Distinct()
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
        }

        private static List<ContentsInfo> CreateSnapshot(IEnumerable<string> assetPaths)
        {
            return assetPaths.Select(path =>
            {
                var file = new FileInfo(path);
                return new ContentsInfo
                {
                    Path = path,
                    Hash = AssetDatabase.GetAssetDependencyHash(path).ToString(),
                    Size = file.Length
                };
            }).ToList();
        }

        private bool HasSnapshotChanged(string targetPath, IReadOnlyCollection<ContentsInfo> currentSnapshot)
        {
            if (!_buildSnapshots.TryGetValue(targetPath, out var previousSnapshot))
                return true;

            if (previousSnapshot.Count != currentSnapshot.Count)
                return true;

            var previousByPath = previousSnapshot.ToDictionary(info => info.Path, StringComparer.Ordinal);
            return currentSnapshot.Any(current =>
                !previousByPath.TryGetValue(current.Path, out var previous) ||
                !current.HasSameContent(previous));
        }

        private bool CopyPreviousBundle(string bundleFileName, string bundleRoot, BuildTarget platform)
        {
            var previousRoot = _settings.IsIntegralBuild(platform)
                ? GetPreviousIntegralRoot(platform)
                : GetPreviousRevisionRoot(platform);

            var sourcePath = Path.Combine(previousRoot, bundleFileName);
            if (!File.Exists(sourcePath))
                return false;

            File.Copy(sourcePath, Path.Combine(bundleRoot, bundleFileName), true);
            return true;
        }

        private string GetPreviousIntegralRoot(BuildTarget platform)
        {
            return Path.Combine(_settings.OutputPath, _settings.GetPlatformDirectory(platform), "integral_prev");
        }

        private string GetPreviousRevisionRoot(BuildTarget platform)
        {
            return Path.Combine(
                _settings.OutputPath,
                _settings.GetPlatformDirectory(platform),
                _settings.GetAppVersion(platform),
                Math.Max(0, _settings.GetRevision(platform) - 1).ToString());
        }

        private void WriteContentsManifest(string bundleRoot, BuildTarget platform)
        {
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < _settings.AssetPathes.Count; i++)
            {
                var path = NormalizeTargetPath(_settings.AssetPathes[i]);
                if (!string.IsNullOrEmpty(path))
                    labels[path] = _settings.AssetLabels[i] ?? string.Empty;
            }

            var manifest = ContentsInfoList.GetContentsInfoListFromFiles(
                bundleRoot,
                labels,
                AssetBundleUtil.ASSET_BUNDLE_EXTENSION_NAME);
            manifest.Platform = platform == BuildTarget.Android ? 1 : 2;
            manifest.AppVersion = _settings.GetAppVersion(platform);
            manifest.Revision = _settings.GetRevision(platform);
            manifest.FileCount = manifest.FileInfos.Count;

            File.WriteAllText(
                Path.Combine(bundleRoot, AssetBundleUtil.INFO_FILE_NAME),
                manifest.ToJSONString());
        }

        private Dictionary<string, List<ContentsInfo>> LoadBuildSnapshots()
        {
            var path = GetBuildSnapshotPath();
            if (!File.Exists(path))
                return new Dictionary<string, List<ContentsInfo>>();

            return DictionaryJson.FromJson<string, List<ContentsInfo>>(File.ReadAllText(path));
        }

        private void WriteBuildSnapshots()
        {
            File.WriteAllText(
                GetBuildSnapshotPath(),
                DictionaryJson.ToJson(_buildSnapshots, true));
        }

        private string GetBuildSnapshotPath()
        {
            return Path.Combine(_settings.OutputPath, BuildSnapshotFileName);
        }

        private static void CleanBuildOutput(string bundleRoot)
        {
            var directory = new DirectoryInfo(bundleRoot);
            foreach (var file in directory.GetFiles())
            {
                if (string.Equals(file.Extension, ".manifest", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(file.Name, directory.Name, StringComparison.Ordinal))
                {
                    file.Delete();
                }
            }
        }

        private static void NormalizeBuiltBundleName(string bundleRoot, string expectedFileName)
        {
            var expectedPath = Path.Combine(bundleRoot, expectedFileName);
            if (File.Exists(expectedPath))
                return;

            var lowerCasePath = Path.Combine(bundleRoot, expectedFileName.ToLowerInvariant());
            if (File.Exists(lowerCasePath))
                File.Move(lowerCasePath, expectedPath);

            if (!File.Exists(expectedPath))
                throw new FileNotFoundException("Built AssetBundle was not found.", expectedPath);
        }

        private static string NormalizeTargetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/').Trim('/');
        }

        private static string GetBundleFileName(string targetPath)
        {
            return $"{targetPath.Replace('/', '~')}.{AssetBundleUtil.ASSET_BUNDLE_EXTENSION_NAME}";
        }
    }
}
#endif
