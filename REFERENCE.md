# Reference

This document describes the supported public API for the Unity package itself. It is intentionally focused on the package surface, not the internal native bridge implementation.

The primary integration types are:

- `ApproovService`
- `ApproovHttpClientHandler`
- `ApproovServiceMutator`
- `ApproovRequestContext`
- `ApproovRequestMutations`
- `ApproovDefaultMessageSigning`
- `ApproovException` and its subclasses

## Setup Model

Before using the runtime API:

1. Save the Approov config string in `Tools/Approov/Approov Settings`.
2. For Android, rely on the packaged Gradle/Maven integration.
3. For iOS, install `Approov.xcframework` from the Unity `Tools/Approov` menu.
4. Call `ApproovService.Initialize()` early during app startup on device.

The package runtime only initializes the native SDK on Android and iOS player builds. In the editor or desktop builds, native initialization is intentionally unavailable.

## ApproovService

`ApproovService` is the main entry point for initialization, network integration, fetch operations, and runtime policy.

### Initialization And Lifecycle

- `Initialize()`
  Reads the config string that the editor synced into `Assets/Resources/Approov/ApproovConfig.txt`.
- `Initialize(string config)`
  Initializes the native SDK with an explicit config string.
- `IsSDKInitialized()`
  Returns whether the native SDK has been initialized in the current session.
- `Prefetch()`
  Starts an early background SDK configuration fetch to warm the Approov state before the first protected request.
- `Prefetch(string url)`
  Starts an early background token fetch for a specific protected URL.

Initialization rules:

- native initialization only happens on Android and iOS player builds
- calling `Initialize` again with the same config is ignored
- calling `Initialize` again with a different config throws a configuration failure

### UnityWebRequest Integration

Recommended usage:

```csharp
UnityWebRequest request = UnityWebRequest.Get("https://example.com");
yield return ApproovService.SendWebRequest(request);
```

Members:

- `SendWebRequest(UnityEngine.Networking.UnityWebRequest request)`
  Coroutine-friendly API that applies token injection, secure-string substitution, optional trace header mutation, and Approov certificate validation before dispatching the request without blocking the Unity main thread during native token fetches.
- `SendApproovWebRequest(this UnityEngine.Networking.UnityWebRequest request)`
  Thin extension-method alias for `ApproovService.SendWebRequest(...)`.

`ApproovService.SendWebRequest(...)` is the preferred UnityWebRequest path because the service-owned API cannot be bypassed by Unity method hiding.

### HttpClient Integration

Recommended usage:

```csharp
HttpClient client = ApproovService.CreateHttpClient();
```

Members:

- `CreateHttpClient()`
  Returns an `HttpClient` that uses `ApproovHttpClientHandler`.
- `CreateHttpClient(System.Net.Http.HttpMessageHandler innerHandler)`
  Wraps a caller-supplied handler chain with `ApproovHttpClientHandler`.
- `CreateHttpClientHandler()`
  Creates a standalone `ApproovHttpClientHandler`.
- `CreateHttpClientHandler(System.Net.Http.HttpMessageHandler innerHandler)`
  Creates a standalone `ApproovHttpClientHandler` around an existing inner handler.

### Logging And Diagnostics

- `SetLoggingLevel(ApproovLogLevel level)` / `GetLoggingLevel()`
  Controls service-layer logging verbosity.
- `SetDetailedDebugLogging(bool enabled)` / `GetDetailedDebugLogging()`
  Convenience switch for trace-level diagnostics.
- `ApproovTokenFetchStatusToString(ApproovTokenFetchStatus status)`
  Converts the native status enum to the canonical string representation.
- `DescribeFetchResult(ApproovTokenFetchResult fetchResult)`
  Produces a structured diagnostic string for logging.

### Token, Trace, And Retry Policy

- `SetBindingHeader(string header)` / `GetBindingHeader()`
  Configures the header whose value is hashed into the Approov token payload for token binding.
- `SetTokenHeaderAndPrefix(string header, string prefix)` / `GetTokenHeader()` / `GetTokenPrefix()`
  Controls which header receives the Approov token and what prefix, if any, is prepended.
- `SetApproovTraceIDHeader(string header)` / `GetApproovTraceIDHeader()`
  Controls the optional trace header name. Pass `null` to disable it.
- `SetProceedOnNetworkFailure(bool proceed)` / `GetProceedOnNetworkFailure()`
  Controls whether requests may continue without Approov mutation when Approov networking fails.
- `SetUseApproovStatusIfNoToken(bool shouldUse)` / `GetUseApproovStatusIfNoToken()`
  Controls whether the textual Approov fetch status is used as the token value when no token is available.

These flags change request behavior materially. In particular, `SetProceedOnNetworkFailure(true)` can allow calls to continue without token injection or secure-string substitution, which may be inappropriate for production routes.

### Request Customization

- `SetServiceMutator(ApproovServiceMutator mutator)` / `GetServiceMutator()`
  Installs the request/fetch policy hook used by the service layer.
