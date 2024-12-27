#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;
using System.IO;
using Causeless3t.Utilities;

namespace Causeless3t.AssetBundle.Editor
{
	public class BuildAssetBundles : EditorWindow
	{
		[Serializable]
		private class BundleBuildInfo
		{
			public List<string> AssetPathes = new();
			public List<string> AssetLabels = new();
			public string iOSVersion = "";
			public string AndroidVersion = "";
			public int iOSRevision;
			public int AndroidRevision;
			public string OutputPath;
		}

		private static readonly string BuildInfoPath = Application.dataPath + "/BuildAssetBundles.dat";

		private BundleBuildInfo _bundleBuildInfo = new();
		private Vector2 _scrollPos = new Vector2();

		[MenuItem("Tools/Build Asset Bundle #&a")]
		private static void ShowWindow()
		{
			EditorWindow window = GetWindow(typeof(BuildAssetBundles), false, "Build AssetBundles");
			window.minSize = new Vector2(600, 600);
		}
		
		private void OnEnable()
		{
			LoadSettings();
		}
		
		private void LoadSettings()
		{
			if (!File.Exists(BuildInfoPath))
				return;
			
			var buildInfoText = File.ReadAllText(BuildInfoPath);
			_bundleBuildInfo = JsonUtility.FromJson<BundleBuildInfo>(buildInfoText);
		}

		private void SaveSettings()
		{
			File.WriteAllText(BuildInfoPath, JsonUtility.ToJson(_bundleBuildInfo));
		}
		
		private void OnGUI()
		{
			ViewTargetPathList();
			ViewMakeInfo();
			ViewBuild();
		}

		#region Build Info
		
