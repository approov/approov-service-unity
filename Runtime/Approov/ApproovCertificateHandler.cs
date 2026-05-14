using System;
using UnityEngine;
using UnityEngine.Networking;

namespace Approov
{
    /// <summary>
    /// Unity TLS callback that delegates certificate pin validation to the Approov native SDK.
    /// </summary>
    public class ApproovCertificateHandler : CertificateHandler
    {
        private static readonly string TAG = "ApproovCertificateHandler ";
        private readonly string requestUrl;
        private readonly string authority;
        private readonly ApproovRequestContext requestContext;

        public ApproovCertificateHandler(UnityWebRequest request)
        {
            // Certificate validation runs off the main thread, so cache any UnityWebRequest state here.
            requestContext = request == null ? null : ApproovRequestContext.CreateSnapshot(request);
            requestUrl = requestContext?.Uri?.AbsoluteUri ?? string.Empty;

            if (requestContext?.Uri != null)
            {
                authority = requestContext.Uri.Authority;
            }
            else
            {
                authority = string.Empty;
            }
        }

        protected override bool ValidateCertificate(byte[] certificateData)
        {
            ApproovService.LogTrace(TAG + "ApproovCertificateHandler.ValidateCertificate: validating certificate for " + authority);

            if (!ApproovService.IsSDKInitialized())
            {
                ApproovService.LogWarning(TAG + "ApproovCertificateHandler.ValidateCertificate: SDK is not initialized, denying connection for " + requestUrl);
                return false;
            }

            if (requestContext != null && !ApproovService.ShouldApplyPinning(requestContext))
            {
                ApproovService.LogWarning(TAG + "ApproovCertificateHandler.ValidateCertificate: pinning skipped by mutator but certificate handler is still attached; denying connection for " + requestUrl);
                return false;
            }

            if (string.IsNullOrWhiteSpace(authority))
            {
                ApproovService.LogTrace(TAG + "ApproovCertificateHandler.ValidateCertificate: missing cached authority, denying connection for " + requestUrl);
                return false;
            }

            // Delegate the SPKI pin check to the native SDK. A null result means the connection is allowed.
            string result = ApproovBridge.ShouldProceedWithNetworkConnection(certificateData, authority, ApproovBridge.kPinTypePublicKeySha256);
            if (result == null)
            {
                ApproovService.LogTrace(TAG + "ApproovCertificateHandler.ValidateCertificate: will ALLOW connection to " + requestUrl);
                return true;
            }

            // Non-null means the native layer produced a reason for denying the connection.
            ApproovService.LogWarning(TAG + "ApproovCertificateHandler.ValidateCertificate: will DENY connection to " + requestUrl + " with error: " + result);
            return false;
        }
    }
}
