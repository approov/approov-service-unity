using System;

namespace Approov
{
    /// <summary>
    /// Extensibility point for customizing how the service layer handles fetch results, request
    /// mutation, post-processing, and pinning decisions.
    /// </summary>
    public abstract class ApproovServiceMutator
    {
        private sealed class DefaultApproovServiceMutator : ApproovServiceMutator
        {
            public override string ToString()
            {
                return "ApproovServiceMutator.Default";
            }
        }

        /// <summary>
        /// Default policy used when the caller does not install a custom mutator.
        /// </summary>
        public static ApproovServiceMutator Default { get; } = new DefaultApproovServiceMutator();

        /// <summary>
        /// Handles the result of <see cref="ApproovService.Precheck"/>.
        /// </summary>
        public virtual void HandlePrecheckResult(ApproovTokenFetchResult approovResult)
        {
            switch (approovResult.status)
            {
                case ApproovTokenFetchStatus.Success:
                case ApproovTokenFetchStatus.UnknownKey:
                    return;
                case ApproovTokenFetchStatus.Rejected:
                    throw new RejectionException("Precheck rejected", approovResult.ARC, approovResult.rejectionReasons);
                case ApproovTokenFetchStatus.NoNetwork:
                case ApproovTokenFetchStatus.PoorNetwork:
                case ApproovTokenFetchStatus.MITMDetected:
                    throw new NetworkingErrorException("Precheck: network issue, retry needed");
                default:
                    throw new PermanentException("Precheck: " + ApproovService.ApproovTokenFetchStatusToString(approovResult.status));
            }
        }

        /// <summary>
        /// Handles the result of an explicit token fetch.
        /// </summary>
        public virtual void HandleFetchTokenResult(ApproovTokenFetchResult approovResult)
        {
            switch (approovResult.status)
            {
                case ApproovTokenFetchStatus.Success:
                    return;
                case ApproovTokenFetchStatus.NoNetwork:
                case ApproovTokenFetchStatus.PoorNetwork:
                case ApproovTokenFetchStatus.MITMDetected:
                    throw new NetworkingErrorException("FetchToken: networking error, retry needed");
                default:
                    throw new PermanentException("FetchToken: " + ApproovService.ApproovTokenFetchStatusToString(approovResult.status));
            }
        }

        /// <summary>
        /// Handles the result of a secure-string lookup or definition operation.
        /// </summary>
        public virtual void HandleFetchSecureStringResult(ApproovTokenFetchResult approovResult, string operation, string key)
        {
            switch (approovResult.status)
            {
                case ApproovTokenFetchStatus.Success:
                    return;
                case ApproovTokenFetchStatus.Disabled:
                    throw new ConfigurationFailureException("FetchSecureString: secure string feature is disabled");
                case ApproovTokenFetchStatus.UnknownKey:
                    throw new ConfigurationFailureException("FetchSecureString: secure string unknown key");
                case ApproovTokenFetchStatus.Rejected:
                    throw new RejectionException("FetchSecureString rejected", approovResult.ARC, approovResult.rejectionReasons);
                case ApproovTokenFetchStatus.NoNetwork:
                case ApproovTokenFetchStatus.PoorNetwork:
                case ApproovTokenFetchStatus.MITMDetected:
                    throw new NetworkingErrorException("FetchSecureString: network issue, retry needed");
                default:
                    throw new PermanentException("FetchSecureString: " + ApproovService.ApproovTokenFetchStatusToString(approovResult.status));
            }
        }

        /// <summary>
        /// Handles the result of an explicit custom JWT fetch.
        /// </summary>
        public virtual void HandleFetchCustomJwtResult(ApproovTokenFetchResult approovResult)
        {
            switch (approovResult.status)
            {
                case ApproovTokenFetchStatus.Success:
                    return;
                case ApproovTokenFetchStatus.Disabled:
                    throw new ConfigurationFailureException("FetchCustomJWT: feature not enabled");
                case ApproovTokenFetchStatus.Rejected:
                    throw new RejectionException("FetchCustomJWT rejected", approovResult.ARC, approovResult.rejectionReasons);
                case ApproovTokenFetchStatus.NoNetwork:
                case ApproovTokenFetchStatus.PoorNetwork:
                case ApproovTokenFetchStatus.MITMDetected:
                    throw new NetworkingErrorException("FetchCustomJWT: network issue, retry needed");
                default:
                    throw new PermanentException("FetchCustomJWT: " + ApproovService.ApproovTokenFetchStatusToString(approovResult.status));
            }
        }