		private void ViewTargetPathList()
		{
			_scrollPos = GUILayout.BeginScrollView(_scrollPos, false, false, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(false));
			GUILayout.BeginVertical("box", GUILayout.ExpandHeight(true));
			{
				GUILayout.Space(5);
				GUILayout.BeginHorizontal();
				GUILayout.Label("AssetBundle Target List");
				if (GUILayout.Button("Clear List", GUILayout.ExpandWidth(false)))
				{
					_bundleBuildInfo.AssetPathes.Clear();
					_bundleBuildInfo.AssetLabels.Clear();
					GUI.FocusControl("");
				}
				GUILayout.EndHorizontal();
				GUILayout.Space(5);
				for (int i = 0; i < _bundleBuildInfo.AssetPathes.Count; i++)
				{
					GUILayout.BeginHorizontal();
					_bundleBuildInfo.AssetPathes[i] = EditorGUILayout.TextField(_bundleBuildInfo.AssetPathes[i]);
					_bundleBuildInfo.AssetLabels[i] = EditorGUILayout.TextField(_bundleBuildInfo.AssetLabels[i]);
					if (GUILayout.Button("Sel", GUILayout.ExpandWidth(false)))
					{
						SetTargetFolderPath(i);
						GUI.FocusControl("");
					}
					if (GUILayout.Button("-", GUILayout.ExpandWidth(false)))
					{
						RemoveTargetPath(i);
						GUI.FocusControl("");
					}
					GUILayout.EndHorizontal();
					if (_bundleBuildInfo.AssetPathes.Count > i && string.IsNullOrEmpty(_bundleBuildInfo.AssetPathes[i]))
					{
						_bundleBuildInfo.AssetPathes.RemoveAt(i);
						_bundleBuildInfo.AssetLabels.RemoveAt(i--);
					}
				}
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Add", GUILayout.ExpandWidth(false)))
				{
					SetTargetFolderPath(_bundleBuildInfo.AssetPathes.Count);
					GUI.FocusControl("");
				}
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
			GUILayout.EndScrollView();
		}

		private void ViewMakeInfo()
		{
			GUILayout.BeginVertical("box", GUILayout.ExpandHeight(false));
			GUILayout.BeginHorizontal();
			{
				GUILayout.BeginHorizontal();
				GUILayout.BeginVertical("box", GUILayout.ExpandHeight(false), GUILayout.ExpandWidth(false), GUILayout.Width(300));
				{
					GUILayout.Label(" Android ");
					GUILayout.Label(" - Revision - ");
					_bundleBuildInfo.AndroidRevision = EditorGUILayout.IntField(_bundleBuildInfo.AndroidRevision);

					GUILayout.Label(" - App Version - ");
					_bundleBuildInfo.AndroidVersion = EditorGUILayout.TextField(_bundleBuildInfo.AndroidVersion);
				}
				GUILayout.EndVertical();
				GUILayout.BeginVertical("box", GUILayout.ExpandHeight(false), GUILayout.ExpandWidth(false), GUILayout.Width(300));
				{
					GUILayout.Label(" iOS ");
					GUILayout.Label(" - Revision - ");
					_bundleBuildInfo.iOSRevision = EditorGUILayout.IntField(_bundleBuildInfo.iOSRevision);

					GUILayout.Label(" - App Version - ");
					_bundleBuildInfo.iOSVersion = EditorGUILayout.TextField(_bundleBuildInfo.iOSVersion);
				}
				GUILayout.EndVertical();
				GUILayout.EndHorizontal();
			}
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}

		private void RemoveTargetPath(int idx)
		{
			if (_bundleBuildInfo.AssetPathes.Count > idx)
			{
				_bundleBuildInfo.AssetPathes.RemoveAt(idx);
				_bundleBuildInfo.AssetLabels.RemoveAt(idx);
			}
		}

		private void SetTargetFolderPath(int idx)
		{
			string targetPath = Application.dataPath + "/";
			if (_bundleBuildInfo.AssetPathes.Count > idx)
			{
				targetPath = Path.Combine(Application.dataPath, _bundleBuildInfo.AssetPathes[idx]);
			}
			string path = EditorUtility.OpenFolderPanel("Select Build Target", targetPath, "");
			if (path.Length > 0)
				SetTargetPath(idx, path + "/");
		}

		private void SetTargetPath(int idx, string path)
		{
			path = path.Replace ('\\', '/');
			path = path.Replace(Application.dataPath + "/", "");

			// 중복제거.
			for (int i = 0; i < _bundleBuildInfo.AssetPathes.Count; i++)
			{
				if (_bundleBuildInfo.AssetPathes[i].Equals(path))
					return;
			}

			if (path.Length != 0)
			{
				if (_bundleBuildInfo.AssetPathes.Count > idx)
				{
					_bundleBuildInfo.AssetPathes[idx] = path;
					_bundleBuildInfo.AssetLabels[idx] = string.Empty;
				}
				else
				{
					_bundleBuildInfo.AssetPathes.Add(path);
					_bundleBuildInfo.AssetLabels.Add(string.Empty);
				}
			}
		}
		
		#endregion Build Info

		#region Build Bundles
		
		private void ViewBuild()
		{
			GUILayout.BeginVertical("box", GUILayout.ExpandHeight(false));
			GUILayout.Label(" Bundle Output Path ");
			GUILayout.BeginHorizontal();
			_bundleBuildInfo.OutputPath = EditorGUILayout.TextField(_bundleBuildInfo.OutputPath);
			if (GUILayout.Button("Select", GUILayout.ExpandWidth(false)))
			{
				string targetPath = Application.dataPath + "/";
				_bundleBuildInfo.OutputPath = EditorUtility.OpenFolderPanel("Select Output Path", targetPath, "");
				GUI.FocusControl("");
			}
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
			
			GUILayout.BeginHorizontal(GUILayout.Height(30));
		
			if (GUILayout.Button("Android Build"))
			{
				// RemoveUITexture();
				SaveSettings();
				BuildAssetBundle(BuildTarget.Android);
			}

			if (GUILayout.Button("iOS Build"))
			{
				// RemoveUITexture();
				SaveSettings();
				BuildAssetBundle(BuildTarget.iOS);
			}

			if (GUILayout.Button("Save Bundle Setting"))
			{
				SaveSettings();
				EditorUtility.DisplayDialog("Success!","Setting info save successfully completed!", "ok");
			}
			GUILayout.EndHorizontal();
		}

		private string GetIntegralDir(BuildTarget platform)
		{
			return $"{_bundleBuildInfo.OutputPath}/{GetPlatformDir(platform)}/integral";
		}

		private string GetVersionDir(BuildTarget platform)
		{
			return $"{_bundleBuildInfo.OutputPath}/{GetPlatformDir(platform)}/{GetAppVersion(platform)}/{GetRevision(platform)}";
		}

		private string GetAppVersion(BuildTarget platform)
		{
			switch (platform)
			{
				case BuildTarget.Android:
					return _bundleBuildInfo.AndroidVersion;
				case BuildTarget.iOS:
					return _bundleBuildInfo.iOSVersion;
				default:
					throw new Exception();
			}
		}

		private string GetPlatformDir(BuildTarget platform)
		{
			switch (platform)
			{
				case BuildTarget.Android:
					return "android";
				case BuildTarget.iOS:
					return "ios";
			}
			return "";
		}
		
		public void BuildAssetBundle(BuildTarget platform)
		{
			if (string.IsNullOrEmpty(_bundleBuildInfo.OutputPath))
			{
				Debug.LogError("You didn't select output path!");
				return;
			}
			
			string bundleRoot = string.Empty;
			// integral bundle
			if (IsIntegralBuild(platform))
				bundleRoot = GetIntegralDir(platform);
			else
				bundleRoot = GetVersionDir(platform);

			if (Directory.Exists(bundleRoot))
			{
				if (IsIntegralBuild(platform))
				{
					// Integral 빌드에서 동일한 이름의 예전 폴더를 백업으로 사용한다
					string bundleRoot_prev = bundleRoot.Replace("integral", "integral_prev");
					if (Directory.Exists(bundleRoot_prev))
						Directory.Delete(bundleRoot_prev, true);
					Directory.Move(bundleRoot, bundleRoot_prev);
				}
				else
				{
					Directory.Delete(bundleRoot, true);
				}
			}
			Directory.CreateDirectory(bundleRoot);

			for (int i = 0; i < _bundleBuildInfo.AssetPathes.Count; ++i)
			{
				if (_bundleBuildInfo.AssetPathes[i].Length > 0)
					BuildAssetBundleFromFilesInFolder(_bundleBuildInfo.AssetPathes[i], bundleRoot, platform);
				EditorUtility.DisplayProgressBar("Build Asset Progress", "Please wait for build asset bundles...", i / (float)_bundleBuildInfo.AssetPathes.Count);
			}
			WriteToBundleFileInfo();

			//make filesinfo.bytes
			MakeFilesInfo(platform);
			EditorUtility.ClearProgressBar();

			// Clear Manifest and file named bundleRoot folder
			DirectoryInfo di = new DirectoryInfo(bundleRoot);

			if (!di.Exists) return;

			FileInfo[] fiList = di.GetFiles();
			for (int i = 0; i < fiList.Length; i++)
			{
				if ( fiList[i].Extension.Equals(".manifest") || fiList[i].Name.Equals(di.Name) )
				{
					File.Delete(fiList[i].FullName);
				}
			}
			
			EditorUtility.DisplayDialog("Success!","Successfully completed.\n\n", "ok");
		}

		private void MakeFilesInfo(BuildTarget platform)
		{
			// 원격지에 올라가는 번들파일정보
			string bundleRoot = string.Empty;
			// ios integral bundle
			if (IsIntegralBuild(platform))
				bundleRoot = GetIntegralDir(platform);
			else
				bundleRoot = GetVersionDir(platform);

			Dictionary<string, string> bundleLabelPathDic = new();
			for (int i = 0; i < _bundleBuildInfo.AssetPathes.Count; ++i)
				bundleLabelPathDic.Add(_bundleBuildInfo.AssetPathes[i], _bundleBuildInfo.AssetLabels[i]);
			
			// BundleInfo생성.
			ContentsInfoList infoList = ContentsInfoList.GetContentsInfoListFromFiles(bundleRoot, bundleLabelPathDic, AssetBundleUtil.ASSET_BUNDLE_EXTENSION_NAME);
			infoList.Platform = platform == BuildTarget.Android ? 1 : 2;
			infoList.AppVersion = GetAppVersion(platform);
			infoList.Revision = GetRevision(platform);
			infoList.FileCount = infoList.FileInfos.Count;

			string jsonStr = infoList.ToJSONString();
			// string encryptedStr = packetSec.Encrypt( jsonStr, TWNetwork.aes_key );
			// byte[] encryptedByte = System.Text.Encoding.UTF8.GetBytes(encryptedStr);

			File.WriteAllText(Path.Combine(bundleRoot, AssetBundleUtil.INFO_FILE_NAME), jsonStr);
		}

		private int GetRevision(BuildTarget platform) => platform switch
		{
			BuildTarget.Android => _bundleBuildInfo.AndroidRevision,
			BuildTarget.iOS => _bundleBuildInfo.iOSRevision,
			_ => 0
		};

		private void BuildAssetBundleFromFilesInFolder(string targetPath, string bundleRoot, BuildTarget platform)
		{
			Debug.Log ( " Build Asset -> targetPath : " + targetPath );
			string targetFullPath = "Assets/" + targetPath;
		
			string exportPath;
			string targetPath2;
			{ // 번들파일 이름 결정
				targetPath2 = targetPath.Replace('/', '~');
				targetPath2 += $".{AssetBundleUtil.ASSET_BUNDLE_EXTENSION_NAME}";
				exportPath = Path.Combine(bundleRoot, targetPath2);
			}
		
			// build 에 포함시킬 파일 리스트 업.
			List<UnityEngine.Object> objectList = new List<UnityEngine.Object>();
			SearchDir(targetFullPath, objectList);
			if (objectList.Count == 0) return;
			
			#region AssetBundle Compare & Build System
			// 번들파일 전체를 비교하여 필요한 파일만 빌드한다
			if (CompareAndBuildRoutine(targetFullPath, objectList) == false)
			{
				string alterExportPath = string.Empty;
				string prevAlterExportPath = string.Empty;
				if (IsIntegralBuild(platform))
				{
					alterExportPath = exportPath;
					prevAlterExportPath = exportPath.Replace("/integral/", "/integral_prev/");
				}
				else
				{
					int revision = GetRevision(platform);
					alterExportPath = exportPath;
					prevAlterExportPath = exportPath.Replace($"/{revision}/", $"/{revision-1}/");
				}

				if (File.Exists(prevAlterExportPath))
				{
					// copy before built file to new build path because it's don't need build file
					File.Copy(prevAlterExportPath, alterExportPath);
					return;
				}
			}
			#endregion AssetBundle Compare & Build System

			UnityEngine.Object[] selection = objectList.ToArray();
			string[] assetPath = new string[selection.Length];
			var count = 0;
			for (int i = 0; i < selection.Length; ++i)
			{
				Type assetType = selection[i].GetType();
				if (assetType == typeof(UnityEngine.Object)) //not a recognized type
				{
					selection[i] = null;
					continue;
				}
				assetPath[i] = AssetDatabase.GetAssetPath(selection[i]);
				count++;
			}
		
			//use asset-path as asset-name, so you can load asset by asset-path
			// make build map
			AssetBundleBuild [] buildMap = new AssetBundleBuild[1];
			buildMap[0].assetBundleVariant = AssetBundleUtil.ASSET_BUNDLE_EXTENSION_NAME;
			buildMap[0].assetBundleName = targetPath2.Replace($".{AssetBundleUtil.ASSET_BUNDLE_EXTENSION_NAME}","");
			buildMap[0].assetNames = assetPath;
			
			BuildPipeline.BuildAssetBundles(bundleRoot,
				buildMap,
				BuildAssetBundleOptions.DisableWriteTypeTree |
				BuildAssetBundleOptions.UncompressedAssetBundle |
				BuildAssetBundleOptions.ForceRebuildAssetBundle, platform);
			
			// rename lower -> upper
			if (File.Exists(Path.Combine(bundleRoot, targetPath2.ToLower())))
				File.Move(Path.Combine(bundleRoot, targetPath2.ToLower()), Path.Combine(bundleRoot, targetPath2));
		
			//select all packed assets
			Debug.Log("======= Total " + count + " Assets exported~! in " + exportPath + " =======");
		
			AssetDatabase.Refresh();
		}

		private void SearchFile(string filePath, List<UnityEngine.Object> objectList)
		{
			if (filePath.Length == 0)
				return;
			if (!filePath.EndsWith(".meta") && 
			    !filePath.EndsWith(".DS_Store") && 
			    !filePath.EndsWith(".localized") && 
			    !filePath.EndsWith(".db"))
			{
				UnityEngine.Object objectA = AssetDatabase.LoadAssetAtPath(filePath, typeof(UnityEngine.Object));
				objectList.Add(objectA);
			}
		}

		private void SearchDir(string parentPath, List<UnityEngine.Object> objectList)
		{
			if (parentPath.Length == 0)
				return;

			foreach (string filePath in Directory.GetFiles(parentPath))
				SearchFile(filePath, objectList);
			foreach (string dir in Directory.GetDirectories(parentPath))
				SearchDir(dir, objectList);
		}
		
		private bool IsIntegralBuild( BuildTarget platform )
		{
			if (platform == BuildTarget.iOS)
				return _bundleBuildInfo.iOSRevision < 1;
			return _bundleBuildInfo.AndroidRevision < 1;
		}

		private void RemoveUITexture()
		{
			string[] folders = {
				"Assets/Resources/Materials/UI"
			};
			string[] guids = AssetDatabase.FindAssets("t:Material", folders);
		
			foreach (string guid in guids) {
				Debug.Log (AssetDatabase.GUIDToAssetPath(guid));
				Material mat = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), typeof(Material)) as Material;
				mat.mainTexture = null;
				Debug.Log ("Remove Texture");
			}
		}

