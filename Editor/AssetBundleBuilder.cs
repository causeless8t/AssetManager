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
            _buildSnapshots = new Dictionary<string, List<ContentsInfo>>(
                StringComparer.Ordinal);

            var bundleRoot = _settings.GetBundleRoot(platform);
            PrepareOutputDirectory(bundleRoot, platform);
            var buildEntries = new List<BundleBuildEntry>();

            for (var i = 0; i < _settings.AssetPathes.Count; i++)
            {
                var targetPath = NormalizeTargetPath(_settings.AssetPathes[i]);
                if (!string.IsNullOrEmpty(targetPath))
                {
                    var entry = CreateBuildEntry(targetPath);
                    if (entry != null)
                        buildEntries.Add(entry);
                }

                onProgress?.Invoke((i + 1f) /
                                   Math.Max(1, _settings.AssetPathes.Count + 1));
            }

            if (buildEntries.Count == 0)
                throw new InvalidOperationException("No assets were found in the configured target folders.");

            var unityManifest = BuildBundles(buildEntries, bundleRoot, platform);
            onProgress?.Invoke(1f);

            WriteBuildSnapshots();
            WriteContentsManifest(bundleRoot, platform, unityManifest, buildEntries);
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

        private BundleBuildEntry CreateBuildEntry(string targetPath)
        {
            var assetRoot = $"Assets/{targetPath}";
            var assetPaths = FindAssetPaths(assetRoot);
            if (assetPaths.Count == 0)
                return null;

            var currentSnapshot = CreateSnapshot(assetPaths);
            _buildSnapshots[targetPath] = currentSnapshot;

            var bundleFileName = GetBundleFileName(targetPath);
            return new BundleBuildEntry
            {
                FileName = bundleFileName,
                Build = new AssetBundleBuild
                {
                    assetBundleName = Path.GetFileNameWithoutExtension(bundleFileName),
                    assetBundleVariant = AssetBundleUtil.ASSET_BUNDLE_EXTENSION_NAME,
                    assetNames = assetPaths.ToArray()
                }
            };
        }

        private static AssetBundleManifest BuildBundles(
            IReadOnlyCollection<BundleBuildEntry> entries,
            string bundleRoot,
            BuildTarget platform)
        {
            var manifest = BuildPipeline.BuildAssetBundles(
                bundleRoot,
                entries.Select(entry => entry.Build).ToArray(),
                BuildAssetBundleOptions.DisableWriteTypeTree |
                BuildAssetBundleOptions.UncompressedAssetBundle,
                platform);

            if (manifest == null)
                throw new InvalidOperationException("AssetBundle build failed.");

            foreach (var entry in entries)
                NormalizeBuiltBundleName(bundleRoot, entry.FileName);

            Debug.Log($"Built {entries.Count} AssetBundles into {bundleRoot}");
            return manifest;
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

        private string GetPreviousIntegralRoot(BuildTarget platform)
        {
            return Path.Combine(_settings.OutputPath, _settings.GetPlatformDirectory(platform), "integral_prev");
        }

        private void WriteContentsManifest(
            string bundleRoot,
            BuildTarget platform,
            AssetBundleManifest unityManifest,
            IReadOnlyCollection<BundleBuildEntry> entries)
        {
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < _settings.AssetPathes.Count; i++)
            {
                var path = NormalizeTargetPath(_settings.AssetPathes[i]);
                if (!string.IsNullOrEmpty(path))
                    labels[path] = _settings.AssetLabels[i] ?? string.Empty;
            }

            var unityBundleNames = unityManifest.GetAllAssetBundles();
            var expectedNames = entries.ToDictionary(
                entry => ResolveUnityBundleName(entry.Build, unityBundleNames),
                entry => entry.FileName,
                StringComparer.OrdinalIgnoreCase);
            var dependencies = new Dictionary<string, IReadOnlyList<string>>(
                StringComparer.Ordinal);

            foreach (var entry in entries)
            {
                var unityBundleName = ResolveUnityBundleName(
                    entry.Build,
                    unityBundleNames);
                var dependencyPaths = unityManifest
                    .GetDirectDependencies(unityBundleName)
                    .Select(dependency => expectedNames.TryGetValue(dependency, out var expectedName)
                        ? expectedName
                        : dependency)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                dependencies[entry.FileName] = dependencyPaths;
            }

            var manifest = ContentsInfoList.GetContentsInfoListFromFiles(
                bundleRoot,
                labels,
                AssetBundleUtil.ASSET_BUNDLE_EXTENSION_NAME,
                dependencies);
            manifest.Platform = platform == BuildTarget.Android ? 1 : 2;
            manifest.AppVersion = _settings.GetAppVersion(platform);
            manifest.Revision = _settings.GetRevision(platform);
            manifest.FileCount = manifest.FileInfos.Count;

            File.WriteAllText(
                Path.Combine(bundleRoot, AssetBundleUtil.INFO_FILE_NAME),
                manifest.ToJSONString());
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

        private static string GetUnityBundleName(AssetBundleBuild build)
        {
            return string.IsNullOrEmpty(build.assetBundleVariant)
                ? build.assetBundleName
                : $"{build.assetBundleName}.{build.assetBundleVariant}";
        }

        private static string ResolveUnityBundleName(
            AssetBundleBuild build,
            IEnumerable<string> unityBundleNames)
        {
            var expectedName = GetUnityBundleName(build);
            var actualName = unityBundleNames.FirstOrDefault(name =>
                string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase));

            return actualName ?? throw new InvalidOperationException(
                $"Built AssetBundle is missing from the Unity manifest: {expectedName}");
        }

        private sealed class BundleBuildEntry
        {
            internal string FileName;
            internal AssetBundleBuild Build;
        }
    }
}
#endif
