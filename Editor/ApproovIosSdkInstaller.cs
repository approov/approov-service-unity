using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Approov.EditorTools
{
    internal static class ApproovIosSdkInstaller
    {
        private const string RepoOwner = "approov";
        private const string RepoName = "approov-ios-sdk";
        private const string TargetPath = "Assets/Plugins/iOS/Approov.xcframework";
        private const string InstallerTempRoot = "Temp/ApproovInstaller";

        [Serializable]
        private sealed class GitHubAsset
        {
            public string name;
            public string browser_download_url;
        }

        [Serializable]
        private sealed class GitHubRelease
        {
            public string tag_name;
            public GitHubAsset[] assets;
        }

        [MenuItem("Tools/Approov/Install iOS SDK")]
        private static async void InstallLatestMenu()
        {
            await InstallLatestAsync();
        }

        [MenuItem("Tools/Approov/Update iOS SDK")]
        private static async void UpdateLatestMenu()
        {
            await InstallLatestAsync();
        }

        [MenuItem("Tools/Approov/Approov Settings")]
        private static void OpenSettingsMenu()
        {
            ApproovInstallerWindow.ShowWindow();
        }

        [InitializeOnLoadMethod]
        private static void RegisterPrompt()
        {
            EditorApplication.delayCall += PromptForMissingIosSdk;
        }

        public static string InstalledVersion => ApproovInstallerState.instance.InstalledIosVersion;
        public static string InstalledSourceUrl => ApproovInstallerState.instance.InstalledIosSourceUrl;
        public static string RequestedVersion
        {
            get => ApproovInstallerState.instance.RequestedIosVersion;
            set
            {
                ApproovInstallerState.instance.RequestedIosVersion = value;
                ApproovInstallerState.instance.SaveState();
            }
        }

        public static async Task InstallLatestAsync()
        {
            await InstallReleaseAsync(null);
        }

        public static async Task InstallSpecificVersionAsync(string version)
        {
            await InstallReleaseAsync(version);
        }

        public static async Task ReinstallInstalledVersionAsync()
        {
            string version = string.IsNullOrEmpty(InstalledVersion) ? RequestedVersion : InstalledVersion;
            if (string.IsNullOrEmpty(version))
            {
                await InstallLatestAsync();
                return;
            }

            await InstallReleaseAsync(version);
        }

        private static async Task InstallReleaseAsync(string version)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Approov", "Resolving iOS SDK release", 0.2f);
                GitHubRelease release = await FetchReleaseAsync(version);
                if (release == null)
                {
                    throw new InvalidOperationException("Unable to resolve the Approov iOS SDK release.");
                }

                GitHubAsset asset = release.assets?.FirstOrDefault(candidate =>
                    candidate.name != null &&
                    candidate.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                    candidate.name.IndexOf("xcframework", StringComparison.OrdinalIgnoreCase) >= 0);
                if (asset == null)
                {
                    throw new InvalidOperationException("The release does not contain an Approov xcframework zip asset.");
                }

                EditorUtility.DisplayProgressBar("Approov", "Downloading iOS SDK", 0.5f);
                string zipPath = await DownloadAssetAsync(asset.browser_download_url);

                EditorUtility.DisplayProgressBar("Approov", "Installing iOS SDK", 0.8f);
                InstallFromZip(zipPath, release.tag_name, asset.browser_download_url);
            }
            catch (Exception exception)
            {
                Debug.LogError("Approov iOS SDK installation failed: " + exception);
                EditorUtility.DisplayDialog("Approov", "Failed to install the iOS SDK.\n\n" + exception.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static async Task<GitHubRelease> FetchReleaseAsync(string version)
        {
            using HttpClient client = CreateClient();
            if (string.IsNullOrEmpty(version))
            {
                string latestJson = await client.GetStringAsync($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
                return JsonUtility.FromJson<GitHubRelease>(latestJson);
            }

            foreach (string candidate in new[] { version, "v" + version.TrimStart('v', 'V') })
            {
                try
                {
                    string taggedJson = await client.GetStringAsync($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/tags/{candidate}");
                    return JsonUtility.FromJson<GitHubRelease>(taggedJson);
                }
                catch (HttpRequestException)
                {
                    // Try the next tag format before surfacing a failure.
                }
            }

            throw new InvalidOperationException("Unable to resolve a GitHub release for version " + version + ".");
        }

        private static async Task<string> DownloadAssetAsync(string assetUrl)
        {
            Directory.CreateDirectory(InstallerTempRoot);
            string zipPath = Path.Combine(InstallerTempRoot, "Approov.xcframework.zip");
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            using HttpClient client = CreateClient();
            byte[] bytes = await client.GetByteArrayAsync(assetUrl);
            await File.WriteAllBytesAsync(zipPath, bytes);
            return zipPath;
        }

        private static HttpClient CreateClient()
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Approov-Unity-Package/0.1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        private static void InstallFromZip(string zipPath, string version, string sourceUrl)
        {
            string extractRoot = Path.Combine(InstallerTempRoot, "Extracted");
            if (Directory.Exists(extractRoot))
            {
                Directory.Delete(extractRoot, true);
            }

            ZipFile.ExtractToDirectory(zipPath, extractRoot);
            string extractedFramework = Directory.GetDirectories(extractRoot, "Approov.xcframework", SearchOption.AllDirectories).FirstOrDefault();
            if (string.IsNullOrEmpty(extractedFramework))
            {
                throw new InvalidOperationException("Approov.xcframework was not found in the downloaded archive.");
            }

            if (Directory.Exists(TargetPath))
            {
                FileUtil.DeleteFileOrDirectory(TargetPath);
                FileUtil.DeleteFileOrDirectory(TargetPath + ".meta");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(TargetPath) ?? "Assets/Plugins/iOS");
            FileUtil.CopyFileOrDirectory(extractedFramework, TargetPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ApplyPluginImporterSettings();

            ApproovInstallerState.instance.InstalledIosVersion = version;
            ApproovInstallerState.instance.RequestedIosVersion = version;
            ApproovInstallerState.instance.InstalledIosSourceUrl = sourceUrl;
            ApproovInstallerState.instance.MissingIosPromptDismissed = true;
            ApproovInstallerState.instance.SaveState();
        }

        private static void ApplyPluginImporterSettings()
        {
            foreach (PluginImporter importer in PluginImporter.GetAllImporters())
            {
                if (!importer.assetPath.StartsWith(TargetPath, StringComparison.Ordinal))
                {
                    continue;
                }

                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(false);
                importer.SetCompatibleWithPlatform(BuildTarget.iOS, true);
                importer.SaveAndReimport();
            }
        }

        private static void PromptForMissingIosSdk()
        {
            if (ApproovInstallerState.instance.MissingIosPromptDismissed || Directory.Exists(TargetPath))
            {
                return;
            }

            PackageInfo packageInfo = PackageInfo.FindForAssetPath("Packages/io.approov.service.unity");
            if (packageInfo == null)
            {
                return;
            }

            int choice = EditorUtility.DisplayDialogComplex(
                "Approov iOS SDK Missing",
                "Approov.xcframework is not installed in this project. Install the latest release now, or open the settings window to pin a specific version.",
                "Install Latest",
                "Later",
                "Settings");

            if (choice == 0)
            {
                _ = InstallLatestAsync();
            }
            else if (choice == 2)
            {
                ApproovInstallerWindow.ShowWindow();
            }
            else
            {
                ApproovInstallerState.instance.MissingIosPromptDismissed = true;
                ApproovInstallerState.instance.SaveState();
            }
        }
    }
}
