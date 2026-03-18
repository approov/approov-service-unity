using UnityEditor;
using UnityEngine;

namespace Approov.EditorTools
{
    [FilePath("ProjectSettings/ApproovInstallerState.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class ApproovInstallerState : ScriptableSingleton<ApproovInstallerState>
    {
        [SerializeField] private string installedIosVersion = string.Empty;
        [SerializeField] private string installedIosSourceUrl = string.Empty;
        [SerializeField] private string requestedIosVersion = string.Empty;
        [SerializeField] private bool missingIosPromptDismissed;

        public string InstalledIosVersion
        {
            get => installedIosVersion;
            set => installedIosVersion = value;
        }

        public string InstalledIosSourceUrl
        {
            get => installedIosSourceUrl;
            set => installedIosSourceUrl = value;
        }

        public string RequestedIosVersion
        {
            get => requestedIosVersion;
            set => requestedIosVersion = value;
        }

        public bool MissingIosPromptDismissed
        {
            get => missingIosPromptDismissed;
            set => missingIosPromptDismissed = value;
        }

        public void SaveState()
        {
            Save(true);
        }
    }
}
