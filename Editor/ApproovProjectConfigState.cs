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
        private const string RuntimeAssetPath = "Assets/Resources/Approov/ApproovConfig.txt";
        private const string RuntimeAssetDirectory = "Assets/Resources/Approov";

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

            Directory.CreateDirectory(RuntimeAssetDirectory);
            File.WriteAllText(RuntimeAssetPath, normalizedConfig);
            AssetDatabase.ImportAsset(RuntimeAssetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void DeleteRuntimeAsset()
        {
            if (File.Exists(RuntimeAssetPath))
            {
                FileUtil.DeleteFileOrDirectory(RuntimeAssetPath);
            }

            if (File.Exists(RuntimeAssetPath + ".meta"))
            {
                FileUtil.DeleteFileOrDirectory(RuntimeAssetPath + ".meta");
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
