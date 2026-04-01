using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using NUnit.Framework;

namespace Approov.Tests
{
    public class ApproovDefaultMessageSigningTests
    {
        private sealed class StubSigner : ApproovDefaultMessageSigning
        {
            public string LastMessage { get; private set; }

            protected override bool TryGetSignatureBytes(SigningMode mode, string message, out string signatureLabel, out byte[] signatureBytes)
            {
                LastMessage = message;
                signatureLabel = mode == SigningMode.Account ? "account" : "install";
                signatureBytes = new byte[] { 1, 2, 3, 4 };
                return true;
            }
        }

        [Test]
        public void HandleProcessedRequest_AddsInstallSignatureHeaders()
        {
            HttpRequestMessage request = new(HttpMethod.Get, "https://api.example.com/v1/test?x=1");
            request.Headers.TryAddWithoutValidation("Approov-Token", "token-value");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer hello");

            StubSigner signer = new StubSigner();
            signer.SetDefaultFactory(ApproovDefaultMessageSigning.GenerateDefaultSignatureParametersFactory());

            ApproovRequestContext context = ApproovRequestContext.Create(request);
            signer.HandleProcessedRequest(context, new ApproovRequestMutations
            {
                TokenHeaderKey = "Approov-Token"
            });

            string signature = request.Headers.GetValues("Signature").Single();
            string signatureInput = request.Headers.GetValues("Signature-Input").Single();

            Assert.That(signature, Is.EqualTo("install=:AQIDBA==:"));
            StringAssert.Contains("install=(", signatureInput);
            StringAssert.Contains("\"@method\"", signatureInput);
            StringAssert.Contains("\"@target-uri\"", signatureInput);
            StringAssert.Contains("\"approov-token\"", signatureInput);
            StringAssert.Contains("\"authorization\"", signatureInput);
            StringAssert.Contains("alg=\"ecdsa-p256-sha256\"", signatureInput);
            StringAssert.Contains("created=", signatureInput);
            StringAssert.Contains("expires=", signatureInput);
            StringAssert.Contains("\"@method\": GET", signer.LastMessage);
            StringAssert.Contains("\"approov-token\": token-value", signer.LastMessage);
        }

        [Test]
        public void HandleProcessedRequest_AddsContentDigestWhenBodyIsReadable()
        {
            HttpRequestMessage request = new(HttpMethod.Post, "https://api.example.com/v1/test");
            request.Headers.TryAddWithoutValidation("Approov-Token", "token-value");
            request.Content = new StringContent("hello", Encoding.UTF8, "text/plain");

            StubSigner signer = new StubSigner();
            ApproovDefaultMessageSigning.SignatureParametersFactory factory = new ApproovDefaultMessageSigning.SignatureParametersFactory()
                .SetBaseParameters(new ApproovDefaultMessageSigning.SignatureParameters()
                    .AddComponentIdentifier("@method")
                    .AddComponentIdentifier("@target-uri"))
                .SetUseInstallMessageSigning()
                .SetAddApproovTokenHeader(true)
                .SetBodyDigestConfig(ApproovDefaultMessageSigning.DIGEST_SHA256, true);
            signer.SetDefaultFactory(factory);

            ApproovRequestContext context = ApproovRequestContext.Create(request);
            signer.HandleProcessedRequest(context, new ApproovRequestMutations
            {
                TokenHeaderKey = "Approov-Token"
            });

            string contentDigest = context.GetHeader("Content-Digest");
            string signatureInput = request.Headers.GetValues("Signature-Input").Single();

            Assert.That(contentDigest, Is.Not.Null.And.Contains("sha-256=:"));
            StringAssert.Contains("\"content-digest\"", signatureInput);
            StringAssert.Contains("\"content-digest\": sha-256=:", signer.LastMessage);
        }

        [Test]
        public void SerializeDictionary_OmitsEqualsForBareBooleanTrue()
        {
            string serialized = StructuredFieldValueSerializer.SerializeDictionary(new List<KeyValuePair<string, StructuredFieldItem>>
            {
                new("sig", new StructuredFieldItem(true, new[]
                {
                    new StructuredFieldParameter("key", "value")
                })),
                new("alg", new StructuredFieldItem("ecdsa-p256-sha256"))
            });

            Assert.That(serialized, Is.EqualTo("sig;key=\"value\", alg=\"ecdsa-p256-sha256\""));
        }

        [Test]
        public void HandleProcessedRequest_SkipsSigningWhenHostFactoryIsNull()
        {
            HttpRequestMessage request = new(HttpMethod.Get, "https://api.example.com/v1/test");
            request.Headers.TryAddWithoutValidation("Approov-Token", "token-value");

            StubSigner signer = new StubSigner();
            signer.SetDefaultFactory(ApproovDefaultMessageSigning.GenerateDefaultSignatureParametersFactory());
            signer.PutHostFactory("api.example.com", null);

            ApproovRequestContext context = ApproovRequestContext.Create(request);
            signer.HandleProcessedRequest(context, new ApproovRequestMutations
            {
                TokenHeaderKey = "Approov-Token"
            });

            Assert.False(request.Headers.Contains("Signature"));
            Assert.False(request.Headers.Contains("Signature-Input"));
        }

        [Test]
        public void ConvertDerSignatureToP1363_ConvertsTwoFixedWidthIntegers()
        {
            byte[] r = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
            byte[] s = Enumerable.Range(33, 32).Select(value => (byte)value).ToArray();

            byte[] der = new byte[2 + 2 + r.Length + 2 + s.Length];
            int offset = 0;
            der[offset++] = 0x30;
            der[offset++] = 0x44;
            der[offset++] = 0x02;
            der[offset++] = 0x20;
            Buffer.BlockCopy(r, 0, der, offset, r.Length);
            offset += r.Length;
            der[offset++] = 0x02;
            der[offset++] = 0x20;
            Buffer.BlockCopy(s, 0, der, offset, s.Length);

            byte[] converted = ApproovDefaultMessageSigning.ConvertDerSignatureToP1363(der);

            Assert.That(converted, Has.Length.EqualTo(64));
            Assert.That(converted.Take(32).ToArray(), Is.EqualTo(r));
            Assert.That(converted.Skip(32).ToArray(), Is.EqualTo(s));
        }
    }
}