		#region AssetBundle Compare & Build System

		private Dictionary<string, List<ContentsInfo>> _bundleFileInfo;

		private string GetBundleFileInfoPath()
		{
			return Path.Combine(_bundleBuildInfo.OutputPath, "AssetFileInfo.txt");
		}
		
		private bool CompareAndBuildRoutine(string bundleTargetPath, List<UnityEngine.Object> objectList)
		{
			if (_bundleFileInfo == null)
				_bundleFileInfo = LoadBundleFileInfo();
			bool retVal = false;
			var assetHashInfos = GetFileInfoJson (bundleTargetPath);
			for (int i = 0; i < objectList.Count; ++i) 
			{
				string assetPath = AssetDatabase.GetAssetPath(objectList[i]);
				bool isNeedBuildFile = IsNeedBuildFile(assetPath, assetHashInfos);
				if (!retVal) retVal = isNeedBuildFile; // 한번 true가 되면 바뀌지 않는다
			}
			if (assetHashInfos.Count != objectList.Count) // it's exist removed file
				retVal = true;

			return retVal;
		}

		private Dictionary<string, List<ContentsInfo>> LoadBundleFileInfo()
		{
			if (File.Exists(GetBundleFileInfoPath()) == false) return new();
			string contents = File.ReadAllText(GetBundleFileInfoPath());
			return DictionaryJson.FromJson<string, List<ContentsInfo>>(contents);
		}

