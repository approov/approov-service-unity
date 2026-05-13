using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Approov
{
    internal static class ApproovRequestProcessor
    {
        private static readonly object NativeRequestLock = new();

        public static void ApplyToUnityWebRequest(UnityWebRequest request)
        {
            Apply(ApproovRequestContext.Create(request));
        }

        public static IEnumerator ApplyToUnityWebRequestAsync(UnityWebRequest request)
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
            Dictionary<string, string> capturedHeaders = CaptureUnityRequestHeaders(request, mutator);
            Dictionary<string, string> headersToSet = new(StringComparer.OrdinalIgnoreCase);
            Uri updatedUri = null;
            ApproovRequestContext backgroundContext = ApproovRequestContext.CreateMutableSnapshot(
                request,
                capturedHeaders,
                (header, value) => headersToSet[header] = value,
                uri => updatedUri = uri);

            if (!mutator.ShouldProcessRequest(backgroundContext))
            {
                ApproovService.LogTrace("ApproovRequestProcessor request skipped by mutator");
                yield break;
            }

            string requestUrl = backgroundContext.Uri?.AbsoluteUri;
            ApproovService.LogTrace("ApproovRequestProcessor Apply start url=" + requestUrl);

            Task<ApproovTokenFetchResult> tokenFetchTask = Task.Run(() =>
            {
                lock (NativeRequestLock)
                {
                    return FetchTokenWithNativeState(backgroundContext, requestUrl);
                }
            });

            while (!tokenFetchTask.IsCompleted)
            {
                yield return null;
            }

            ApproovTokenFetchResult approovResult = GetCompletedTaskResult(tokenFetchTask);
            ApproovService.LogTrace(
                "ApproovRequestProcessor FetchToken result url=" + requestUrl + " | " +
                ApproovService.DescribeFetchResult(approovResult));
            if (!mutator.HandleInterceptorFetchTokenResult(backgroundContext, approovResult))
            {
                ApproovService.LogTrace("ApproovRequestProcessor mutator allowed request to proceed without Approov changes");
                yield break;
            }

            ApproovRequestMutations changes = new();
            ApplyTokenAndTraceHeaders(backgroundContext, approovResult, changes);
            yield return ApplyHeaderSubstitutionsAsync(backgroundContext, mutator, changes);
            yield return ApplyQuerySubstitutionsAsync(backgroundContext, mutator, changes);

            ApproovService.LogTrace(
                "ApproovRequestProcessor invoking mutator " + mutator +
                " with tokenHeader=" + (changes.TokenHeaderKey ?? "none") +
                " traceHeader=" + (changes.TraceIDHeaderKey ?? "none"));
            mutator.HandleProcessedRequest(backgroundContext, changes);
            ApproovService.LogTrace("ApproovRequestProcessor Apply complete");

            foreach (KeyValuePair<string, string> header in headersToSet)
            {
                request.SetRequestHeader(header.Key, header.Value);
            }

            if (updatedUri != null && !string.Equals(updatedUri.AbsoluteUri, request.url, StringComparison.Ordinal))
            {
                request.uri = updatedUri;
            }
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

            // The native SDK keeps token-binding input as process-wide state. Serialize the
            // full native request mutation pass so concurrent requests cannot bind or substitute
            // values with another request's SDK state.
            lock (NativeRequestLock)
            {
                ApplyWithNativeState(request, mutator, requestUrl);
            }
        }

        private static void ApplyWithNativeState(ApproovRequestContext request, ApproovServiceMutator mutator, string requestUrl)
        {
            ApproovTokenFetchResult approovResult = FetchTokenWithNativeState(request, requestUrl);
            ApproovService.LogTrace(
                "ApproovRequestProcessor FetchToken result url=" + requestUrl + " | " +
                ApproovService.DescribeFetchResult(approovResult));
            if (!mutator.HandleInterceptorFetchTokenResult(request, approovResult))
            {
                ApproovService.LogTrace("ApproovRequestProcessor mutator allowed request to proceed without Approov changes");
                return;
            }

            ApproovRequestMutations changes = new();
            ApplyTokenAndTraceHeaders(request, approovResult, changes);
            ApplyHeaderSubstitutions(request, mutator, changes);
            ApplyQuerySubstitutions(request, mutator, changes);

            ApproovService.LogTrace(
                "ApproovRequestProcessor invoking mutator " + mutator +
                " with tokenHeader=" + (changes.TokenHeaderKey ?? "none") +
                " traceHeader=" + (changes.TraceIDHeaderKey ?? "none"));
            mutator.HandleProcessedRequest(request, changes);
            ApproovService.LogTrace("ApproovRequestProcessor Apply complete");
        }

        private static ApproovTokenFetchResult FetchTokenWithNativeState(ApproovRequestContext request, string requestUrl)
        {
            string bindingHeader = ApproovService.GetBindingHeader();
            if (!string.IsNullOrWhiteSpace(bindingHeader))
            {
                string bindingValue = request.GetHeader(bindingHeader);
                if (bindingValue == null)
                {
                    throw new ConfigurationFailureException("ApproovRequestProcessor Missing token binding header: " + bindingHeader);
                }

                ApproovService.SetDataHashInToken(bindingValue);
                ApproovService.LogTrace("ApproovRequestProcessor bound token to header " + bindingHeader);
            }

            ApproovTokenFetchResult approovResult = ApproovBridge.FetchApproovTokenAndWait(requestUrl);
            if (approovResult.isConfigChanged)
            {
                // A token fetch can also deliver refreshed dynamic pinning state. Clear the transport-side
                // certificate cache and mark the SDK config as consumed so later fetches see a clean state.
                ApproovService.LogTrace("ApproovRequestProcessor SDK configuration changed, refreshing pin state");
                ApproovBridge.ClearCertificateCache();
                ApproovService.FetchConfig();
            }

            if (approovResult.isForceApplyPins)
            {
                throw new NetworkingErrorException("ApproovRequestProcessor Forced pin update required", true);
            }

            return approovResult;
        }

        private static void ApplyTokenAndTraceHeaders(ApproovRequestContext request, ApproovTokenFetchResult approovResult, ApproovRequestMutations changes)
        {
            string tokenHeaderKey = ApproovService.GetTokenHeader();
            string tokenValue = approovResult.token;
            if (string.IsNullOrEmpty(tokenValue) && ApproovService.GetUseApproovStatusIfNoToken())
            {
                tokenValue = ApproovService.ApproovTokenFetchStatusToString(approovResult.status);
            }

            if (!string.IsNullOrWhiteSpace(tokenValue))
            {
                request.SetHeader(tokenHeaderKey, ApproovService.GetTokenPrefix() + tokenValue);
                changes.TokenHeaderKey = tokenHeaderKey;
                ApproovService.LogTrace("ApproovRequestProcessor added token header " + tokenHeaderKey);
            }

            string traceHeader = ApproovService.GetApproovTraceIDHeader();
            if (!string.IsNullOrWhiteSpace(traceHeader) && !string.IsNullOrWhiteSpace(approovResult.traceID))
            {
                request.SetHeader(traceHeader, approovResult.traceID);
                changes.TraceIDHeaderKey = traceHeader;
                ApproovService.LogTrace("ApproovRequestProcessor added trace header " + traceHeader);
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
                ApproovService.LogTrace("ApproovRequestProcessor substituted header " + substitutionHeader.Key);
            }

            changes.SubstitutionHeaderKeys = updatedHeaders ?? new List<string>();
        }

        private static IEnumerator ApplyHeaderSubstitutionsAsync(ApproovRequestContext request, ApproovServiceMutator mutator, ApproovRequestMutations changes)
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
                Task<ApproovTokenFetchResult> secureStringTask = Task.Run(() =>
                {
                    lock (NativeRequestLock)
                    {
                        return ApproovBridge.FetchSecureStringAndWait(secureStringKey, null);
                    }
                });

                while (!secureStringTask.IsCompleted)
                {
                    yield return null;
                }

                ApproovTokenFetchResult secureStringResult = GetCompletedTaskResult(secureStringTask);
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
                ApproovService.LogTrace("ApproovRequestProcessor substituted header " + substitutionHeader.Key);
            }

            changes.SubstitutionHeaderKeys = updatedHeaders ?? new List<string>();
        }

        private static void ApplyQuerySubstitutions(ApproovRequestContext request, ApproovServiceMutator mutator, ApproovRequestMutations changes)
        {
            string originalUrl = request.Uri?.AbsoluteUri ?? string.Empty;
            string updatedUrl = originalUrl;
            List<string> updatedKeys = null;

            foreach (string queryParameter in ApproovService.GetSubstitutionQueryParams())
            {
                // Rewrite only the configured query parameter values so other query structure is preserved.
                Regex regex = new("([?&]" + Regex.Escape(queryParameter) + "=)([^&#]*)", RegexOptions.ECMAScript);
                if (!regex.IsMatch(updatedUrl))
                {
                    continue;
                }

                updatedUrl = regex.Replace(updatedUrl, match =>
                {
                    string secureStringKey = DecodeSubstitutionQueryParameterValue(match.Groups[2].Value);
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
                    ApproovService.LogTrace("ApproovRequestProcessor substituted query parameter " + queryParameter);
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

        private static IEnumerator ApplyQuerySubstitutionsAsync(ApproovRequestContext request, ApproovServiceMutator mutator, ApproovRequestMutations changes)
        {
            string originalUrl = request.Uri?.AbsoluteUri ?? string.Empty;
            string updatedUrl = originalUrl;
            List<string> updatedKeys = null;

            foreach (string queryParameter in ApproovService.GetSubstitutionQueryParams())
            {
                Regex regex = new("([?&]" + Regex.Escape(queryParameter) + "=)([^&#]*)", RegexOptions.ECMAScript);
                MatchCollection matches = regex.Matches(updatedUrl);
                if (matches.Count == 0)
                {
                    continue;
                }

                StringBuilder builder = new();
                int lastIndex = 0;
                foreach (Match match in matches)
                {
                    builder.Append(updatedUrl, lastIndex, match.Index - lastIndex);
                    string replacement = match.Value;
                    string secureStringKey = DecodeSubstitutionQueryParameterValue(match.Groups[2].Value);
                    Task<ApproovTokenFetchResult> secureStringTask = Task.Run(() =>
                    {
                        lock (NativeRequestLock)
                        {
                            return ApproovBridge.FetchSecureStringAndWait(secureStringKey, null);
                        }
                    });

                    while (!secureStringTask.IsCompleted)
                    {
                        yield return null;
                    }

                    ApproovTokenFetchResult secureStringResult = GetCompletedTaskResult(secureStringTask);
                    if (mutator.HandleQueryParamSubstitutionResult(request, secureStringResult, queryParameter))
                    {
                        if (secureStringResult.secureString == null)
                        {
                            throw new ApproovException("ApproovRequestProcessor Query substitution returned null secure string");
                        }

                        updatedKeys ??= new List<string>();
                        updatedKeys.Add(queryParameter);
                        ApproovService.LogTrace("ApproovRequestProcessor substituted query parameter " + queryParameter);
                        replacement = match.Groups[1].Value + Uri.EscapeDataString(secureStringResult.secureString);
                    }

                    builder.Append(replacement);
                    lastIndex = match.Index + match.Length;
                }

                builder.Append(updatedUrl, lastIndex, updatedUrl.Length - lastIndex);
                updatedUrl = builder.ToString();
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

        internal static string DecodeSubstitutionQueryParameterValue(string encodedValue)
        {
            if (encodedValue == null)
            {
                return string.Empty;
            }

            return Uri.UnescapeDataString(encodedValue);
        }

        private static T GetCompletedTaskResult<T>(Task<T> task)
        {
            if (task.IsFaulted)
            {
                throw task.Exception.InnerException ?? task.Exception;
            }

            if (task.IsCanceled)
            {
                throw new TaskCanceledException(task);
            }

            return task.Result;
        }

        private static Dictionary<string, string> CaptureUnityRequestHeaders(UnityWebRequest request, ApproovServiceMutator mutator)
        {
            HashSet<string> headerNames = new(StringComparer.OrdinalIgnoreCase)
            {
                "Accept",
                "Api-Key",
                "Authorization",
                "Content-Digest",
                "Content-Type",
                "Signature",
                "Signature-Input",
            };

            // UnityWebRequest cannot enumerate caller-set headers, so capture the headers
            // the built-in mutators and samples need before moving native work off-thread.
            headerNames.Add(ApproovService.GetTokenHeader());
            headerNames.Add(ApproovService.GetApproovTraceIDHeader());
            headerNames.Add(ApproovService.GetBindingHeader());

            foreach (KeyValuePair<string, string> substitutionHeader in ApproovService.GetSubstitutionHeaders())
            {
                headerNames.Add(substitutionHeader.Key);
            }

            mutator.AddUnityRequestHeadersToCapture(headerNames);

            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
            CaptureUnityHeaders(request, headers, headerNames);
            for (int i = 0; i < 4; i++)
            {
                int headerNameCount = headerNames.Count;
                ApproovRequestContext capturedContext = ApproovRequestContext.CreateMutableSnapshot(request, headers, null, null);
                mutator.AddUnityRequestHeadersToCapture(headerNames, capturedContext);
                if (headerNames.Count == headerNameCount)
                {
                    break;
                }

                CaptureUnityHeaders(request, headers, headerNames);
            }

            return headers;
        }

        private static void CaptureUnityHeaders(UnityWebRequest request, Dictionary<string, string> headers, HashSet<string> headerNames)
        {
            foreach (string header in headerNames)
            {
                CaptureUnityHeader(request, headers, header);
            }
        }

        private static void CaptureUnityHeader(UnityWebRequest request, Dictionary<string, string> headers, string header)
        {
            if (string.IsNullOrWhiteSpace(header) || headers.ContainsKey(header))
            {
                return;
            }

            string value = request.GetRequestHeader(header);
            if (value != null)
            {
                headers[header] = value;
            }
        }
    }
}