        /// <summary>
        /// Determines whether the request should enter the Approov processing pipeline at all.
        /// </summary>
        public virtual bool ShouldProcessRequest(ApproovRequestContext request)
        {
            if (request == null)
            {
                throw new ApproovException("ShouldProcessRequest was passed a null request context");
            }

            string url = request.Uri?.AbsoluteUri;
            return !string.IsNullOrWhiteSpace(url) && !ApproovService.CheckURLIsExcluded(url);
        }

        /// <summary>
        /// Handles the token-fetch result produced during intercepted request processing and decides
        /// whether the pipeline should continue mutating the request.
        /// </summary>
        public virtual bool HandleInterceptorFetchTokenResult(ApproovRequestContext request, ApproovTokenFetchResult approovResult)
        {
            switch (approovResult.status)
            {
                case ApproovTokenFetchStatus.Success:
                    return true;
                case ApproovTokenFetchStatus.NoNetwork:
                case ApproovTokenFetchStatus.PoorNetwork:
                case ApproovTokenFetchStatus.MITMDetected:
                    if (ApproovService.GetUseApproovStatusIfNoToken())
                    {
                        return true;
                    }

                    if (!ApproovService.GetProceedOnNetworkFailure())
                    {
                        throw new NetworkingErrorException(
                            "Approov token fetch for " + request.Uri + ": " + ApproovService.ApproovTokenFetchStatusToString(approovResult.status),
                            true);
                    }

                    // Returning false means "send the original request unchanged".
                    return false;
                case ApproovTokenFetchStatus.NoApproovService:
                case ApproovTokenFetchStatus.UnknownURL:
                case ApproovTokenFetchStatus.UnprotectedURL:
                    return false;
                default:
                    throw new PermanentException(
                        "Approov token fetch for " + request.Uri + ": " + ApproovService.ApproovTokenFetchStatusToString(approovResult.status));
            }
        }

        /// <summary>
        /// Handles the result of a secure-string substitution for a request header.
        /// </summary>
        public virtual bool HandleHeaderSubstitutionResult(ApproovRequestContext request, ApproovTokenFetchResult approovResult, string header)
        {
            switch (approovResult.status)
            {
                case ApproovTokenFetchStatus.Success:
                    return true;
                case ApproovTokenFetchStatus.Rejected:
                    throw new RejectionException("Header substitution rejected for " + header, approovResult.ARC, approovResult.rejectionReasons);
                case ApproovTokenFetchStatus.NoNetwork:
                case ApproovTokenFetchStatus.PoorNetwork:
                case ApproovTokenFetchStatus.MITMDetected:
                    if (!ApproovService.GetProceedOnNetworkFailure())
                    {
                        throw new NetworkingErrorException("Header substitution for " + header + ": retry needed");
                    }

                    return false;
                case ApproovTokenFetchStatus.UnknownKey:
                    return false;
                default:
                    throw new PermanentException("Header substitution for " + header + ": " + ApproovService.ApproovTokenFetchStatusToString(approovResult.status));
            }
        }

        /// <summary>
        /// Handles the result of a secure-string substitution for a query parameter.
        /// </summary>
        public virtual bool HandleQueryParamSubstitutionResult(ApproovRequestContext request, ApproovTokenFetchResult approovResult, string queryKey)
        {
            switch (approovResult.status)
            {
                case ApproovTokenFetchStatus.Success:
                    return true;
                case ApproovTokenFetchStatus.Rejected:
                    throw new RejectionException("Query substitution rejected for " + queryKey, approovResult.ARC, approovResult.rejectionReasons);
                case ApproovTokenFetchStatus.NoNetwork:
                case ApproovTokenFetchStatus.PoorNetwork:
                case ApproovTokenFetchStatus.MITMDetected:
                    if (!ApproovService.GetProceedOnNetworkFailure())
                    {
                        throw new NetworkingErrorException("Query substitution for " + queryKey + ": retry needed");
                    }

                    return false;
                case ApproovTokenFetchStatus.UnknownKey:
                    return false;
                default:
                    throw new PermanentException("Query substitution for " + queryKey + ": " + ApproovService.ApproovTokenFetchStatusToString(approovResult.status));
            }
        }

        /// <summary>
        /// Called after the service layer has finished mutating a request.
        /// </summary>
        public virtual void HandleProcessedRequest(ApproovRequestContext request, ApproovRequestMutations changes)
        {
        }

        /// <summary>
        /// Determines whether Approov pinning should run for the request.
        /// </summary>
        public virtual bool ShouldProcessPinning(ApproovRequestContext request)
        {
            return true;
        }
    }
}
