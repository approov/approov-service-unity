using System.Net.Http;
using System.Net.Security;
using NUnit.Framework;

namespace Approov.Tests
{
    public class ApproovHttpClientHandlerTests
    {
        [Test]
        public void Constructor_ComposesExistingCertificateCallback()
        {
            bool existingCallbackCalled = false;
            HttpClientHandler innerHandler = new()
            {
                ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
                {
                    existingCallbackCalled = true;
                    return true;
                }
            };

            using ApproovHttpClientHandler approovHandler = new(innerHandler);

            bool result = innerHandler.ServerCertificateCustomValidationCallback(
                new HttpRequestMessage(HttpMethod.Get, "https://api.example.com"),
                null,
                null,
                SslPolicyErrors.None);

            Assert.True(existingCallbackCalled);
            Assert.True(result);
        }

        [Test]
        public void Constructor_PreservesExistingCertificateCallbackRejection()
        {
            bool existingCallbackCalled = false;
            HttpClientHandler innerHandler = new()
            {
                ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
                {
                    existingCallbackCalled = true;
                    return false;
                }
            };

            using ApproovHttpClientHandler approovHandler = new(innerHandler);

            bool result = innerHandler.ServerCertificateCustomValidationCallback(
                new HttpRequestMessage(HttpMethod.Get, "https://api.example.com"),
                null,
                null,
                SslPolicyErrors.None);

            Assert.True(existingCallbackCalled);
            Assert.False(result);
        }
    }
}
