using UnityEngine;

namespace Approov
{
    internal static class ApproovProjectConfig
    {
        private const string ConfigResourcePath = "Approov/ApproovConfig";
        private const string PackageVersionResourcePath = "Approov/ApproovPackageVersion";

        public static string GetConfigString()
        {
            TextAsset configAsset = Resources.Load<TextAsset>(ConfigResourcePath);
            return configAsset == null ? string.Empty : (configAsset.text ?? string.Empty).Trim();
        }

        public static bool HasConfigString()
        {
            return !string.IsNullOrWhiteSpace(GetConfigString());
        }

        public static string GetPackageVersion()
        {
            TextAsset versionAsset = Resources.Load<TextAsset>(PackageVersionResourcePath);
            return versionAsset == null ? "unknown" : (versionAsset.text ?? string.Empty).Trim();
        }
    }
}
