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
                return sslPolicyErrors == SslPolicyErrors.None;
            }

            if (certificate == null || requestMessage?.RequestUri == null)
            {
                return false;
            }

#if UNITY_ANDROID || UNITY_IOS
            string result = ApproovBridge.ShouldProceedWithNetworkConnection(certificate.RawData, requestMessage.RequestUri.Host, ApproovBridge.kPinTypePublicKeySha256);
            return result == null;
#else
            return sslPolicyErrors == SslPolicyErrors.None;
#endif
        }
    }
}
