using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;

namespace VRCSceneBackup
{
    /// <summary>User preferences, stored per machine in EditorPrefs.</summary>
    internal static class SceneBackupSettings
    {
        const string EnabledKey = "VRCSceneBackup.Enabled";
        const string KeepCountKey = "VRCSceneBackup.KeepCount";
        const string BackupDirKey = "VRCSceneBackup.BackupDir";
        const string LogKey = "VRCSceneBackup.Log";

        public const bool DefaultEnabled = true;
        public const int DefaultKeepCount = 5;
        public const int MinKeepCount = 1;
        public const int MaxKeepCount = 100;
        public const string DefaultBackupDir = "Assets/Scene Backup";
        public const bool DefaultLog = true;

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, DefaultEnabled);
            set => EditorPrefs.SetBool(EnabledKey, value);
        }

        /// <summary>How many backups to keep per platform and scene.</summary>
        public static int KeepCount
        {
            get => Mathf.Clamp(EditorPrefs.GetInt(KeepCountKey, DefaultKeepCount), MinKeepCount, MaxKeepCount);
            set => EditorPrefs.SetInt(KeepCountKey, Mathf.Clamp(value, MinKeepCount, MaxKeepCount));
        }

        public static bool LogEnabled
        {
            get => EditorPrefs.GetBool(LogKey, DefaultLog);
            set => EditorPrefs.SetBool(LogKey, value);
        }

        /// <summary>Raw text as typed by the user (may be invalid). Used by the settings UI only.</summary>
        public static string BackupDirRaw
        {
            get => EditorPrefs.GetString(BackupDirKey, DefaultBackupDir);
            set => EditorPrefs.SetString(BackupDirKey, value ?? string.Empty);
        }

        /// <summary>Validated backup root folder. Falls back to the default if the saved value is invalid.</summary>
        public static string BackupDir
        {
            get
            {
                string value = Normalize(BackupDirRaw);
                return IsValidBackupDir(value) ? value : DefaultBackupDir;
            }
        }

        public static bool IsValidBackupDir(string path)
        {
            path = Normalize(path);

            if (string.IsNullOrEmpty(path)) return false;
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) return false;

            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (string segment in path.Split('/'))
            {
                if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..") return false;
                if (segment.IndexOfAny(invalid) >= 0) return false;
            }

            return true;
        }

        public static void ResetToDefaults()
        {
            EditorPrefs.DeleteKey(EnabledKey);
            EditorPrefs.DeleteKey(KeepCountKey);
            EditorPrefs.DeleteKey(BackupDirKey);
            EditorPrefs.DeleteKey(LogKey);
        }

        static string Normalize(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').Trim().TrimEnd('/');
        }
    }

    /// <summary>Core logic: saves a copy of the active scene and prunes old backups.</summary>
    internal static class SceneBackupService
    {
        const string LogPrefix = "[VRC Scene Backup] ";
        const string TimestampFormat = "yyyy-MM-dd_HH-mm-ss";

        /// <summary>
        /// Saves a copy of the active scene as {BackupDir}/{platform}/bak_{scene}_{timestamp}.unity.
        /// Never throws; returns false and logs a warning on failure.
        /// </summary>
        public static bool TryBackup(out string backupPath)
        {
            backupPath = null;

            try
            {
                Scene scene = SceneManager.GetActiveScene();

                if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path))
                {
                    Log("Skipped: active scene has not been saved yet.");
                    return false;
                }

                string rootDir = SceneBackupSettings.BackupDir;

                // Never back up a backup
                if (scene.path.StartsWith(rootDir + "/", StringComparison.Ordinal))
                {
                    Log("Skipped: active scene is already a backup.");
                    return false;
                }

                string platform = GetPlatformName(EditorUserBuildSettings.activeBuildTarget);
                string platformDir = rootDir + "/" + platform;
                string sceneName = SanitizeFileName(scene.name);
                string stamp = DateTime.Now.ToString(TimestampFormat);

                EnsureFolder(platformDir);

                string path = AssetDatabase.GenerateUniqueAssetPath(
                    $"{platformDir}/bak_{sceneName}_{stamp}.unity");

                // saveAsCopy = true: writes a copy of the in-memory scene and leaves the open scene untouched
                if (!EditorSceneManager.SaveScene(scene, path, true))
                {
                    Debug.LogWarning(LogPrefix + "Failed to save backup: " + path);
                    return false;
                }

                backupPath = path;
                Log("Saved " + path);

                Prune(platformDir, sceneName, SceneBackupSettings.KeepCount);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + "Backup failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Creates the folder (and any missing parents) under Assets.</summary>
        public static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) return;

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folderPath));
        }

        static void Log(string message)
        {
            if (SceneBackupSettings.LogEnabled)
                Debug.Log(LogPrefix + message);
        }

        static string GetPlatformName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows64: return "windows";
                case BuildTarget.Android: return "android";
                case BuildTarget.iOS: return "ios";
                default: return SanitizeFileName(target.ToString().ToLowerInvariant());
            }
        }

        static string SanitizeFileName(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value;
        }

        /// <summary>Deletes the oldest backups of this scene in the platform folder beyond the keep limit.</summary>
        static void Prune(string platformDir, string sceneName, int keepCount)
        {
            // Strict match, so a scene named "tb" never touches backups of "tb_world"
            var pattern = new Regex(
                $"^bak_{Regex.Escape(sceneName)}_\\d{{4}}-\\d{{2}}-\\d{{2}}_\\d{{2}}-\\d{{2}}-\\d{{2}}(?: \\d+)?\\.unity$",
                RegexOptions.CultureInvariant);

            var stale = Directory.EnumerateFiles(platformDir, "*.unity", SearchOption.TopDirectoryOnly)
                .Where(f => pattern.IsMatch(Path.GetFileName(f)))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(keepCount)
                .ToList();

            foreach (string file in stale)
            {
                string assetPath = file.Replace('\\', '/');
                if (!AssetDatabase.DeleteAsset(assetPath))
                    Debug.LogWarning(LogPrefix + "Could not delete old backup: " + assetPath);
            }
        }
    }

    /// <summary>Runs automatically before every VRChat SDK build (Build &amp; Test and Publish).</summary>
    public sealed class SceneBackupOnBuild : IVRCSDKBuildRequestedCallback
    {
        // Run before other build callbacks
        public int callbackOrder => -1000;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (SceneBackupSettings.Enabled)
                SceneBackupService.TryBackup(out _);

            // Always allow the build to continue, even if the backup failed
            return true;
        }
    }

    internal static class SceneBackupMenu
    {
        const string Root = "Tools/VRC Scene Backup/";
        const string PreferencesPath = "Preferences/VRC Scene Backup";

        [MenuItem(Root + "Enabled")]
        static void ToggleEnabled() => SceneBackupSettings.Enabled = !SceneBackupSettings.Enabled;

        [MenuItem(Root + "Enabled", true)]
        static bool ToggleEnabledValidate()
        {
            Menu.SetChecked(Root + "Enabled", SceneBackupSettings.Enabled);
            return true;
        }

        [MenuItem(Root + "Backup Now")]
        static void BackupNow() => SceneBackupService.TryBackup(out _);

        [MenuItem(Root + "Open Backup Folder")]
        static void OpenBackupFolder() => OpenFolder();

        [MenuItem(Root + "Settings...")]
        static void OpenSettings() => SettingsService.OpenUserPreferences(PreferencesPath);

        /// <summary>Creates the backup folder if needed, then opens it in the Project window.</summary>
        internal static void OpenFolder()
        {
            string dir = SceneBackupSettings.BackupDir;
            bool created = !AssetDatabase.IsValidFolder(dir);

            SceneBackupService.EnsureFolder(dir);

            if (created)
            {
                AssetDatabase.Refresh();

                // The Project window needs a few frames to show a newly created folder
                RunAfterFrames(3, () => NavigateToFolder(dir));
            }
            else
            {
                NavigateToFolder(dir);
            }
        }

        static void NavigateToFolder(string dir)
        {
            var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dir);
            if (folder == null)
            {
                Debug.LogWarning("[VRC Scene Backup] Could not open backup folder: " + dir);
                return;
            }

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = folder;

            // Navigate into the folder; fall back to highlighting it if that is not possible
            if (!TryShowFolderContents(folder))
                EditorGUIUtility.PingObject(folder);
        }

        /// <summary>Runs an action after the given number of editor update ticks.</summary>
        static void RunAfterFrames(int frames, Action action)
        {
            void Tick()
            {
                if (--frames > 0) return;

                EditorApplication.update -= Tick;
                action();
            }

            EditorApplication.update += Tick;
        }

        /// <summary>
        /// Shows the contents of a folder in the Project window (like double-clicking it).
        /// Uses reflection because Unity has no public API for this. Returns false on failure.
        /// </summary>
        static bool TryShowFolderContents(UnityEngine.Object folder)
        {
            try
            {
                Type browserType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");
                if (browserType == null) return false;

                UnityEngine.Object[] browsers = Resources.FindObjectsOfTypeAll(browserType);
                if (browsers.Length == 0) return false;

                const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // Only works in Two Column Layout (m_ViewMode: 0 = OneColumn, 1 = TwoColumns)
                FieldInfo viewMode = browserType.GetField("m_ViewMode", Flags);
                if (viewMode != null && Convert.ToInt32(viewMode.GetValue(browsers[0])) != 1)
                    return false;

                MethodInfo show = browserType.GetMethod("ShowFolderContents", Flags);
                if (show == null) return false;

                show.Invoke(browsers[0], new object[] { folder.GetInstanceID(), true });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        [SettingsProvider]
        static SettingsProvider CreatePreferences()
        {
            return new SettingsProvider(PreferencesPath, SettingsScope.User)
            {
                label = "VRC Scene Backup",
                keywords = new[] { "scene", "backup", "vrchat", "build", "keep", "folder" },
                guiHandler = _ =>
                {
                    EditorGUILayout.Space();

                    SceneBackupSettings.Enabled = EditorGUILayout.Toggle(
                        new GUIContent("Backup before build", "Save a scene copy before every VRChat Build & Test / Publish."),
                        SceneBackupSettings.Enabled);

                    SceneBackupSettings.KeepCount = EditorGUILayout.IntSlider(
                        new GUIContent("Backups to keep", "Per platform and per scene. Older backups are deleted."),
                        SceneBackupSettings.KeepCount,
                        SceneBackupSettings.MinKeepCount,
                        SceneBackupSettings.MaxKeepCount);

                    SceneBackupSettings.BackupDirRaw = EditorGUILayout.TextField(
                        new GUIContent("Backup folder", "Must be inside the Assets folder."),
                        SceneBackupSettings.BackupDirRaw);

                    if (!SceneBackupSettings.IsValidBackupDir(SceneBackupSettings.BackupDirRaw))
                    {
                        EditorGUILayout.HelpBox(
                            "Invalid folder. It must start with \"Assets/\". Using the default: " + SceneBackupSettings.DefaultBackupDir,
                            MessageType.Warning);
                    }

                    SceneBackupSettings.LogEnabled = EditorGUILayout.Toggle(
                        new GUIContent("Log to Console", "Print a message when a backup is saved."),
                        SceneBackupSettings.LogEnabled);

                    EditorGUILayout.Space();

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Backup Now")) SceneBackupService.TryBackup(out _);
                        if (GUILayout.Button("Open Folder")) OpenFolder();
                        if (GUILayout.Button("Reset to Defaults")) SceneBackupSettings.ResetToDefaults();
                    }
                }
            };
        }
    }
}