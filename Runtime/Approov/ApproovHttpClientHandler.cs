using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Approov
{
    public sealed class ApproovHttpClientHandler : DelegatingHandler
    {
        public ApproovHttpClientHandler() : this(CreateDefaultInnerHandler())
        {
        }

        public ApproovHttpClientHandler(HttpMessageHandler innerHandler) : base(ConfigureInnerHandler(innerHandler ?? new HttpClientHandler()))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ApproovService.LogTrace("ApproovHttpClientHandler SendAsync start method=" + request.Method + " url=" + request.RequestUri);
            ApproovRequestProcessor.ApplyToHttpRequestMessage(request);
            return base.SendAsync(request, cancellationToken);
        }

        private static HttpMessageHandler CreateDefaultInnerHandler()
        {
            return ConfigureInnerHandler(new HttpClientHandler());
        }

        private static HttpMessageHandler ConfigureInnerHandler(HttpMessageHandler innerHandler)
        {
            if (innerHandler is HttpClientHandler httpClientHandler)
            {
                httpClientHandler.ServerCertificateCustomValidationCallback = ValidateServerCertificate;
            }

            return innerHandler;
        }

        private static bool ValidateServerCertificate(HttpRequestMessage requestMessage, X509Certificate2 certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (!ApproovService.IsSDKInitialized())
            {
                ApproovService.LogTrace("ApproovHttpClientHandler ValidateServerCertificate using platform TLS validation because SDK is not initialized");
                return sslPolicyErrors == SslPolicyErrors.None;
            }

            if (requestMessage != null && !ApproovService.ShouldApplyPinning(ApproovRequestContext.CreateSnapshot(requestMessage)))
            {
                ApproovService.LogTrace("ApproovHttpClientHandler ValidateServerCertificate skipping pinning because mutator disabled it");
                return sslPolicyErrors == SslPolicyErrors.None;
            }

            if (certificate == null || requestMessage?.RequestUri == null)
            {
                ApproovService.LogTrace("ApproovHttpClientHandler ValidateServerCertificate missing certificate or request URI");
                return false;
            }

#if UNITY_ANDROID || UNITY_IOS
            string result = ApproovBridge.ShouldProceedWithNetworkConnection(certificate.RawData, requestMessage.RequestUri.Host, ApproovBridge.kPinTypePublicKeySha256);
            ApproovService.LogTrace("ApproovHttpClientHandler ValidateServerCertificate host=" + requestMessage.RequestUri.Host + " result=" + (result ?? "ALLOW"));
            return result == null;
#else
            return sslPolicyErrors == SslPolicyErrors.None;
#endif
        }
    }
}
