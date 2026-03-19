using System;
using UnityEngine;
using UnityEngine.Networking;

namespace Approov
{
    public class ApproovCertificateHandler : CertificateHandler
    {
        private static readonly string TAG = "ApproovCertificateHandler ";
        private UnityWebRequest approovWebRequest = null;

        public ApproovCertificateHandler(UnityWebRequest request)
        {
            this.approovWebRequest = request;
        }

        protected override bool ValidateCertificate(byte[] certificateData)
        {
            // Extract the hostname from the URL
            Uri uri = new Uri(approovWebRequest.url);
            string hostname = uri.Host;
            ApproovService.LogTrace(TAG + "ApproovCertificateHandler.ValidateCertificate: validating certificate for " + hostname);
            // Call bridging layer versions
            string result = ApproovBridge.ShouldProceedWithNetworkConnection(certificateData, hostname, ApproovBridge.kPinTypePublicKeySha256);
            // The bridging layer processes the return result from the native layer and returns null if the connection should be allowed
            if (result == null)
            {
                ApproovService.LogTrace(TAG + "ApproovCertificateHandler.ValidateCertificate: will ALLOW connection to " + approovWebRequest.url);
                return true;
            }
            // Pr returns an eeror message if the connection should be denied
            ApproovService.LogTrace(TAG + "ApproovCertificateHandler.ValidateCertificate: will DENY connection to " + approovWebRequest.url + " with error: " + result);
            return false;
        }
    }
}
