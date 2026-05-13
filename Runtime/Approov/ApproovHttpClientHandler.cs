using System;
using System.Net.Http;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Approov
{
    /// <summary>
    /// <see cref="HttpMessageHandler"/> adapter that applies the shared Approov request pipeline
    /// to <see cref="HttpClient"/> traffic.
    /// </summary>
    public sealed class ApproovHttpClientHandler : DelegatingHandler
    {
        private sealed class CachedRequestMetadata
        {
            public string AbsoluteUrl;
            public string Authority;
        }

        private const string CachedUrlPropertyKey = "Approov.AbsoluteUrl";
        private const string CachedAuthorityPropertyKey = "Approov.Authority";
        // Certificate validation can happen after the live request URI has been rewritten or partially
        // obscured by the platform stack, so cache the original absolute URL and authority here.
        private static readonly ConditionalWeakTable<HttpRequestMessage, CachedRequestMetadata> CachedRequestMetadataByMessage = new();

        /// <summary>
        /// Creates a handler with a default <see cref="HttpClientHandler"/> inner handler.
        /// </summary>
        public ApproovHttpClientHandler() : this(CreateDefaultInnerHandler())
        {
        }

        /// <summary>
        /// Creates a handler that wraps an existing message-handler pipeline.
        /// </summary>
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
                // Hook Approov pin evaluation only when we have direct access to the TLS callback.
                Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool> existingCallback =
                    httpClientHandler.ServerCertificateCustomValidationCallback;
                httpClientHandler.ServerCertificateCustomValidationCallback = existingCallback == null
                    ? ValidateServerCertificate
                    : (requestMessage, certificate, chain, sslPolicyErrors) =>
                    {
                        if (!existingCallback(requestMessage, certificate, chain, sslPolicyErrors))
                        {
                            ApproovService.LogTrace("ApproovHttpClientHandler ValidateServerCertificate denied by existing callback");
                            return false;
                        }

                        return ValidateServerCertificate(requestMessage, certificate, chain, sslPolicyErrors);
                    };
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
                Authority = request.RequestUri.Authority,
            });

            request.Properties[CachedUrlPropertyKey] = request.RequestUri.AbsoluteUri;
            request.Properties[CachedAuthorityPropertyKey] = request.RequestUri.Authority;
        }

        private static string GetCachedAuthority(HttpRequestMessage requestMessage)
        {
            if (requestMessage?.RequestUri != null && requestMessage.RequestUri.IsAbsoluteUri)
            {
                return requestMessage.RequestUri.Authority;
            }

            if (requestMessage?.Properties != null &&
                requestMessage.Properties.TryGetValue(CachedAuthorityPropertyKey, out object cachedAuthorityValue) &&
                cachedAuthorityValue is string propertyAuthority &&
                !string.IsNullOrWhiteSpace(propertyAuthority))
            {
                return propertyAuthority;
            }

            if (requestMessage != null &&
                CachedRequestMetadataByMessage.TryGetValue(requestMessage, out CachedRequestMetadata metadata) &&
                !string.IsNullOrWhiteSpace(metadata.Authority))
            {
                return metadata.Authority;
            }

            string candidate = requestMessage?.RequestUri?.ToString();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri absoluteUri))
                {
                    return absoluteUri.Authority;
                }

                if (!candidate.Contains("://", StringComparison.Ordinal) &&
                    Uri.TryCreate("https://" + candidate.TrimStart('/'), UriKind.Absolute, out Uri guessedUri))
                {
                    return guessedUri.Authority;
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

            if (requestMessage != null && !ApproovService.ShouldApplyPinning(ApproovRequestContext.CreateSnapshot(requestMessage, includeBody: false)))
            {
                ApproovService.LogTrace("ApproovHttpClientHandler ValidateServerCertificate skipping pinning because mutator disabled it");
                return sslPolicyErrors == SslPolicyErrors.None;
            }

            string authority = GetCachedAuthority(requestMessage);
            string requestUrl = GetCachedUrl(requestMessage);
            X509Certificate2 certificateToValidate = certificate;

            if (certificateToValidate == null &&
                chain?.ChainElements != null &&
                chain.ChainElements.Count > 0)
            {
                certificateToValidate = chain.ChainElements[0].Certificate;
            }

            if (certificateToValidate == null || string.IsNullOrWhiteSpace(authority))
            {
                ApproovService.LogTrace(
                    "ApproovHttpClientHandler ValidateServerCertificate missing certificate or absolute authority for " + requestUrl +
                    " certificatePresent=" + (certificateToValidate != null) +
                    " authorityPresent=" + !string.IsNullOrWhiteSpace(authority) +
                    " sslPolicyErrors=" + sslPolicyErrors);
                return false;
            }

#if UNITY_ANDROID || UNITY_IOS
            string result = ApproovBridge.ShouldProceedWithNetworkConnection(certificateToValidate.RawData, authority, ApproovBridge.kPinTypePublicKeySha256);
            ApproovService.LogTrace("ApproovHttpClientHandler ValidateServerCertificate authority=" + authority + " url=" + requestUrl + " result=" + (result ?? "ALLOW"));
            return result == null;
#else
            ApproovService.LogTrace(
                "ApproovHttpClientHandler ValidateServerCertificate using platform TLS validation for " +
                authority + " errors=" + sslPolicyErrors);
            return sslPolicyErrors == SslPolicyErrors.None;
#endif
        }
    }
}
