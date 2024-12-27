using System;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

namespace Causeless3t.AssetBundle
{
	[Serializable]
	public class ContentsInfo : IComparable<ContentsInfo>
	{
		public string Label;
		public string Path;
		public string Hash;
		public long Size;

		public int CompareTo(ContentsInfo other)
		{
			if (other != null && Path.Equals(other.Path))
			{
				if (Size != other.Size)
					return 1;
				return Hash.Equals(other.Hash) ? 0 : 1;
			}
			return -1;
		}
	}
	
	[Serializable]
	public class ContentsInfoList
	{
		public List<ContentsInfo> FileInfos = new();
		public int Platform;
		public string AppVersion;
		public int Revision;
		public int FileCount;
		private List<ContentsInfo> _removableFiles = new();

		public ContentsInfoList() { }
		
		public string ToJSONString() => JsonUtility.ToJson(this);
		
		public void AddRemovableFile(ContentsInfo fileInfo) => _removableFiles.Add(fileInfo);
		public List<string> GetRemovableFiles() => _removableFiles.ConvertAll(x => x.Path);
		
		public static ContentsInfoList GetContentsInfoListFromFiles(
			string contentsRootDirPath,
			Dictionary<string, string> bundleLabelDic,
			string filteredExtension)
		{
			ContentsInfoList retVal = new ContentsInfoList();
			DirectoryInfo dirInfo = new DirectoryInfo(contentsRootDirPath);
			FileInfo [] files = dirInfo.GetFiles($"*.{filteredExtension}");
        
			foreach (FileInfo file in files)
			{
				string fileName = file.Name;
				long fileSize = file.Length;
				string crc = AssetBundleUtil.GetFileHash(file.FullName);
				var key = Path.GetFileNameWithoutExtension(fileName).Replace('~', '/');
				string label = string.Empty;
				foreach (KeyValuePair<string, string> pair in bundleLabelDic)
				{
					if (pair.Key.EndsWith(key))
					{
						label = pair.Value;
						break;
					}
				}
				
				retVal.FileInfos.Add(new ContentsInfo()
				{
					Label = label,
					Path = fileName,
					Hash = crc,
					Size = fileSize
				});
			}
			return retVal;
		}
	}
}
