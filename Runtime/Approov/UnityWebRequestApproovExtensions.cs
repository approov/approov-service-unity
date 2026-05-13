using System.Collections;
using UnityEngine.Networking;

namespace Approov
{
    /// <summary>
    /// Coroutine-friendly UnityWebRequest entry points for Approov protection.
    /// </summary>
    public static class UnityWebRequestApproovExtensions
    {
        /// <summary>
        /// Applies Approov request mutation and certificate validation before sending the request.
        /// </summary>
        public static IEnumerator SendApproovWebRequest(this UnityWebRequest request)
        {
            return ApproovService.SendWebRequest(request);
        }
    }
}
