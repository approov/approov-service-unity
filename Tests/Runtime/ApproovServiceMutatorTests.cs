using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Approov.Tests
{
    public class ApproovServiceMutatorTests
    {
        private sealed class ThrowingHttpContent : HttpContent
        {
            protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext context)
            {
                throw new IOException("content should not be read");
            }

            protected override bool TryComputeLength(out long length)
            {
                length = -1;
                return false;
            }
        }

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

        [Test]
        public void HasHeader_ReturnsTrueForEmptyHeaderValue()
        {
            HttpRequestMessage request = new(HttpMethod.Get, "https://api.example.com");
            request.Headers.TryAddWithoutValidation("X-Empty", string.Empty);

            ApproovRequestContext context = ApproovRequestContext.Create(request);

            Assert.That(context.GetHeader("X-Empty"), Is.EqualTo(string.Empty));
            Assert.That(context.HasHeader("X-Empty"), Is.True);
        }

        [Test]
        public void Create_ReturnsNullUriForMalformedUrl()
        {
            UnityEngine.Networking.UnityWebRequest request = new(
                "not a valid url",
                UnityEngine.Networking.UnityWebRequest.kHttpVerbGET);

            ApproovRequestContext context = ApproovRequestContext.Create(request);

            Assert.That(context.Uri, Is.Null);
        }

        [Test]
        public void CreateSnapshotWithoutBody_DoesNotReadHttpContent()
        {
            HttpRequestMessage request = new(HttpMethod.Post, "https://api.example.com")
            {
                Content = new ThrowingHttpContent()
            };

            ApproovRequestContext context = ApproovRequestContext.CreateSnapshot(request, includeBody: false);

            Assert.That(context.Uri.AbsoluteUri, Is.EqualTo("https://api.example.com/"));
            Assert.False(context.TryGetBodyBytes(out _));
        }
    }
}
