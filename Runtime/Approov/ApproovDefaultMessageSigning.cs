using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Approov
{
    public class ApproovDefaultMessageSigning : ApproovServiceMutator
    {
        protected internal enum SigningMode
        {
            Install,
            Account
        }

        internal sealed class SignatureComponent
        {
            public string Identifier { get; set; }

            public string HeaderLookupName { get; set; }
        }

        internal sealed class SignaturePlan
        {
            public SigningMode Mode { get; set; }

            public List<SignatureComponent> Components { get; set; }

            public List<StructuredFieldParameter> Parameters { get; set; }
        }

        public sealed class SignatureParameters
        {
            private readonly List<string> _componentIdentifiers = new();

            public SignatureParameters AddComponentIdentifier(string componentIdentifier)
            {
                if (string.IsNullOrWhiteSpace(componentIdentifier))
                {
                    throw new ArgumentNullException(nameof(componentIdentifier));
                }

                _componentIdentifiers.Add(componentIdentifier);
                return this;
            }

            internal IReadOnlyList<string> ComponentIdentifiers => _componentIdentifiers;
        }

        public sealed class SignatureParametersFactory
        {
            private SignatureParameters _baseParameters;
            private string _bodyDigestAlgorithm;
            private bool _bodyDigestRequired;
            private bool _useAccountMessageSigning;
            private bool _addCreated;
            private long _expiresLifetime;
            private bool _addApproovTokenHeader;
            private bool _addApproovTraceIDHeader;
            private readonly List<string> _optionalHeaders = new();

            public SignatureParametersFactory SetBaseParameters(SignatureParameters baseParameters)
            {
                _baseParameters = baseParameters;
                return this;
            }

            public SignatureParametersFactory SetBodyDigestConfig(string bodyDigestAlgorithm, bool required)
            {
                if (bodyDigestAlgorithm == null)
                {
                    required = false;
                }
                else if (!string.Equals(bodyDigestAlgorithm, DIGEST_SHA256, StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(bodyDigestAlgorithm, DIGEST_SHA512, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("Unsupported body digest algorithm: " + bodyDigestAlgorithm, nameof(bodyDigestAlgorithm));
                }

                _bodyDigestAlgorithm = bodyDigestAlgorithm?.ToLowerInvariant();
                _bodyDigestRequired = required;
                return this;
            }

            public SignatureParametersFactory SetUseInstallMessageSigning()
            {
                _useAccountMessageSigning = false;
                return this;
            }

            public SignatureParametersFactory SetUseAccountMessageSigning()
            {
                _useAccountMessageSigning = true;
                return this;
            }

            public SignatureParametersFactory SetAddCreated(bool addCreated)
            {
                _addCreated = addCreated;
                return this;
            }

            public SignatureParametersFactory SetExpiresLifetime(long expiresLifetime)
            {
                _expiresLifetime = expiresLifetime;
                return this;
            }

            public SignatureParametersFactory SetAddApproovTokenHeader(bool addApproovTokenHeader)
            {
                _addApproovTokenHeader = addApproovTokenHeader;
                return this;
            }

            public SignatureParametersFactory SetAddApproovTraceIDHeader(bool addApproovTraceIDHeader)
            {
                _addApproovTraceIDHeader = addApproovTraceIDHeader;
                return this;
            }

            public SignatureParametersFactory AddOptionalHeaders(params string[] headers)
            {
                if (headers == null)
                {
                    return this;
                }

                for (int i = 0; i < headers.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(headers[i]))
                    {
                        _optionalHeaders.Add(headers[i]);
                    }
                }

                return this;
            }

            private SignaturePlan BuildSignaturePlan(ApproovRequestContext request, ApproovRequestMutations changes)
            {
                List<SignatureComponent> components = new();
                IReadOnlyList<string> baseComponents = _baseParameters?.ComponentIdentifiers;
                if (baseComponents != null)
                {
                    for (int i = 0; i < baseComponents.Count; i++)
                    {
                        AddComponent(components, baseComponents[i], baseComponents[i]);
                    }
                }

                if (_addApproovTokenHeader && !string.IsNullOrWhiteSpace(changes?.TokenHeaderKey))
                {
                    AddHeaderComponent(components, changes.TokenHeaderKey);
                }

                if (_addApproovTraceIDHeader && !string.IsNullOrWhiteSpace(changes?.TraceIDHeaderKey))
                {
                    AddHeaderComponent(components, changes.TraceIDHeaderKey);
                }

                for (int i = 0; i < _optionalHeaders.Count; i++)
                {
                    string header = _optionalHeaders[i];
                    if (request.HasHeader(header))
                    {
                        AddHeaderComponent(components, header);
                    }
                }

                if (!string.IsNullOrWhiteSpace(_bodyDigestAlgorithm))
                {
                    bool generated = TryGenerateBodyDigest(request, _bodyDigestAlgorithm, components);
                    if (!generated && _bodyDigestRequired)
                    {
                        throw new ConfigurationFailureException("ApproovDefaultMessageSigning: failed to create required content digest");
                    }
                }

                List<StructuredFieldParameter> parameters = new()
                {
                    new StructuredFieldParameter("alg", _useAccountMessageSigning ? ALG_HS256 : ALG_ES256)
                };

                if (_addCreated || _expiresLifetime > 0)
                {
                    long created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    if (_addCreated)
                    {
                        parameters.Add(new StructuredFieldParameter("created", created));
                    }

                    if (_expiresLifetime > 0)
                    {
                        parameters.Add(new StructuredFieldParameter("expires", created + _expiresLifetime));
                    }
                }

                return new SignaturePlan
                {
                    Mode = _useAccountMessageSigning ? SigningMode.Account : SigningMode.Install,
                    Components = components,
                    Parameters = parameters,
                };
            }

            private static bool TryGenerateBodyDigest(ApproovRequestContext request, string algorithm, List<SignatureComponent> components)
            {
                if (!request.TryGetBodyBytes(out byte[] bodyBytes))
                {
                    return false;
                }

                byte[] digestBytes = string.Equals(algorithm, DIGEST_SHA512, StringComparison.OrdinalIgnoreCase)
                    ? ComputeDigest(bodyBytes, SHA512.Create())
                    : ComputeDigest(bodyBytes, SHA256.Create());

                string digestHeaderValue = StructuredFieldValueSerializer.SerializeDictionary(new List<System.Collections.Generic.KeyValuePair<string, StructuredFieldItem>>
                {
                    new System.Collections.Generic.KeyValuePair<string, StructuredFieldItem>(algorithm.ToLowerInvariant(), new StructuredFieldItem(digestBytes))
                });

                request.SetHeader("Content-Digest", digestHeaderValue);
                AddHeaderComponent(components, "Content-Digest");
                return true;
            }

            private static void AddHeaderComponent(List<SignatureComponent> components, string headerName)
            {
                AddComponent(components, headerName.ToLowerInvariant(), headerName);
            }

            private static void AddComponent(List<SignatureComponent> components, string identifier, string headerLookupName)
            {
                if (string.IsNullOrWhiteSpace(identifier))
                {
                    return;
                }

                string normalizedIdentifier = identifier.StartsWith("@", StringComparison.Ordinal)
                    ? identifier.ToLowerInvariant()
                    : identifier.ToLowerInvariant();

                for (int i = 0; i < components.Count; i++)
                {
                    if (string.Equals(components[i].Identifier, normalizedIdentifier, StringComparison.Ordinal))
                    {
                        return;
                    }
                }

                components.Add(new SignatureComponent
                {
                    Identifier = normalizedIdentifier,
                    HeaderLookupName = headerLookupName,
                });
            }

            internal SignaturePlan CreatePlan(ApproovRequestContext request, ApproovRequestMutations changes)
            {
                return BuildSignaturePlan(request, changes);
            }
        }

        public const string DIGEST_SHA256 = "sha-256";
        public const string DIGEST_SHA512 = "sha-512";
        public const string ALG_ES256 = "ecdsa-p256-sha256";
        public const string ALG_HS256 = "hmac-sha256";

        private const string SignatureHeader = "Signature";
        private const string SignatureInputHeader = "Signature-Input";

        private readonly Dictionary<string, SignatureParametersFactory> _hostFactories = new(StringComparer.OrdinalIgnoreCase);

        protected SignatureParametersFactory DefaultFactory { get; private set; }

        public ApproovDefaultMessageSigning SetDefaultFactory(SignatureParametersFactory factory)
        {
            DefaultFactory = factory;
            return this;
        }

        public ApproovDefaultMessageSigning PutHostFactory(string authority, SignatureParametersFactory factory)
        {
            if (string.IsNullOrWhiteSpace(authority))
            {
                throw new ArgumentNullException(nameof(authority));
            }

            _hostFactories[authority] = factory;
            return this;
        }

        public override string ToString()
        {
            return "ApproovDefaultMessageSigning";
        }

        public override void HandleProcessedRequest(ApproovRequestContext request, ApproovRequestMutations changes)
        {
            if (request == null || changes == null || string.IsNullOrWhiteSpace(changes.TokenHeaderKey))
            {
                return;
            }

            SignatureParametersFactory factory = ResolveFactory(request.Uri);
            if (factory == null)
            {
                ApproovService.LogTrace("ApproovDefaultMessageSigning: no signing factory configured for " + request.Uri);
                return;
            }

            SignaturePlan plan = factory.CreatePlan(request, changes);
            if (plan == null || plan.Components.Count == 0)
            {
                ApproovService.LogTrace("ApproovDefaultMessageSigning: no signature components produced for " + request.Uri);
                return;
            }

            string signatureBase = BuildSignatureBase(request, plan);
            if (!TryGetSignatureBytes(plan.Mode, signatureBase, out string signatureLabel, out byte[] signatureBytes))
            {
                ApproovService.LogTrace("ApproovDefaultMessageSigning: no signature bytes available for " + request.Uri);
                return;
            }

            List<StructuredFieldItem> componentItems = new(plan.Components.Count);
            for (int i = 0; i < plan.Components.Count; i++)
            {
                componentItems.Add(new StructuredFieldItem(plan.Components[i].Identifier));
            }

            string signatureHeaderValue = StructuredFieldValueSerializer.SerializeDictionary(new List<System.Collections.Generic.KeyValuePair<string, StructuredFieldItem>>
            {
                new System.Collections.Generic.KeyValuePair<string, StructuredFieldItem>(signatureLabel, new StructuredFieldItem(signatureBytes))
            });

            string signatureInputValue = StructuredFieldValueSerializer.SerializeDictionary(new List<System.Collections.Generic.KeyValuePair<string, StructuredFieldItem>>
            {
                new System.Collections.Generic.KeyValuePair<string, StructuredFieldItem>(signatureLabel, new StructuredFieldItem(componentItems, plan.Parameters))
            });

            request.SetHeader(SignatureHeader, signatureHeaderValue);
            request.SetHeader(SignatureInputHeader, signatureInputValue);
            ApproovService.LogTrace(
                "ApproovDefaultMessageSigning: added " + signatureLabel +
                " signature with " + plan.Components.Count + " components for " + request.Uri);
        }

        public static SignatureParametersFactory GenerateDefaultSignatureParametersFactory()
        {
            return GenerateDefaultSignatureParametersFactory(null);
        }

        public static SignatureParametersFactory GenerateDefaultSignatureParametersFactory(SignatureParameters baseParametersOverride)
        {
            SignatureParameters baseParameters = baseParametersOverride ?? new SignatureParameters()
                .AddComponentIdentifier("@method")
                .AddComponentIdentifier("@target-uri");

            return new SignatureParametersFactory()
                .SetBaseParameters(baseParameters)
                .SetUseInstallMessageSigning()
                .SetAddCreated(true)
                .SetExpiresLifetime(15)
                .SetAddApproovTokenHeader(true)
                .SetAddApproovTraceIDHeader(true)
                .AddOptionalHeaders("Authorization", "Content-Length", "Content-Type")
                .SetBodyDigestConfig(DIGEST_SHA256, false);
        }

        protected virtual bool TryGetSignatureBytes(SigningMode mode, string message, out string signatureLabel, out byte[] signatureBytes)
        {
            signatureLabel = mode == SigningMode.Account ? "account" : "install";
            signatureBytes = null;

            try
            {
                string base64Signature = mode == SigningMode.Account
                    ? ApproovService.GetAccountMessageSignature(message)
                    : ApproovService.GetInstallMessageSignature(message);

                if (string.IsNullOrWhiteSpace(base64Signature))
                {
                    return false;
                }

                byte[] rawSignature = Convert.FromBase64String(base64Signature);
                if (mode == SigningMode.Install)
                {
                    rawSignature = ConvertDerSignatureToP1363(rawSignature);
                }

                signatureBytes = rawSignature;
                return true;
            }
            catch (ApproovException ex)
            {
                ApproovService.LogTrace("ApproovDefaultMessageSigning: skipping request signature because " + ex.Message);
                return false;
            }
            catch (FormatException ex)
            {
                throw new PermanentException("ApproovDefaultMessageSigning: invalid signature encoding - " + ex.Message);
            }
        }

        internal static byte[] ConvertDerSignatureToP1363(byte[] derSignature)
        {
            if (derSignature == null || derSignature.Length == 0)
            {
                throw new ArgumentException("DER signature is missing", nameof(derSignature));
            }

            int offset = 0;
            ExpectTag(derSignature, ref offset, 0x30);
            ReadLength(derSignature, ref offset);
            byte[] r = ReadInteger(derSignature, ref offset);
            byte[] s = ReadInteger(derSignature, ref offset);

            byte[] result = new byte[64];
            CopyInteger(r, result, 0);
            CopyInteger(s, result, 32);
            return result;
        }

        private SignatureParametersFactory ResolveFactory(Uri uri)
        {
            if (uri != null && _hostFactories.TryGetValue(uri.Authority, out SignatureParametersFactory hostFactory))
            {
                return hostFactory;
            }

            return DefaultFactory;
        }

        private static string BuildSignatureBase(ApproovRequestContext request, SignaturePlan plan)
        {
            List<string> lines = new(plan.Components.Count + 1);
            List<StructuredFieldItem> componentItems = new(plan.Components.Count);
            for (int i = 0; i < plan.Components.Count; i++)
            {
                SignatureComponent component = plan.Components[i];
                componentItems.Add(new StructuredFieldItem(component.Identifier));
                string label = StructuredFieldValueSerializer.SerializeItem(new StructuredFieldItem(component.Identifier));
                string value = ResolveComponentValue(request, component);
                lines.Add(label + ": " + value);
            }

            string signatureParams = StructuredFieldValueSerializer.SerializeInnerList(componentItems, plan.Parameters);
            lines.Add("\"@signature-params\": " + signatureParams);
            return string.Join("\n", lines);
        }

        private static string ResolveComponentValue(ApproovRequestContext request, SignatureComponent component)
        {
            switch (component.Identifier)
            {
                case "@method":
                    return request.Method ?? string.Empty;
                case "@target-uri":
                    return request.Uri?.AbsoluteUri ?? string.Empty;
                case "@authority":
                    return request.Uri?.Authority ?? string.Empty;
                case "@scheme":
                    return request.Uri?.Scheme ?? string.Empty;
                case "@path":
                    return request.Uri?.AbsolutePath ?? string.Empty;
                case "@query":
                    return request.Uri?.Query?.TrimStart('?') ?? string.Empty;
                case "@request-target":
                    if (request.Uri == null)
                    {
                        return string.Empty;
                    }

                    return request.Uri.AbsolutePath + request.Uri.Query;
                default:
                    string value = request.GetHeader(component.HeaderLookupName);
                    if (value == null)
                    {
                        throw new ApproovException("ApproovDefaultMessageSigning: missing required header '" + component.HeaderLookupName + "'");
                    }

                    return value;
            }
        }

        private static void ExpectTag(byte[] derSignature, ref int offset, byte expectedTag)
        {
            if (offset >= derSignature.Length || derSignature[offset] != expectedTag)
            {
                throw new FormatException("Unexpected DER tag");
            }

            offset++;
        }

        private static int ReadLength(byte[] derSignature, ref int offset)
        {
            if (offset >= derSignature.Length)
            {
                throw new FormatException("Invalid DER length");
            }

            int length = derSignature[offset++];
            if ((length & 0x80) == 0)
            {
                return length;
            }

            int octetCount = length & 0x7F;
            if (octetCount <= 0 || octetCount > 4 || offset + octetCount > derSignature.Length)
            {
                throw new FormatException("Unsupported DER length");
            }

            length = 0;
            for (int i = 0; i < octetCount; i++)
            {
                length = (length << 8) | derSignature[offset++];
            }

            return length;
        }

        private static byte[] ReadInteger(byte[] derSignature, ref int offset)
        {
            ExpectTag(derSignature, ref offset, 0x02);
            int length = ReadLength(derSignature, ref offset);
            if (length <= 0 || offset + length > derSignature.Length)
            {
                throw new FormatException("Invalid DER integer length");
            }

            byte[] integerBytes = new byte[length];
            Buffer.BlockCopy(derSignature, offset, integerBytes, 0, length);
            offset += length;
            return integerBytes;
        }

        private static void CopyInteger(byte[] source, byte[] destination, int destinationOffset)
        {
            int startIndex = 0;
            if (source.Length > 32)
            {
                if (source.Length == 33 && source[0] == 0)
                {
                    startIndex = 1;
                }
                else
                {
                    throw new FormatException("DER integer is larger than 32 bytes");
                }
            }

            int length = source.Length - startIndex;
            if (length > 32)
            {
                throw new FormatException("DER integer is larger than 32 bytes");
            }

            Buffer.BlockCopy(source, startIndex, destination, destinationOffset + (32 - length), length);
        }

        private static byte[] ComputeDigest(byte[] data, HashAlgorithm algorithm)
        {
            using (algorithm)
            {
                return algorithm.ComputeHash(data);
            }
        }
    }
}
