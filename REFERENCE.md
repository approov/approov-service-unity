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

`ApproovWebRequest` subclasses `UnityWebRequest` and applies Approov request mutation plus dynamic certificate validation before dispatch.

Use its static constructors such as:

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
