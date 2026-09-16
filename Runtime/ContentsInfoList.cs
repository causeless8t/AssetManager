using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Causeless3t.AssetBundle
{
    [Serializable]
    public sealed class ContentsInfo
    {
        public string Label;
        public string Path;
        public string Hash;
        public long Size;
        public List<string> Dependencies = new();

        public bool HasSameContent(ContentsInfo other)
        {
            return other != null &&
                   string.Equals(Path, other.Path, StringComparison.Ordinal) &&
                   Size == other.Size &&
                   string.Equals(Hash, other.Hash, StringComparison.Ordinal);
        }
    }

    [Serializable]
    public sealed class ContentsInfoList
    {
        public List<ContentsInfo> FileInfos = new();
        public int Platform;
        public string AppVersion;
        public int Revision;
        public int FileCount;

        [NonSerialized]
        private List<ContentsInfo> _removableFiles = new();

        public string ToJSONString()
        {
            return JsonUtility.ToJson(this);
        }

        internal void AddRemovableFile(ContentsInfo fileInfo)
        {
            if (fileInfo == null)
                return;

            _removableFiles ??= new List<ContentsInfo>();
            _removableFiles.Add(fileInfo);
        }

        internal IReadOnlyList<string> GetRemovableFiles()
        {
            if (_removableFiles == null || _removableFiles.Count == 0)
                return Array.Empty<string>();

            return _removableFiles.ConvertAll(info => info.Path);
        }

        public static ContentsInfoList GetContentsInfoListFromFiles(
            string contentsRootDirectoryPath,
            Dictionary<string, string> bundleLabels,
            string filteredExtension,
            IReadOnlyDictionary<string, IReadOnlyList<string>> bundleDependencies = null)
        {
            if (string.IsNullOrWhiteSpace(contentsRootDirectoryPath))
            {
                throw new ArgumentException(
                    "Contents root directory path cannot be empty.",
                    nameof(contentsRootDirectoryPath));
            }

            if (bundleLabels == null)
                throw new ArgumentNullException(nameof(bundleLabels));

            if (string.IsNullOrWhiteSpace(filteredExtension))
            {
                throw new ArgumentException(
                    "Filtered extension cannot be empty.",
                    nameof(filteredExtension));
            }

            var result = new ContentsInfoList();
            var directory = new DirectoryInfo(contentsRootDirectoryPath);
            var extension = filteredExtension.TrimStart('.');
            var files = directory.GetFiles($"*.{extension}");

            foreach (var file in files)
            {
                var key = System.IO.Path
                    .GetFileNameWithoutExtension(file.Name)
                    .Replace('~', '/');
                var label = string.Empty;

                foreach (var pair in bundleLabels)
                {
                    if (!pair.Key.EndsWith(key, StringComparison.Ordinal))
                        continue;

                    label = pair.Value;
                    break;
                }

                var dependencies = new List<string>();
                if (bundleDependencies != null &&
                    bundleDependencies.TryGetValue(file.Name, out var dependencyPaths))
                {
                    dependencies.AddRange(dependencyPaths);
                }

                result.FileInfos.Add(new ContentsInfo
                {
                    Label = label,
                    Path = file.Name,
                    Hash = AssetBundleUtil.GetFileHash(file.FullName),
                    Size = file.Length,
                    Dependencies = dependencies
                });
            }

            result.FileCount = result.FileInfos.Count;
            return result;
        }
    }
}
