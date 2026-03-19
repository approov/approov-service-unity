using System.IO;
using UnityEditor;
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
            SyncRuntimeAssets(ApproovProjectConfigState.instance.ConfigString);
        }

        public static void SyncRuntimeAssets(string configString)
        {
            string normalizedConfig = string.IsNullOrWhiteSpace(configString) ? string.Empty : configString.Trim();
            string packageVersion = GetInstalledPackageVersion();

            Directory.CreateDirectory(ApproovProjectConfigState.RuntimeAssetDirectory);
            File.WriteAllText(ApproovProjectConfigState.VersionRuntimeAssetPath, packageVersion);
            AssetDatabase.ImportAsset(ApproovProjectConfigState.VersionRuntimeAssetPath, ImportAssetOptions.ForceSynchronousImport);

            if (string.IsNullOrEmpty(normalizedConfig))
            {
                DeleteRuntimeAsset();
                return;
            }

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
            ApproovProjectConfigState.instance.SaveState();
        }
    }
}
