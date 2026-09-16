#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Causeless3t.AssetBundle.Editor
{
    public sealed class BuildAssetBundles : EditorWindow
    {
        private AssetBundleBuildSettings _settings;
        private Vector2 _scrollPosition;

        [MenuItem("Tools/Build Asset Bundle #&a")]
        private static void ShowWindow()
        {
            var window = GetWindow<BuildAssetBundles>(false, "Build AssetBundles");
            window.minSize = new Vector2(600, 600);
        }

        private void OnEnable()
        {
            _settings = AssetBundleBuildSettings.Load();
        }

        private void OnGUI()
        {
            _settings ??= AssetBundleBuildSettings.Load();
            _settings.EnsureCollections();

            DrawTargetFolders();
            DrawPlatformSettings();
            DrawBuildControls();
        }

        private void DrawTargetFolders()
        {
            _scrollPosition = GUILayout.BeginScrollView(
                _scrollPosition,
                false,
                false,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(false));

            GUILayout.BeginVertical("box", GUILayout.ExpandHeight(true));
            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            GUILayout.Label("AssetBundle Target List");

            if (GUILayout.Button("Clear List", GUILayout.ExpandWidth(false)))
            {
                _settings.AssetPathes.Clear();
                _settings.AssetLabels.Clear();
                GUI.FocusControl(string.Empty);
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(5);

            for (var i = 0; i < _settings.AssetPathes.Count; i++)
            {
                GUILayout.BeginHorizontal();
                _settings.AssetPathes[i] = EditorGUILayout.TextField(_settings.AssetPathes[i]);
                _settings.AssetLabels[i] = EditorGUILayout.TextField(_settings.AssetLabels[i]);

                if (GUILayout.Button("Sel", GUILayout.ExpandWidth(false)))
                {
                    SelectTargetFolder(i);
                    GUI.FocusControl(string.Empty);
                }

                if (GUILayout.Button("-", GUILayout.ExpandWidth(false)))
                {
                    RemoveTargetFolder(i);
                    GUI.FocusControl(string.Empty);
                    GUILayout.EndHorizontal();
                    i--;
                    continue;
                }

                GUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add", GUILayout.ExpandWidth(false)))
            {
                SelectTargetFolder(_settings.AssetPathes.Count);
                GUI.FocusControl(string.Empty);
            }

            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private void DrawPlatformSettings()
        {
            GUILayout.BeginVertical("box", GUILayout.ExpandHeight(false));
            GUILayout.BeginHorizontal();

            DrawPlatformSettings(
                "Android",
                ref _settings.AndroidRevision,
                ref _settings.AndroidVersion);
            DrawPlatformSettings(
                "iOS",
                ref _settings.iOSRevision,
                ref _settings.iOSVersion);

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private static void DrawPlatformSettings(
            string platform,
            ref int revision,
            ref string appVersion)
        {
            GUILayout.BeginVertical(
                "box",
                GUILayout.ExpandHeight(false),
                GUILayout.ExpandWidth(true));

            GUILayout.Label(platform);
            GUILayout.Label("Revision");
            revision = EditorGUILayout.IntField(Math.Max(0, revision));
            GUILayout.Label("App Version");
            appVersion = EditorGUILayout.TextField(appVersion);

            GUILayout.EndVertical();
        }

        private void DrawBuildControls()
        {
            GUILayout.BeginVertical("box", GUILayout.ExpandHeight(false));
            GUILayout.Label("Bundle Output Path");
            GUILayout.BeginHorizontal();

            _settings.OutputPath = EditorGUILayout.TextField(_settings.OutputPath);

            if (GUILayout.Button("Select", GUILayout.ExpandWidth(false)))
            {
                _settings.OutputPath = EditorUtility.OpenFolderPanel(
                    "Select Output Path",
                    string.IsNullOrEmpty(_settings.OutputPath)
                        ? Application.dataPath
                        : _settings.OutputPath,
                    string.Empty);
                GUI.FocusControl(string.Empty);
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginHorizontal(GUILayout.Height(30));

            if (GUILayout.Button("Android Build"))
                BuildAssetBundle(BuildTarget.Android);

            if (GUILayout.Button("iOS Build"))
                BuildAssetBundle(BuildTarget.iOS);

            if (GUILayout.Button("Save Bundle Setting"))
            {
                _settings.Save();
                EditorUtility.DisplayDialog(
                    "Success",
                    "Build settings were saved.",
                    "OK");
            }

            GUILayout.EndHorizontal();
        }

        private void SelectTargetFolder(int index)
        {
            var initialPath = Application.dataPath;
            if (index < _settings.AssetPathes.Count)
            {
                initialPath = Path.Combine(
                    Application.dataPath,
                    _settings.AssetPathes[index]);
            }

            var selectedPath = EditorUtility.OpenFolderPanel(
                "Select Build Target",
                initialPath,
                string.Empty);

            if (string.IsNullOrEmpty(selectedPath))
                return;

            var normalizedDataPath = Application.dataPath.Replace('\\', '/').TrimEnd('/');
            var normalizedSelectedPath = selectedPath.Replace('\\', '/').TrimEnd('/');

            if (!normalizedSelectedPath.StartsWith(
                    normalizedDataPath + "/",
                    StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog(
                    "Invalid Folder",
                    "AssetBundle target folders must be inside the project's Assets folder.",
                    "OK");
                return;
            }

            var relativePath = normalizedSelectedPath
                .Substring(normalizedDataPath.Length + 1)
                .Trim('/');

            if (_settings.AssetPathes.Exists(path =>
                    string.Equals(path, relativePath, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (index < _settings.AssetPathes.Count)
            {
                _settings.AssetPathes[index] = relativePath;
                _settings.AssetLabels[index] = string.Empty;
            }
            else
            {
                _settings.AssetPathes.Add(relativePath);
                _settings.AssetLabels.Add(string.Empty);
            }
        }

        private void RemoveTargetFolder(int index)
        {
            if (index < 0 || index >= _settings.AssetPathes.Count)
                return;

            _settings.AssetPathes.RemoveAt(index);
            _settings.AssetLabels.RemoveAt(index);
        }

        public void BuildAssetBundle(BuildTarget platform)
        {
            _settings.Save();

            try
            {
                var builder = new AssetBundleBuilder(_settings);
                var outputPath = builder.Build(
                    platform,
                    progress => EditorUtility.DisplayProgressBar(
                        "Build Asset Progress",
                        "Building AssetBundles...",
                        progress));

                EditorUtility.DisplayDialog(
                    "Success",
                    $"AssetBundle build completed.\n\n{outputPath}",
                    "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Build Failed",
                    exception.Message,
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
#endif