- `AddSubstitutionHeader(string header, string requiredPrefix)` / `RemoveSubstitutionHeader(string header)` / `GetSubstitutionHeaders()`
  Declares headers whose values should be replaced with secure strings.
- `AddSubstitutionQueryParam(string key)` / `RemoveSubstitutionQueryParam(string key)` / `GetSubstitutionQueryParams()`
  Declares query parameter names whose values should be replaced with secure strings.
- `AddExclusionURLRegex(string urlRegex)` / `RemoveExclusionURLRegex(string urlRegex)` / `CheckURLIsExcluded(string url)`
  Declares request URL patterns that should bypass Approov request mutation.

Exclusion rules should be used carefully. Excluding protected URLs can prevent normal pin refresh paths from running.

### SDK Fetch And Configuration Operations

- `SetUserProperty(string property)`
  Publishes an informational user property into Approov telemetry for the current app instance.
- `FetchSecureString(string key, string newDef)`
  Looks up or defines an Approov secure string.
- `FetchCustomJWT(string payload)`
  Fetches a custom JWT for the supplied JSON payload.
- `Precheck()`
  Performs an attestation precheck.
- `GetDeviceID()`
  Returns the current Approov device identifier for this app installation.
- `SetDataHashInToken(string data)`
  Hashes caller-provided non-null data into subsequent token fetches. Passing `null` is rejected because the native SDK token-binding API requires a data value.
- `FetchToken(string url)`
  Performs an explicit token fetch for a URL when interceptor-based injection is not being used.
- `GetPinsJSON(string pinType)`
  Returns the current Approov pin set as JSON.
- `FetchConfig()`
  Returns the currently cached or freshly fetched dynamic SDK configuration.
- `SetDevKey(string key)`
  Sets a development key for controlled development and testing workflows only.
- `GetIntegrityMeasurementProof(byte[] nonce, byte[] measurementConfig)`
  Produces an integrity measurement proof using a previously returned measurement configuration.
- `GetDeviceMeasurementProof(byte[] nonce, byte[] measurementConfig)`
  Produces a device measurement proof using a previously returned measurement configuration.

### Message Signing Primitives

- `GetMessageSignature(string message)`
  Obsolete alias for account signing.
- `GetAccountMessageSignature(string message)`
  Returns a base64-encoded account-key signature for the exact string payload supplied.
- `GetInstallMessageSignature(string message)`
  Returns a base64-encoded install-key signature for the exact string payload supplied.

The `message` string is signed exactly as provided. Do not pre-hash it, normalize whitespace implicitly, or assume canonicalization by the package.

## ApproovHttpClientHandler

`ApproovHttpClientHandler` is the `HttpClient` transport adapter for the package.

Members:

- `ApproovHttpClientHandler()`
- `ApproovHttpClientHandler(System.Net.Http.HttpMessageHandler innerHandler)`

Behavior:

- mutates outbound `HttpRequestMessage` instances through the shared `ApproovRequestProcessor`
- preserves request URL and host metadata for later certificate validation
- if the inner handler is an `HttpClientHandler`, installs the Approov TLS validation callback

## ApproovCertificateHandler

`ApproovCertificateHandler` is the Unity TLS callback used by `ApproovService.SendWebRequest(...)`.

Guidance:

- you generally do not instantiate it directly
- the service layer installs or removes it automatically depending on whether pinning should apply
- it evaluates SPKI pins through the native Approov SDK

## ApproovServiceMutator

`ApproovServiceMutator` is the package extension point for fetch handling, request policy, post-processing, and pinning decisions.

Members:

- `Default`
  The built-in default mutator instance.
- `HandlePrecheckResult(ApproovTokenFetchResult approovResult)`
- `HandleFetchTokenResult(ApproovTokenFetchResult approovResult)`
- `HandleFetchSecureStringResult(ApproovTokenFetchResult approovResult, string operation, string key)`
- `HandleFetchCustomJwtResult(ApproovTokenFetchResult approovResult)`
- `ShouldProcessRequest(ApproovRequestContext request)`
- `HandleInterceptorFetchTokenResult(ApproovRequestContext request, ApproovTokenFetchResult approovResult)`
- `HandleHeaderSubstitutionResult(ApproovRequestContext request, ApproovTokenFetchResult approovResult, string header)`
- `HandleQueryParamSubstitutionResult(ApproovRequestContext request, ApproovTokenFetchResult approovResult, string queryKey)`
- `HandleProcessedRequest(ApproovRequestContext request, ApproovRequestMutations changes)`
- `ShouldProcessPinning(ApproovRequestContext request)`
- `AddUnityRequestHeadersToCapture(ISet<string> headers)`
- `AddUnityRequestHeadersToCapture(ISet<string> headers, ApproovRequestContext request)`

The default mutator:

- skips excluded URLs
- throws retryable exceptions for network failures when configured to fail closed
- converts rejections and permanent native failures into typed `ApproovException` subclasses

## ApproovRequestContext

`ApproovRequestContext` is the transport-neutral request wrapper exposed to mutators.

Members:

- `Transport`
  Indicates whether the context came from `UnityWebRequest`, `HttpClient`, or a snapshot copy.
- `Method`
  The HTTP method name.
- `Uri`
  The current request URI. Setting it updates the live request when the context is mutable.
- `GetHeader(string name)`
- `HasHeader(string name)`
- `SetHeader(string name, string value)`
- `TryGetBodyBytes(out byte[] bodyBytes)`

Snapshots are used where the package must read request data off the main thread or after the transport-specific object can no longer be queried safely, such as certificate validation callbacks.

## ApproovRequestMutations

`ApproovRequestMutations` describes what the service layer changed on a request before `HandleProcessedRequest(...)` runs.

Members:

- `TokenHeaderKey`
  Header name that received the Approov token.
- `TraceIDHeaderKey`
  Header name that received the Approov trace ID.
- `SubstitutionHeaderKeys`
  Headers that were replaced with secure-string values.
- `OriginalUrl`
  Pre-substitution URL when query parameters caused a URI rewrite.
- `SubstitutionQueryParamKeys`
  Query parameter names that were replaced with secure-string values.

## ApproovDefaultMessageSigning

`ApproovDefaultMessageSigning` is a ready-to-use `ApproovServiceMutator` that adds RFC 9421-style `Signature` and `Signature-Input` headers after Approov token injection.
When a signing factory is selected but the SDK cannot produce signature bytes, the mutator leaves the request unsigned.

Key members:

- `SetDefaultFactory(SignatureParametersFactory factory)`
- `PutHostFactory(string authority, SignatureParametersFactory factory)`
- `GenerateDefaultSignatureParametersFactory()`
- `GenerateDefaultSignatureParametersFactory(SignatureParameters baseParametersOverride)`
- constants `DIGEST_SHA256`, `DIGEST_SHA512`, `ALG_ES256`, and `ALG_HS256`

### SignatureParameters

`SignatureParameters` describes the covered component identifiers:

- `AddComponentIdentifier(string componentIdentifier)`

### SignatureParametersFactory

`SignatureParametersFactory` configures how signatures are built:

- `SetBaseParameters(SignatureParameters baseParameters)`
- `SetBodyDigestConfig(string bodyDigestAlgorithm, bool required)`
- `SetUseInstallMessageSigning()`
- `SetUseAccountMessageSigning()`
- `SetAddCreated(bool addCreated)`
- `SetExpiresLifetime(long expiresLifetime)`
- `SetAddApproovTokenHeader(bool addApproovTokenHeader)`
- `SetAddApproovTraceIDHeader(bool addApproovTraceIDHeader)`
- `AddOptionalHeaders(params string[] headers)`

The generated default factory:

- uses install signing with `ecdsa-p256-sha256`
- signs `@method` and `@target-uri`
- includes the actual Approov token header and trace header when present
- includes optional `Authorization`, `Content-Length`, and `Content-Type`
- adds `created` and `expires`
- adds `Content-Digest` with `sha-256` when the body is readable

## Exceptions

`ApproovException` is the base exception type for package-originated failures.

Subclasses:

- `InitializationFailureException`
- `ConfigurationFailureException`
- `PinningErrorException`
- `NetworkingErrorException`
- `PermanentException`
- `RejectionException`

Important properties and fields:

- `ApproovException.ShouldRetry`
  Signals whether the failure should generally be treated as retryable.
- `RejectionException.ARC`
  The Approov rejection code returned by the SDK.
- `RejectionException.RejectionReasons`
  Optional structured rejection detail supplied by the SDK.

## Additional Public Types

### ApproovLogLevel

`ApproovLogLevel` controls service-layer diagnostics:

- `Off`
- `Error`
- `Warning`
- `Trace`

### ApproovTokenFetchStatus

`ApproovTokenFetchStatus` is the normalized status enum used in mutator hooks and explicit fetch results:

- `Success`
- `NoNetwork`
- `MITMDetected`
- `PoorNetwork`
- `NoApproovService`
- `BadURL`
- `UnknownURL`
- `UnprotectedURL`
- `NotInitialized`
- `NoNetworkPermission`
- `MissingLibDependency`
- `Rejected`
- `Disabled`
- `UnknownKey`
- `BadKey`
- `BadPayload`
- `InternalError`

### ApproovTokenFetchResult

`ApproovTokenFetchResult` is surfaced to mutator hooks and contains:

- `status`
- `ARC`
- `isForceApplyPins`
- `token`
- `traceID`
- `rejectionReasons`
- `isConfigChanged`
- `secureString`
- `measurementConfig`
- `loggableToken`

### KeyValuePair

`Approov.KeyValuePair` exists as a simple DTO to support `JsonUtility` deserialization when callers choose to parse the JSON returned by `GetPinsJSON(...)`.

## Internal Bridge

The native bridge is an internal package detail. It is intentionally not part of the supported API contract.

## Platform Requirements

- Android builds require `minSdkVersion` 25 or higher because the packaged Android integration targets API 25+.
- The upstream Approov iOS SDK requires iOS 12 or higher.
