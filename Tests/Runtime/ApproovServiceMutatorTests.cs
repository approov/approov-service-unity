using System.Net.Http;
using NUnit.Framework;

namespace Approov.Tests
{
    public class ApproovServiceMutatorTests
    {
        [SetUp]
        public void SetUp()
        {
            ApproovService.SetProceedOnNetworkFailure(false);
            ApproovService.SetUseApproovStatusIfNoToken(false);
            ApproovService.SetServiceMutator(null);
        }

        [Test]
        public void HandleInterceptorFetchTokenResult_ReturnsFalseForNoApproovService()
        {
            ApproovRequestContext context = ApproovRequestContext.Create(new HttpRequestMessage(HttpMethod.Get, "https://api.example.com"));
            bool shouldContinue = ApproovServiceMutator.Default.HandleInterceptorFetchTokenResult(context, new ApproovTokenFetchResult
            {
                status = ApproovTokenFetchStatus.NoApproovService
            });

            Assert.False(shouldContinue);
        }

        [Test]
        public void HandleInterceptorFetchTokenResult_ThrowsForNetworkFailureWhenProceedDisabled()
        {
            ApproovRequestContext context = ApproovRequestContext.Create(new HttpRequestMessage(HttpMethod.Get, "https://api.example.com"));

            Assert.Throws<NetworkingErrorException>(() =>
                ApproovServiceMutator.Default.HandleInterceptorFetchTokenResult(context, new ApproovTokenFetchResult
                {
                    status = ApproovTokenFetchStatus.NoNetwork
                }));
        }

        [Test]
        public void HandleInterceptorFetchTokenResult_ReturnsFalseForNetworkFailureWhenProceedEnabled()
        {
            ApproovService.SetProceedOnNetworkFailure(true);
            ApproovRequestContext context = ApproovRequestContext.Create(new HttpRequestMessage(HttpMethod.Get, "https://api.example.com"));

            bool shouldContinue = ApproovServiceMutator.Default.HandleInterceptorFetchTokenResult(context, new ApproovTokenFetchResult
            {
                status = ApproovTokenFetchStatus.PoorNetwork
            });

            Assert.False(shouldContinue);
        }

        [Test]
        public void HandleInterceptorFetchTokenResult_ReturnsTrueForNetworkFailureWhenStatusFallbackEnabled()
        {
            ApproovService.SetUseApproovStatusIfNoToken(true);
            ApproovRequestContext context = ApproovRequestContext.Create(new HttpRequestMessage(HttpMethod.Get, "https://api.example.com"));

            bool shouldContinue = ApproovServiceMutator.Default.HandleInterceptorFetchTokenResult(context, new ApproovTokenFetchResult
            {
                status = ApproovTokenFetchStatus.MITMDetected
            });

            Assert.True(shouldContinue);
        }

        [Test]
        public void ShouldProcessRequest_RespectsExclusionRegex()
        {
            const string regex = "https://skip\\.example\\.com/.*";
            ApproovService.AddExclusionURLRegex(regex);

            try
            {
                ApproovRequestContext context = ApproovRequestContext.Create(new HttpRequestMessage(HttpMethod.Get, "https://skip.example.com/path"));
                bool shouldProcess = ApproovServiceMutator.Default.ShouldProcessRequest(context);
                Assert.False(shouldProcess);
            }
            finally
            {
                ApproovService.RemoveExclusionURLRegex(regex);
            }
        }
    }
}
