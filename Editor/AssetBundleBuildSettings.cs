#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Causeless3t.AssetBundle.Editor
{
    [Serializable]
    internal sealed class AssetBundleBuildSettings
    {
        private const string SettingsFileName = "AssetManagerBuildSettings.json";
        private const string LegacySettingsFileName = "BuildAssetBundles.dat";

        public List<string> AssetPathes = new();
        public List<string> AssetLabels = new();
        public string iOSVersion = string.Empty;
        public string AndroidVersion = string.Empty;
        public int iOSRevision;
        public int AndroidRevision;
        public string OutputPath = string.Empty;

        private static string SettingsPath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ProjectSettings", SettingsFileName);

        private static string LegacySettingsPath =>
            Path.Combine(Application.dataPath, LegacySettingsFileName);

        internal static AssetBundleBuildSettings Load()
        {
            var path = File.Exists(SettingsPath) ? SettingsPath : LegacySettingsPath;
            if (!File.Exists(path))
                return new AssetBundleBuildSettings();

            var settings = JsonUtility.FromJson<AssetBundleBuildSettings>(File.ReadAllText(path))
                           ?? new AssetBundleBuildSettings();
            settings.EnsureCollections();
            return settings;
        }

        internal void Save()
        {
            EnsureCollections();
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(SettingsPath, JsonUtility.ToJson(this, true));
        }

        internal void EnsureCollections()
        {
            AssetPathes ??= new List<string>();
            AssetLabels ??= new List<string>();

            while (AssetLabels.Count < AssetPathes.Count)
                AssetLabels.Add(string.Empty);

            if (AssetLabels.Count > AssetPathes.Count)
                AssetLabels.RemoveRange(AssetPathes.Count, AssetLabels.Count - AssetPathes.Count);
        }

        internal int GetRevision(BuildTarget platform)
        {
            return platform switch
            {
                BuildTarget.Android => AndroidRevision,
                BuildTarget.iOS => iOSRevision,
                _ => throw new NotSupportedException($"Unsupported build target: {platform}")
            };
        }

        internal string GetAppVersion(BuildTarget platform)
        {
            return platform switch
            {
                BuildTarget.Android => AndroidVersion,
                BuildTarget.iOS => iOSVersion,
                _ => throw new NotSupportedException($"Unsupported build target: {platform}")
            };
        }

        internal string GetPlatformDirectory(BuildTarget platform)
        {
            return platform switch
            {
                BuildTarget.Android => "android",
                BuildTarget.iOS => "ios",
                _ => throw new NotSupportedException($"Unsupported build target: {platform}")
            };
        }

        internal bool IsIntegralBuild(BuildTarget platform)
        {
            return GetRevision(platform) < 1;
        }

        internal string GetBundleRoot(BuildTarget platform)
        {
            var platformDirectory = GetPlatformDirectory(platform);
            return IsIntegralBuild(platform)
                ? Path.Combine(OutputPath, platformDirectory, "integral")
                : Path.Combine(OutputPath, platformDirectory, GetAppVersion(platform), GetRevision(platform).ToString());
        }
    }
}
#endif
