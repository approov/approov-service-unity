using UnityEditor;
using UnityEngine;

namespace Approov.EditorTools
{
    internal sealed class ApproovInstallerWindow : EditorWindow
    {
        private string requestedVersion;

        public static void ShowWindow()
        {
            GetWindow<ApproovInstallerWindow>("Approov Settings");
        }

        private void OnEnable()
        {
            requestedVersion = ApproovIosSdkInstaller.RequestedVersion;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Approov iOS SDK", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Install the latest Approov.xcframework or pin a specific release from approov/approov-ios-sdk.", MessageType.Info);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Installed Version", string.IsNullOrEmpty(ApproovIosSdkInstaller.InstalledVersion) ? "Not installed" : ApproovIosSdkInstaller.InstalledVersion);
                EditorGUILayout.TextField("Installed Source", string.IsNullOrEmpty(ApproovIosSdkInstaller.InstalledSourceUrl) ? "Not installed" : ApproovIosSdkInstaller.InstalledSourceUrl);
            }

            GUILayout.Space(8f);
            requestedVersion = EditorGUILayout.TextField("Specific Version", requestedVersion ?? string.Empty);
            ApproovIosSdkInstaller.RequestedVersion = requestedVersion;

            GUILayout.Space(8f);
            if (GUILayout.Button("Install Latest Release"))
            {
                _ = ApproovIosSdkInstaller.InstallLatestAsync();
            }

            if (GUILayout.Button("Install Specific Version"))
            {
                if (string.IsNullOrWhiteSpace(requestedVersion))
                {
                    EditorUtility.DisplayDialog("Approov", "Enter a release tag such as 3.5.3.", "OK");
                }
                else
                {
                    _ = ApproovIosSdkInstaller.InstallSpecificVersionAsync(requestedVersion.Trim());
                }
            }

            if (GUILayout.Button("Reinstall Current Version"))
            {
                _ = ApproovIosSdkInstaller.ReinstallInstalledVersionAsync();
            }
        }
    }
}
