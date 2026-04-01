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
            Apply(ApproovRequestContext.Create(request));
        }

        public static void ApplyToHttpRequestMessage(HttpRequestMessage request)
        {
            Apply(ApproovRequestContext.Create(request));
        }

        private static void Apply(ApproovRequestContext request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!ApproovService.IsSDKInitialized())
            {
                throw new InitializationFailureException("ApproovService Error: SDK not initialized");
            }

            ApproovServiceMutator mutator = ApproovService.GetServiceMutator();
            if (!mutator.ShouldProcessRequest(request))
            {
                ApproovService.LogTrace("ApproovRequestProcessor request skipped by mutator");
                return;
            }

            string requestUrl = request.Uri?.AbsoluteUri;
            ApproovService.LogTrace("ApproovRequestProcessor Apply start url=" + requestUrl);

            string bindingHeader = ApproovService.GetBindingHeader();
            if (!string.IsNullOrWhiteSpace(bindingHeader))
            {
                string bindingValue = request.GetHeader(bindingHeader);
                if (bindingValue == null)
                {
                    throw new ConfigurationFailureException("ApproovRequestProcessor Missing token binding header: " + bindingHeader);
                }

                ApproovService.SetDataHashInToken(bindingValue);
            }

            ApproovTokenFetchResult approovResult = ApproovBridge.FetchApproovTokenAndWait(requestUrl);
            if (approovResult.isConfigChanged)
            {
                ApproovService.LogTrace("ApproovRequestProcessor SDK configuration changed, refreshing pin state");
                ApproovBridge.ClearCertificateCache();
                ApproovService.FetchConfig();
            }

            if (approovResult.isForceApplyPins)
            {
                throw new NetworkingErrorException("ApproovRequestProcessor Forced pin update required", true);
            }

            Console.WriteLine("ApproovRequestProcessor FetchToken: " + requestUrl + " " + ApproovService.ApproovTokenFetchStatusToString(approovResult.status));
            if (!mutator.HandleInterceptorFetchTokenResult(request, approovResult))
            {
                ApproovService.LogTrace("ApproovRequestProcessor mutator allowed request to proceed without Approov changes");
                return;
            }

            ApproovRequestMutations changes = new();
            ApplyTokenAndTraceHeaders(request, approovResult, changes);
            ApplyHeaderSubstitutions(request, mutator, changes);
            ApplyQuerySubstitutions(request, mutator, changes);

            mutator.HandleProcessedRequest(request, changes);
            ApproovService.LogTrace("ApproovRequestProcessor Apply complete");
        }

        private static void ApplyTokenAndTraceHeaders(ApproovRequestContext request, ApproovTokenFetchResult approovResult, ApproovRequestMutations changes)
        {
            string tokenHeaderKey = ApproovService.GetTokenHeader();
            string tokenValue = approovResult.token;
            if (string.IsNullOrEmpty(tokenValue) && ApproovService.GetUseApproovStatusIfNoToken())
            {
                tokenValue = ApproovService.ApproovTokenFetchStatusToString(approovResult.status);
            }

            if (tokenValue != null)
            {
                request.SetHeader(tokenHeaderKey, ApproovService.GetTokenPrefix() + tokenValue);
                changes.TokenHeaderKey = tokenHeaderKey;
            }

            string traceHeader = ApproovService.GetApproovTraceIDHeader();
            if (!string.IsNullOrWhiteSpace(traceHeader) && !string.IsNullOrWhiteSpace(approovResult.traceID))
            {
                request.SetHeader(traceHeader, approovResult.traceID);
                changes.TraceIDHeaderKey = traceHeader;
            }
        }

        private static void ApplyHeaderSubstitutions(ApproovRequestContext request, ApproovServiceMutator mutator, ApproovRequestMutations changes)
        {
            Dictionary<string, string> substitutionHeaders = ApproovService.GetSubstitutionHeaders();
            List<string> updatedHeaders = null;
            foreach (System.Collections.Generic.KeyValuePair<string, string> substitutionHeader in substitutionHeaders)
            {
                string headerValue = request.GetHeader(substitutionHeader.Key);
                if (headerValue == null)
                {
                    continue;
                }

                string prefix = substitutionHeader.Value ?? string.Empty;
                if (!headerValue.StartsWith(prefix, StringComparison.Ordinal) || headerValue.Length <= prefix.Length)
                {
                    continue;
                }

                string secureStringKey = headerValue.Substring(prefix.Length);
                ApproovTokenFetchResult secureStringResult = ApproovBridge.FetchSecureStringAndWait(secureStringKey, null);
                if (!mutator.HandleHeaderSubstitutionResult(request, secureStringResult, substitutionHeader.Key))
                {
                    continue;
                }

                if (secureStringResult.secureString == null)
                {
                    throw new ApproovException("ApproovRequestProcessor Header substitution returned null secure string");
                }

                request.SetHeader(substitutionHeader.Key, prefix + secureStringResult.secureString);
                updatedHeaders ??= new List<string>();
                updatedHeaders.Add(substitutionHeader.Key);
            }

            changes.SubstitutionHeaderKeys = updatedHeaders ?? new List<string>();
        }

        private static void ApplyQuerySubstitutions(ApproovRequestContext request, ApproovServiceMutator mutator, ApproovRequestMutations changes)
        {
            string originalUrl = request.Uri.AbsoluteUri;
            string updatedUrl = originalUrl;
            List<string> updatedKeys = null;

            foreach (string queryParameter in ApproovService.GetSubstitutionQueryParams())
            {
                Regex regex = new("([?&]" + Regex.Escape(queryParameter) + "=)([^&#]*)", RegexOptions.ECMAScript);
                if (!regex.IsMatch(updatedUrl))
                {
                    continue;
                }

                updatedUrl = regex.Replace(updatedUrl, match =>
                {
                    string secureStringKey = match.Groups[2].Value;
                    ApproovTokenFetchResult secureStringResult = ApproovBridge.FetchSecureStringAndWait(secureStringKey, null);
                    if (!mutator.HandleQueryParamSubstitutionResult(request, secureStringResult, queryParameter))
                    {
                        return match.Value;
                    }

                    if (secureStringResult.secureString == null)
                    {
                        throw new ApproovException("ApproovRequestProcessor Query substitution returned null secure string");
                    }

                    updatedKeys ??= new List<string>();
                    updatedKeys.Add(queryParameter);
                    return match.Groups[1].Value + Uri.EscapeDataString(secureStringResult.secureString);
                });
            }

            if (!string.Equals(originalUrl, updatedUrl, StringComparison.Ordinal))
            {
                request.Uri = new Uri(updatedUrl);
                changes.SetSubstitutionQueryParamResults(originalUrl, updatedKeys ?? new List<string>());
            }
            else
            {
                changes.SubstitutionQueryParamKeys = updatedKeys ?? new List<string>();
            }
        }
    }
}
