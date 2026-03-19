using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using UnityEngine.Networking;

namespace Approov
{
    internal static class ApproovRequestProcessor
    {
        public static void ApplyToUnityWebRequest(UnityWebRequest request)
        {
            Apply(
                request.uri ?? new Uri(request.url),
                request.GetRequestHeader,
                request.SetRequestHeader,
                uri => request.uri = uri);
        }

        public static void ApplyToHttpRequestMessage(HttpRequestMessage request)
        {
            Apply(
                request.RequestUri,
                header => GetHttpHeader(request, header),
                (header, value) => SetHttpHeader(request, header, value),
                uri => request.RequestUri = uri);
        }

        private static void Apply(Uri requestUri, Func<string, string> getHeader, Action<string, string> setHeader, Action<Uri> setUri)
        {
            if (requestUri == null)
            {
                throw new ArgumentNullException(nameof(requestUri));
            }

            if (!ApproovService.IsSDKInitialized())
            {
                throw new InitializationFailureException("ApproovService Error: SDK not initialized");
            }

            string urlWithBaseAddress = requestUri.AbsoluteUri;
            ApproovService.LogTrace("ApproovRequestProcessor Apply start url=" + urlWithBaseAddress);
            if (ApproovService.CheckURLIsExcluded(urlWithBaseAddress))
            {
                ApproovService.LogTrace("ApproovRequestProcessor URL excluded from protection");
                return;
            }

            string bindingHeader = ApproovService.GetBindingHeader();
            if (bindingHeader != null)
            {
                string headerValue = getHeader(bindingHeader);
                if (headerValue == null)
                {
                    throw new ConfigurationFailureException("ApproovRequestProcessor Missing token binding header: " + bindingHeader);
                }

                ApproovService.LogTrace("ApproovRequestProcessor Binding header present: " + bindingHeader + " valueLength=" + headerValue.Length);
                ApproovService.SetDataHashInToken(headerValue);
            }
            else
            {
                ApproovService.LogTrace("ApproovRequestProcessor No binding header configured");
            }

            ApproovTokenFetchResult approovResult = ApproovBridge.FetchApproovTokenAndWait(urlWithBaseAddress);
            if (approovResult.isConfigChanged)
            {
                ApproovService.LogTrace("ApproovRequestProcessor SDK configuration changed, clearing pin cache and fetching latest config");
                ApproovBridge.ClearCertificateCache();
                ApproovService.FetchConfig();
            }

            if (approovResult.isForceApplyPins)
            {
                ApproovService.LogTrace("ApproovRequestProcessor ForceApplyPins requested by SDK");
                throw new NetworkingErrorException("ApproovRequestProcessor Forced pin update required");
            }

            ApproovTokenFetchStatus status = approovResult.status;
            Console.WriteLine("ApproovRequestProcessor FetchToken: " + urlWithBaseAddress + " " + ApproovService.ApproovTokenFetchStatusToString(status));
            if (status == ApproovTokenFetchStatus.Success)
            {
                if (!string.IsNullOrEmpty(approovResult.token))
                {
                    setHeader(ApproovService.GetTokenHeader(), ApproovService.GetTokenPrefix() + approovResult.token);
                    ApproovService.LogTrace("ApproovRequestProcessor Added token header " + ApproovService.GetTokenHeader());
                }
                else
                {
                    ApproovService.LogTrace("ApproovRequestProcessor Token fetch succeeded without a token payload");
                }

                string traceIDHeader = ApproovService.GetApproovTraceIDHeader();
                if (!string.IsNullOrWhiteSpace(traceIDHeader) && !string.IsNullOrWhiteSpace(approovResult.traceID))
                {
                    setHeader(traceIDHeader, approovResult.traceID);
                    ApproovService.LogTrace("ApproovRequestProcessor Added trace header " + traceIDHeader);
                }
            }
            else if (status == ApproovTokenFetchStatus.NoNetwork || status == ApproovTokenFetchStatus.PoorNetwork || status == ApproovTokenFetchStatus.MITMDetected)
            {
                ApproovService.LogTrace("ApproovRequestProcessor Token fetch hit transient status " + ApproovService.ApproovTokenFetchStatusToString(status) + ", proceedOnNetworkFailure=" + ApproovService.GetProceedOnNetworkFailure());
                if (!ApproovService.GetProceedOnNetworkFailure())
                {
                    throw new NetworkingErrorException("ApproovRequestProcessor Retry attempt needed. " + approovResult.loggableToken, true);
                }
            }
            else if (status != ApproovTokenFetchStatus.UnknownURL &&
                     status != ApproovTokenFetchStatus.UnprotectedURL &&
                     status != ApproovTokenFetchStatus.NoApproovService)
            {
                throw new PermanentException("ApproovRequestProcessor Unknown Approov token fetch result " + ApproovService.ApproovTokenFetchStatusToString(status));
            }

            if (status != ApproovTokenFetchStatus.Success && status != ApproovTokenFetchStatus.UnprotectedURL)
            {
                ApproovService.LogTrace("ApproovRequestProcessor Skipping substitutions because token status was " + ApproovService.ApproovTokenFetchStatusToString(status));
                return;
            }

            foreach (KeyValuePair<string, string> substitutionHeader in ApproovService.GetSubstitutionHeaders())
            {
                string headerValue = getHeader(substitutionHeader.Key);
                if (headerValue == null)
                {
                    continue;
                }

                string prefix = substitutionHeader.Value ?? string.Empty;
                if (!headerValue.StartsWith(prefix) || headerValue.Length <= prefix.Length)
                {
                    continue;
                }

                string secureStringKey = headerValue.Substring(prefix.Length);
                ApproovService.LogTrace("ApproovRequestProcessor Substituting header " + substitutionHeader.Key + " using secure string key");
                ApproovTokenFetchResult secureStringResult = ApproovBridge.FetchSecureStringAndWait(secureStringKey, null);
                if (secureStringResult.status == ApproovTokenFetchStatus.Success)
                {
                    if (secureStringResult.secureString == null)
                    {
                        throw new ApproovException("ApproovRequestProcessor Header substitution returned null secure string");
                    }

                    setHeader(substitutionHeader.Key, prefix + secureStringResult.secureString);
                    ApproovService.LogTrace("ApproovRequestProcessor Substituted header " + substitutionHeader.Key);
                }
                else if (secureStringResult.status == ApproovTokenFetchStatus.Rejected)
                {
                    throw new RejectionException("ApproovRequestProcessor Header substitution rejected", secureStringResult.ARC, secureStringResult.rejectionReasons);
                }
                else if (secureStringResult.status == ApproovTokenFetchStatus.NoNetwork ||
                         secureStringResult.status == ApproovTokenFetchStatus.PoorNetwork ||
                         secureStringResult.status == ApproovTokenFetchStatus.MITMDetected)
                {
                    if (!ApproovService.GetProceedOnNetworkFailure())
                    {
                        throw new NetworkingErrorException("ApproovRequestProcessor Header substitution retry needed");
                    }
                }
                else if (secureStringResult.status != ApproovTokenFetchStatus.UnknownKey)
                {
                    throw new PermanentException("ApproovRequestProcessor Header substitution: " + ApproovService.ApproovTokenFetchStatusToString(secureStringResult.status));
                }
            }

            string updatedUrl = urlWithBaseAddress;
            foreach (string queryParameter in ApproovService.GetSubstitutionQueryParams())
            {
                Regex regex = new Regex("([?&]" + Regex.Escape(queryParameter) + "=)([^&#]*)", RegexOptions.ECMAScript);
                if (!regex.IsMatch(updatedUrl))
                {
                    continue;
                }

                ApproovService.LogTrace("ApproovRequestProcessor Substituting query parameter " + queryParameter);
                updatedUrl = regex.Replace(updatedUrl, match =>
                {
                    string secureStringKey = match.Groups[2].Value;
                    ApproovTokenFetchResult secureStringResult = ApproovBridge.FetchSecureStringAndWait(secureStringKey, null);
                    if (secureStringResult.status == ApproovTokenFetchStatus.Success)
                    {
                        if (secureStringResult.secureString == null)
                        {
                            throw new ApproovException("ApproovRequestProcessor Query substitution returned null secure string");
                        }

                        return match.Groups[1].Value + Uri.EscapeDataString(secureStringResult.secureString);
                    }

                    if (secureStringResult.status == ApproovTokenFetchStatus.Rejected)
                    {
                        throw new RejectionException("ApproovRequestProcessor Query substitution rejected", secureStringResult.ARC, secureStringResult.rejectionReasons);
                    }

                    if (secureStringResult.status == ApproovTokenFetchStatus.NoNetwork ||
                        secureStringResult.status == ApproovTokenFetchStatus.PoorNetwork ||
                        secureStringResult.status == ApproovTokenFetchStatus.MITMDetected)
                    {
                        if (!ApproovService.GetProceedOnNetworkFailure())
                        {
                            throw new NetworkingErrorException("ApproovRequestProcessor Query substitution retry needed");
                        }
                    }
                    else if (secureStringResult.status != ApproovTokenFetchStatus.UnknownKey)
                    {
                        throw new PermanentException("ApproovRequestProcessor Query substitution: " + ApproovService.ApproovTokenFetchStatusToString(secureStringResult.status));
                    }

                    return match.Value;
                });
            }

            if (!string.Equals(updatedUrl, urlWithBaseAddress, StringComparison.Ordinal))
            {
                setUri(new Uri(updatedUrl));
                ApproovService.LogTrace("ApproovRequestProcessor Updated request URL after query substitutions");
            }

            ApproovService.LogTrace("ApproovRequestProcessor Apply complete");
        }

        private static string GetHttpHeader(HttpRequestMessage request, string header)
        {
            if (request.Headers.TryGetValues(header, out IEnumerable<string> values))
            {
                foreach (string value in values)
                {
                    return value;
                }
            }

            if (request.Content != null && request.Content.Headers.TryGetValues(header, out values))
            {
                foreach (string value in values)
                {
                    return value;
                }
            }

            return null;
        }

        private static void SetHttpHeader(HttpRequestMessage request, string header, string value)
        {
            request.Headers.Remove(header);
            request.Content?.Headers.Remove(header);

            if (!request.Headers.TryAddWithoutValidation(header, value) && request.Content != null)
            {
                request.Content.Headers.TryAddWithoutValidation(header, value);
            }
        }
    }
}
