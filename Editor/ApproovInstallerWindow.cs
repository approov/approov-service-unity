using UnityEditor;
using UnityEngine;

namespace Approov.EditorTools
{
    internal sealed class ApproovInstallerWindow : EditorWindow
    {
        private string requestedVersion;
        private string approovConfig;

        public static void ShowWindow()
        {
            GetWindow<ApproovInstallerWindow>("Approov Settings");
        }

        private void OnEnable()
        {
            requestedVersion = ApproovIosSdkInstaller.RequestedVersion;
            approovConfig = ApproovProjectConfigState.instance.ConfigString;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Approov Config", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Approov needs a config string before it can initialize. Run `approov sdk -getConfigString`, paste the result here, and the window will store it in project settings automatically. Native initialization can only be verified on iOS and Android builds, not in the editor.", MessageType.Info);

            EditorGUILayout.LabelField("Config Status", string.IsNullOrWhiteSpace(approovConfig) ? "Missing" : "Configured");
            EditorGUI.BeginChangeCheck();
            string updatedConfig = EditorGUILayout.TextArea(approovConfig ?? string.Empty, GUILayout.MinHeight(64f));
            if (EditorGUI.EndChangeCheck())
            {
                approovConfig = updatedConfig?.Trim();
                ApproovProjectConfigState.instance.ConfigString = approovConfig;
                ApproovProjectConfigState.instance.SaveState();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy CLI Command"))
                {
                    EditorGUIUtility.systemCopyBuffer = "approov sdk -getConfigString";
                }

                if (GUILayout.Button("Clear Config"))
                {
                    approovConfig = string.Empty;
                    ApproovProjectConfigState.instance.ConfigString = approovConfig;
                    ApproovProjectConfigState.instance.SaveState();
                }
            }

            if (string.IsNullOrWhiteSpace(approovConfig))
            {
                EditorGUILayout.HelpBox("Approov will not initialize until a config string is saved here.", MessageType.Warning);
            }

            GUILayout.Space(12f);
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
