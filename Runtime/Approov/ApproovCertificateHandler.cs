using System;
using UnityEngine;
using UnityEngine.Networking;

namespace Approov
{
    public class ApproovCertificateHandler : CertificateHandler
    {
        private static readonly string TAG = "ApproovCertificateHandler ";
        private readonly string requestUrl;
        private readonly string hostname;

        public ApproovCertificateHandler(UnityWebRequest request)
        {
            // Certificate validation runs off the main thread, so cache any UnityWebRequest state here.
            requestUrl = request?.url ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(requestUrl) &&
                Uri.TryCreate(requestUrl, UriKind.Absolute, out Uri uri))
            {
                hostname = uri.Host;
            }
            else
            {
                hostname = string.Empty;
            }
        }

        protected override bool ValidateCertificate(byte[] certificateData)
        {
            ApproovService.LogTrace(TAG + "ApproovCertificateHandler.ValidateCertificate: validating certificate for " + hostname);

            if (string.IsNullOrWhiteSpace(hostname))
            {
                ApproovService.LogTrace(TAG + "ApproovCertificateHandler.ValidateCertificate: missing cached hostname, denying connection for " + requestUrl);
                return false;
            }

            // Call bridging layer versions
            string result = ApproovBridge.ShouldProceedWithNetworkConnection(certificateData, hostname, ApproovBridge.kPinTypePublicKeySha256);
            // The bridging layer processes the return result from the native layer and returns null if the connection should be allowed
            if (result == null)
            {
                ApproovService.LogTrace(TAG + "ApproovCertificateHandler.ValidateCertificate: will ALLOW connection to " + requestUrl);
                return true;
            }
            // Pr returns an eeror message if the connection should be denied
            ApproovService.LogTrace(TAG + "ApproovCertificateHandler.ValidateCertificate: will DENY connection to " + requestUrl + " with error: " + result);
            return false;
        }
    }
}
