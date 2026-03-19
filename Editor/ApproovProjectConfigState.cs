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
            ApproovProjectConfigSync.SyncRuntimeAsset(approovConfigString);
            Save(true);
        }
    }

    internal static class ApproovProjectConfigSync
    {
        public static void SyncRuntimeAsset(string configString)
        {
            string normalizedConfig = string.IsNullOrWhiteSpace(configString) ? string.Empty : configString.Trim();

            if (string.IsNullOrEmpty(normalizedConfig))
            {
                DeleteRuntimeAsset();
                return;
            }

            Directory.CreateDirectory(ApproovProjectConfigState.RuntimeAssetDirectory);
            File.WriteAllText(ApproovProjectConfigState.RuntimeAssetPath, normalizedConfig);
            AssetDatabase.ImportAsset(ApproovProjectConfigState.RuntimeAssetPath, ImportAssetOptions.ForceSynchronousImport);
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
