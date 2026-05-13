using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.Networking;

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

        private sealed class CustomCertificateHandler : CertificateHandler
        {
            protected override bool ValidateCertificate(byte[] certificateData)
            {
                return true;
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
        public void HandleInterceptorFetchTokenResult_ThrowsRejectionExceptionForRejected()
        {
            ApproovRequestContext context = ApproovRequestContext.Create(new HttpRequestMessage(HttpMethod.Get, "https://api.example.com"));

            RejectionException exception = Assert.Throws<RejectionException>(() =>
                ApproovServiceMutator.Default.HandleInterceptorFetchTokenResult(context, new ApproovTokenFetchResult
                {
                    status = ApproovTokenFetchStatus.Rejected,
                    ARC = "arc",
                    rejectionReasons = "reasons"
                }));

            Assert.That(exception.ARC, Is.EqualTo("arc"));
            Assert.That(exception.RejectionReasons, Is.EqualTo("reasons"));
        }

        [Test]
        public void HandleFetchTokenResult_ThrowsRejectionExceptionForRejected()
        {
            RejectionException exception = Assert.Throws<RejectionException>(() =>
                ApproovServiceMutator.Default.HandleFetchTokenResult(new ApproovTokenFetchResult
                {
                    status = ApproovTokenFetchStatus.Rejected,
                    ARC = "arc",
                    rejectionReasons = "reasons"
                }));

            Assert.That(exception.ARC, Is.EqualTo("arc"));
            Assert.That(exception.RejectionReasons, Is.EqualTo("reasons"));
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
        public void Create_ReturnsNullUriForUnsetUnityUrl()
        {
            UnityEngine.Networking.UnityWebRequest request = new();

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

        [Test]
        public void TryGetBodyBytes_ReturnsTrueForEmptyBody()
        {
            HttpRequestMessage request = new(HttpMethod.Post, "https://api.example.com")
            {
                Content = new ByteArrayContent(System.Array.Empty<byte>())
            };

            ApproovRequestContext context = ApproovRequestContext.Create(request);

            Assert.True(context.TryGetBodyBytes(out byte[] bodyBytes));
            Assert.That(bodyBytes, Is.Empty);
        }

        [Test]
        public void CombineHeaderValues_UsesHttpListSeparator()
        {
            string combined = ApproovRequestContext.CombineHeaderValues(new[] { "one", " two " });

            Assert.That(combined, Is.EqualTo("one, two"));
        }

        [Test]
        public void AddSubstitutionQueryParam_RejectsNullOrWhitespaceKey()
        {
            Assert.Throws<System.ArgumentNullException>(() => ApproovService.AddSubstitutionQueryParam(null));
            Assert.Throws<System.ArgumentNullException>(() => ApproovService.AddSubstitutionQueryParam(" "));
        }

        [Test]
        public void SetDataHashInToken_RejectsNullData()
        {
            Assert.Throws<System.ArgumentNullException>(() => ApproovService.SetDataHashInToken(null));
        }

        [Test]
        public void FetchToken_RejectsMissingUrl()
        {
            Assert.Throws<System.ArgumentNullException>(() => ApproovService.FetchToken(null));
            Assert.Throws<System.ArgumentNullException>(() => ApproovService.FetchToken(" "));
        }

        [Test]
        public void FetchSecureString_RejectsInvalidKeyBeforeNativeBridge()
        {
            Assert.Throws<System.ArgumentNullException>(() => ApproovService.FetchSecureString(null, null));
            Assert.Throws<System.ArgumentNullException>(() => ApproovService.FetchSecureString(string.Empty, null));
            Assert.Throws<System.ArgumentException>(() => ApproovService.FetchSecureString(new string('a', 65), null));
        }

        [Test]
        public void FetchCustomJWT_RejectsMissingPayload()
        {
            Assert.Throws<System.ArgumentNullException>(() => ApproovService.FetchCustomJWT(null));
            Assert.Throws<System.ArgumentNullException>(() => ApproovService.FetchCustomJWT(" "));
        }

        [Test]
        public void GetPinsJSON_RejectsMissingPinType()
        {
            Assert.Throws<System.ArgumentNullException>(() => ApproovService.GetPinsJSON(null));
            Assert.Throws<System.ArgumentNullException>(() => ApproovService.GetPinsJSON(" "));
        }

        [Test]
        public void MessageSigning_RejectsNullMessage()
        {
            Assert.Throws<System.ArgumentNullException>(() => ApproovService.GetAccountMessageSignature(null));
            Assert.Throws<System.ArgumentNullException>(() => ApproovService.GetInstallMessageSignature(null));
        }

        [Test]
        public void MeasurementProofs_RejectInvalidInputsBeforeNativeBridge()
        {
            byte[] validNonce = new byte[16];
            byte[] measurementConfig = new byte[] { 1, 2, 3 };

            Assert.Throws<System.ArgumentNullException>(() => ApproovService.GetIntegrityMeasurementProof(null, measurementConfig));
            Assert.Throws<System.ArgumentNullException>(() => ApproovService.GetIntegrityMeasurementProof(validNonce, null));
            Assert.Throws<System.ArgumentException>(() => ApproovService.GetIntegrityMeasurementProof(new byte[15], measurementConfig));

            Assert.Throws<System.ArgumentNullException>(() => ApproovService.GetDeviceMeasurementProof(null, measurementConfig));
            Assert.Throws<System.ArgumentNullException>(() => ApproovService.GetDeviceMeasurementProof(validNonce, null));
            Assert.Throws<System.ArgumentException>(() => ApproovService.GetDeviceMeasurementProof(new byte[15], measurementConfig));
        }

        [Test]
        public void DecodeSubstitutionQueryParameterValue_UnescapesPercentEncodedKey()
        {
            string decoded = ApproovRequestProcessor.DecodeSubstitutionQueryParameterValue("tenant%2Fprod%20api%2Bkey");

            Assert.That(decoded, Is.EqualTo("tenant/prod api+key"));
        }

        [Test]
        public void HandleTokenFetchSideEffects_ThrowsRetryableNetworkingErrorForForcedPins()
        {
            NetworkingErrorException exception = Assert.Throws<NetworkingErrorException>(() =>
                ApproovService.HandleTokenFetchSideEffects(new ApproovTokenFetchResult
                {
                    isForceApplyPins = true
                }, "test fetch"));

            Assert.True(exception.ShouldRetry);
        }

        [Test]
        public void ExclusionURLRegex_ThrowsForMalformedRegex()
        {
            Assert.Throws<System.ArgumentException>(() => ApproovService.AddExclusionURLRegex("["));
            Assert.Throws<System.ArgumentException>(() => ApproovService.RemoveExclusionURLRegex("["));
        }

        [Test]
        public void ApplyUnityWebRequestPinning_ReplacesExistingCertificateHandler()
        {
            using UnityWebRequest request = UnityWebRequest.Get("https://api.example.com");
            request.certificateHandler = new CustomCertificateHandler();

            MethodInfo applyPinning = typeof(ApproovService).GetMethod(
                "ApplyUnityWebRequestPinning",
                BindingFlags.NonPublic | BindingFlags.Static);

            applyPinning.Invoke(null, new object[] { request, "test" });

            Assert.That(request.certificateHandler, Is.TypeOf<ApproovCertificateHandler>());
        }

        [Test]
        public void ApproovCertificateHandler_ConstructsForUnsetUnityRequestUrl()
        {
            using UnityWebRequest request = new();
            ApproovCertificateHandler handler = null;

            try
            {
                Assert.DoesNotThrow(() => handler = new ApproovCertificateHandler(request));
            }
            finally
            {
                handler?.Dispose();
            }
        }
    }
}
