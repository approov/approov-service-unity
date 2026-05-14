using System;
using System.Collections.Generic;

namespace Approov
{
    /// <summary>
    /// Describes the changes applied to a request by the Approov service layer before the mutator's
    /// post-processing hook runs.
    /// </summary>
    public sealed class ApproovRequestMutations
    {
        /// <summary>
        /// Header name that received the Approov token, if any.
        /// </summary>
        public string TokenHeaderKey { get; set; }

        /// <summary>
        /// Header name that received the Approov trace ID, if any.
        /// </summary>
        public string TraceIDHeaderKey { get; set; }

        /// <summary>
        /// Headers whose values were replaced by secure-string substitution.
        /// </summary>
        public List<string> SubstitutionHeaderKeys { get; set; }

        /// <summary>
        /// Original absolute URL before any query-parameter substitution rewrote the request URI.
        /// </summary>
        public string OriginalUrl { get; set; }

        /// <summary>
        /// Query parameter names whose values were replaced by secure-string substitution.
        /// </summary>
        public List<string> SubstitutionQueryParamKeys { get; set; }

        public void SetSubstitutionQueryParamResults(string originalUrl, List<string> substitutionQueryParamKeys)
        {
            OriginalUrl = originalUrl;
            SubstitutionQueryParamKeys = substitutionQueryParamKeys ?? new List<string>();
        }
    }
}
