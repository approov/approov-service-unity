# Reference

The supported public API for this package is the high-level Unity surface:

- `ApproovService`
- `ApproovWebRequest`
- `ApproovHttpClientHandler`
- `ApproovException` and its subclasses

## ApproovService

Use `ApproovService` to initialize the SDK and configure request behavior:

- `Initialize()` reads the config string from project settings
- `Initialize(string config)`
- `IsSDKInitialized()`
- `SetBindingHeader(string header)` / `GetBindingHeader()`
- `SetTokenHeaderAndPrefix(string header, string prefix)` / `GetTokenHeader()` / `GetTokenPrefix()`
- `SetApproovTraceIDHeader(string header)` / `GetApproovTraceIDHeader()`
- `SendWebRequest(UnityEngine.Networking.UnityWebRequest request)`
- `SetLoggingLevel(ApproovLogLevel level)` / `GetLoggingLevel()`
- `SetDetailedDebugLogging(bool enabled)` / `GetDetailedDebugLogging()`
- `SetProceedOnNetworkFailure(bool proceed)` / `GetProceedOnNetworkFailure()`
- `AddSubstitutionHeader(string header, string requiredPrefix)` / `RemoveSubstitutionHeader(string header)` / `GetSubstitutionHeaders()`
- `AddSubstitutionQueryParam(string key)` / `RemoveSubstitutionQueryParam(string key)` / `GetSubstitutionQueryParams()`
- `AddExclusionURLRegex(string urlRegex)` / `RemoveExclusionURLRegex(string urlRegex)` / `CheckURLIsExcluded(string url)`
- `SetUserProperty(string property)`
- `Prefetch()`
- `FetchSecureString(string key, string newDef)`
- `FetchCustomJWT(string payload)`
- `Precheck()`
- `GetDeviceID()`
- `SetDataHashInToken(string data)`
- `GetMessageSignature(string message)`
- `FetchToken(string url)`
- `GetPinsJSON(string pinType)`
- `FetchConfig()`
- `SetDevKey(string key)`
- `GetIntegrityMeasurementProof(byte[] nonce, byte[] measurementConfig)`
- `GetDeviceMeasurementProof(byte[] nonce, byte[] measurementConfig)`
- `CreateHttpClient()`
- `CreateHttpClient(System.Net.Http.HttpMessageHandler innerHandler)`
- `CreateHttpClientHandler()`

## UnityWebRequest Surface

The recommended UnityWebRequest integration is:

```csharp
UnityWebRequest request = UnityWebRequest.Get("https://example.com");
yield return ApproovService.SendWebRequest(request);
```

`ApproovService.SendWebRequest(...)` is the primary supported surface for UnityWebRequest-based integrations. It ensures Approov request mutation plus dynamic certificate validation are applied before dispatch.

`ApproovWebRequest` remains available as a compatibility helper type, but it is not the preferred API because Unity method hiding can bypass Approov processing if the request is later handled through a `UnityWebRequest` reference.

Compatibility helpers that remain available:

- `ApproovWebRequest.Get(...)`
- `ApproovWebRequest.Post(...)`
- `ApproovWebRequest.Put(...)`
- `ApproovWebRequest.Delete(...)`

## HttpClient Surface

`ApproovHttpClientHandler` is the `HttpClient` adapter. It applies the same token and secure-string substitution rules before send, and when its inner handler is an `HttpClientHandler` it also wires certificate validation through the Approov native bridge.

Recommended usage:

```csharp
HttpClient client = ApproovService.CreateHttpClient();
```

## Internal Bridge

The native bridge is an internal package detail. It is intentionally not part of the supported public API contract.
