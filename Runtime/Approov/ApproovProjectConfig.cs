using UnityEngine;

namespace Approov
{
    internal static class ApproovProjectConfig
    {
        private const string ConfigResourcePath = "Approov/ApproovConfig";

        public static string GetConfigString()
        {
            TextAsset configAsset = Resources.Load<TextAsset>(ConfigResourcePath);
            return configAsset == null ? string.Empty : (configAsset.text ?? string.Empty).Trim();
        }

        public static bool HasConfigString()
        {
            return !string.IsNullOrWhiteSpace(GetConfigString());
        }
    }
}
