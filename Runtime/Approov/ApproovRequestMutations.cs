using System;
using System.Collections.Generic;

namespace Approov
{
    public sealed class ApproovRequestMutations
    {
        public string TokenHeaderKey { get; set; }

        public string TraceIDHeaderKey { get; set; }

        public List<string> SubstitutionHeaderKeys { get; set; }

        public string OriginalUrl { get; set; }

        public List<string> SubstitutionQueryParamKeys { get; set; }

        public void SetSubstitutionQueryParamResults(string originalUrl, List<string> substitutionQueryParamKeys)
        {
            OriginalUrl = originalUrl;
            SubstitutionQueryParamKeys = substitutionQueryParamKeys ?? new List<string>();
        }
    }
}
