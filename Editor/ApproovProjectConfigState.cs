using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Approov.EditorTools
{
    [FilePath("ProjectSettings/ApproovProjectConfigState.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class ApproovProjectConfigState : ScriptableSingleton<ApproovProjectConfigState>
    {
        internal const string RuntimeAssetPath = "Assets/Resources/Approov/ApproovConfig.txt";
        internal const string VersionRuntimeAssetPath = "Assets/Resources/Approov/ApproovPackageVersion.txt";
        internal const string RuntimeAssetDirectory = "Assets/Resources/Approov";

        [SerializeField] private string approovConfigString = string.Empty;

        public string ConfigString
        {
            get => approovConfigString;
            set => approovConfigString = value;
        }

        public bool HasConfigString => !string.IsNullOrWhiteSpace(approovConfigString);

        public void SaveState()
        {
            ApproovProjectConfigSync.SyncRuntimeAssets(approovConfigString);
            Save(true);
        }
    }

    internal static class ApproovProjectConfigSync
    {
        [InitializeOnLoadMethod]
        private static void SyncOnLoad()
        {
            // Keep the runtime metadata assets aligned with the installed package state on editor load/import.
            SyncRuntimeAssets(ApproovProjectConfigState.instance.ConfigString);
        }

        public static void SyncRuntimeAssets(string configString)
        {
            string normalizedConfig = string.IsNullOrWhiteSpace(configString) ? string.Empty : configString.Trim();
            string packageVersion = GetInstalledPackageVersion();

            // Runtime code cannot reliably read package.json from the installed UPM package on device builds,
            // so we mirror the package version into a Resources asset during editor time.
            Directory.CreateDirectory(ApproovProjectConfigState.RuntimeAssetDirectory);
            File.WriteAllText(ApproovProjectConfigState.VersionRuntimeAssetPath, packageVersion);
            AssetDatabase.ImportAsset(ApproovProjectConfigState.VersionRuntimeAssetPath, ImportAssetOptions.ForceSynchronousImport);

            if (string.IsNullOrEmpty(normalizedConfig))
            {
                DeleteRuntimeAsset();
                return;
            }

            // The config string is stored in project settings for editing convenience, then mirrored into
            // Resources so the runtime can read it inside player builds without depending on editor APIs.
            File.WriteAllText(ApproovProjectConfigState.RuntimeAssetPath, normalizedConfig);
            AssetDatabase.ImportAsset(ApproovProjectConfigState.RuntimeAssetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static string GetInstalledPackageVersion()
        {
            UnityEditor.PackageManager.PackageInfo packageInfo =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ApproovProjectConfigState).Assembly);
            return string.IsNullOrWhiteSpace(packageInfo?.version) ? "unknown" : packageInfo.version.Trim();
        }

        private static void DeleteRuntimeAsset()
        {
            if (File.Exists(ApproovProjectConfigState.RuntimeAssetPath))
            {
                FileUtil.DeleteFileOrDirectory(ApproovProjectConfigState.RuntimeAssetPath);
            }

            if (File.Exists(ApproovProjectConfigState.RuntimeAssetPath + ".meta"))
            {
                FileUtil.DeleteFileOrDirectory(ApproovProjectConfigState.RuntimeAssetPath + ".meta");
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
    }

    internal sealed class ApproovProjectConfigBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            ValidateAndroidMinSdk(report);
            ApproovProjectConfigState.instance.SaveState();
        }

        private static void ValidateAndroidMinSdk(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
            {
                return;
            }

            // Current Unity versions require API 25+, so fail the build early with a clear message
            // instead of allowing an unsupported Android minimum SDK configuration.
            if (PlayerSettings.Android.minSdkVersion < AndroidSdkVersions.AndroidApiLevel25)
            {
                throw new BuildFailedException(
                    "Approov Unity Service Layer requires Android minSdkVersion 25 or higher. " +
                    "Update Project Settings > Player > Android > Minimum API Level before building.");
            }
        }
    }
}