		private void WriteToBundleFileInfo()
		{
			File.WriteAllText(GetBundleFileInfoPath(), DictionaryJson.ToJson(_bundleFileInfo));
		}

		private List<ContentsInfo> GetFileInfoJson(string targetPath)
		{
			if (_bundleFileInfo.TryGetValue(targetPath, out List<ContentsInfo> hashInfoList))
				return hashInfoList;
			_bundleFileInfo[targetPath] = hashInfoList = new List<ContentsInfo>();
			return hashInfoList;
		}

		private bool IsNeedBuildFile(string filePath, List<ContentsInfo> bundleJson)
		{
			// 1. Find fileinfo json by filepath
			int index = bundleJson.FindIndex(x => x.Path == filePath);
			var currentFileInfoJson = CreateFileInfo (filePath);
			if (currentFileInfoJson == null) return false;
			if (index == -1) // it's new file
			{
				bundleJson.Add(currentFileInfoJson);
				return true;
			}
			// 2. Compare fileinfo to current
			var fileInfoJson = bundleJson[index];
			if (fileInfoJson.Hash.Equals(currentFileInfoJson.Hash) == false ||
			    fileInfoJson.Size != currentFileInfoJson.Size)
			{
				bundleJson[index] = currentFileInfoJson;
				return true;
			}
			return false;
		}

		private ContentsInfo CreateFileInfo(string filePath)
		{
			if (File.Exists(filePath) == false)
				return null;
			var file = new FileInfo(filePath);
			ContentsInfo fileInfoJson = new()
			{
				Path = filePath,
				Hash = AssetBundleUtil.GetFileHash(file.FullName),
				Size = file.Length
			};
			return fileInfoJson;
		}
		#endregion AssetBundle Compare & Build System
		#endregion Build Bundles
	}
}

#endif
