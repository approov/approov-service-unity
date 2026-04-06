using System;
using System.Net.Http;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Approov
{
    public sealed class ApproovHttpClientHandler : DelegatingHandler
    {
        private sealed class CachedRequestMetadata
        {
            public string AbsoluteUrl;
            public string Host;
        }

        private const string CachedUrlPropertyKey = "Approov.AbsoluteUrl";
        private const string CachedHostPropertyKey = "Approov.Host";
        private static readonly ConditionalWeakTable<HttpRequestMessage, CachedRequestMetadata> CachedRequestMetadataByMessage = new();

        public ApproovHttpClientHandler() : this(CreateDefaultInnerHandler())
        {
        }

        public ApproovHttpClientHandler(HttpMessageHandler innerHandler) : base(ConfigureInnerHandler(innerHandler ?? new HttpClientHandler()))
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CacheRequestMetadata(request);
            ApproovService.LogTrace("ApproovHttpClientHandler SendAsync start method=" + request.Method + " url=" + request.RequestUri);
            try
            {
                ApproovRequestProcessor.ApplyToHttpRequestMessage(request);
                HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                ApproovService.LogTrace(
                    "ApproovHttpClientHandler SendAsync completed method=" + request.Method +
                    " url=" + request.RequestUri +
                    " status=" + (int)response.StatusCode);
                return response;
            }
            catch (System.Exception exception)
            {
                ApproovService.LogWarning(
                    "ApproovHttpClientHandler SendAsync failed method=" + request.Method +
                    " url=" + request.RequestUri +
                    " error=" + exception);
                throw;
            }
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

        private static void CacheRequestMetadata(HttpRequestMessage request)
        {
            if (request?.RequestUri == null || !request.RequestUri.IsAbsoluteUri)
            {
                return;
            }

            CachedRequestMetadataByMessage.Remove(request);
            CachedRequestMetadataByMessage.Add(request, new CachedRequestMetadata
            {
                AbsoluteUrl = request.RequestUri.AbsoluteUri,
                Host = request.RequestUri.Host,
            });

            request.Properties[CachedUrlPropertyKey] = request.RequestUri.AbsoluteUri;
            request.Properties[CachedHostPropertyKey] = request.RequestUri.Host;
        }

        private static string GetCachedHost(HttpRequestMessage requestMessage)
        {
            if (requestMessage?.RequestUri != null && requestMessage.RequestUri.IsAbsoluteUri)
            {
                return requestMessage.RequestUri.Host;
            }

            if (requestMessage?.Properties != null &&
                requestMessage.Properties.TryGetValue(CachedHostPropertyKey, out object cachedHostValue) &&
                cachedHostValue is string propertyHost &&
                !string.IsNullOrWhiteSpace(propertyHost))
            {
                return propertyHost;
            }

            if (requestMessage != null &&
                CachedRequestMetadataByMessage.TryGetValue(requestMessage, out CachedRequestMetadata metadata) &&
                !string.IsNullOrWhiteSpace(metadata.Host))
            {
                return metadata.Host;
            }

            string candidate = requestMessage?.RequestUri?.ToString();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri absoluteUri))
                {
                    return absoluteUri.Host;
                }

                if (!candidate.Contains("://", StringComparison.Ordinal) &&
                    Uri.TryCreate("https://" + candidate.TrimStart('/'), UriKind.Absolute, out Uri guessedUri))
                {
                    return guessedUri.Host;
                }
            }

            return null;
        }

        private static string GetCachedUrl(HttpRequestMessage requestMessage)
        {
            if (requestMessage?.RequestUri != null && requestMessage.RequestUri.IsAbsoluteUri)
            {
                return requestMessage.RequestUri.AbsoluteUri;
            }

            if (requestMessage?.Properties != null &&
                requestMessage.Properties.TryGetValue(CachedUrlPropertyKey, out object cachedUrlValue) &&
                cachedUrlValue is string propertyUrl &&
                !string.IsNullOrWhiteSpace(propertyUrl))
            {
                return propertyUrl;
            }

            if (requestMessage != null &&
                CachedRequestMetadataByMessage.TryGetValue(requestMessage, out CachedRequestMetadata metadata) &&
                !string.IsNullOrWhiteSpace(metadata.AbsoluteUrl))
            {
                return metadata.AbsoluteUrl;
            }

            return requestMessage?.RequestUri?.ToString() ?? "<unknown>";
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

            string host = GetCachedHost(requestMessage);
            string requestUrl = GetCachedUrl(requestMessage);
            X509Certificate2 certificateToValidate = certificate;

            if (certificateToValidate == null &&
                chain?.ChainElements != null &&
                chain.ChainElements.Count > 0)
            {
                certificateToValidate = chain.ChainElements[0].Certificate;
            }

            if (certificateToValidate == null || string.IsNullOrWhiteSpace(host))
            {
                ApproovService.LogTrace(
                    "ApproovHttpClientHandler ValidateServerCertificate missing certificate or absolute host for " + requestUrl +
                    " certificatePresent=" + (certificateToValidate != null) +
                    " hostPresent=" + !string.IsNullOrWhiteSpace(host) +
                    " sslPolicyErrors=" + sslPolicyErrors);
                return false;
            }

#if UNITY_ANDROID || UNITY_IOS
            string result = ApproovBridge.ShouldProceedWithNetworkConnection(certificateToValidate.RawData, host, ApproovBridge.kPinTypePublicKeySha256);
            ApproovService.LogTrace("ApproovHttpClientHandler ValidateServerCertificate host=" + host + " url=" + requestUrl + " result=" + (result ?? "ALLOW"));
            return result == null;
#else
            ApproovService.LogTrace(
                "ApproovHttpClientHandler ValidateServerCertificate using platform TLS validation for " +
                host + " errors=" + sslPolicyErrors);
            return sslPolicyErrors == SslPolicyErrors.None;
#endif
        }
    }
}
