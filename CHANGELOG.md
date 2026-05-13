# Changelog

All notable changes to this package are documented in this file.

The format is based on Keep a Changelog and this package follows Semantic Versioning.

## [Unreleased]

### Fixed

- `ApproovService.SendWebRequest(...)` now runs token fetch and request mutation work through a coroutine-backed background task before dispatch.
- `ApproovWebRequest` has been removed so integrations use the safer service-owned send path.
- certificate validation now fails closed if an Approov certificate handler is invoked before SDK initialization.
- native request mutation is serialized while using SDK-global token-binding state.
- Android minimum SDK enforcement now uses API 25 consistently with current Unity Android support.
- fallback integer token-status mapping now preserves `NOT_INITIALIZED` instead of shifting later statuses.
- `ApproovService.Prefetch()` no longer fetches a hard-coded domain and background prefetch failures are logged instead of being left as unobserved task exceptions.
- UnityWebRequest mutator hooks now run on the coroutine path while native fetches stay off-thread, and custom mutators can request additional Unity headers to capture.
- certificate pin validation now preserves the request authority so non-default HTTPS ports validate against the correct endpoint.

## [1.0.0] - 2026-04-16

### Added

- `ApproovServiceMutator`, `ApproovRequestContext`, and `ApproovRequestMutations` for request-path customization without forking
- `ApproovDefaultMessageSigning` with RFC 9421 `Signature` / `Signature-Input` header generation
- explicit account/install signing APIs on `ApproovService`
- `SetUseApproovStatusIfNoToken(...)` and mutator-driven request fallback behavior
- package tests covering default mutator policy and request-signing wire format

### Changed

- request processing now routes token/substitution decisions through the active service mutator
- `UNPROTECTED_URL` now skips secure-string substitution and signing by default
- pinning can now be disabled per request through `ShouldProcessPinning(...)`
- iOS certificate cache initialization is now safe for first use and native startup
- Android bridge-class initialization is now synchronized to avoid first-access races
- managed Approov initialization state is now committed atomically after native SDK startup

## [0.1.0] - 2026-03-19

### Added

- Initial UPM package release for the Approov Unity service layer
- Project-based Approov config string settings UI
- iOS SDK installer workflow from the Unity editor
- UnityWebRequest integration via `ApproovService.SendWebRequest(...)`
- `HttpClient` integration via `ApproovHttpClientHandler`
- Approov token injection and dynamic certificate pinning
- Secure string substitution for headers and query parameters
- Approov TraceID header support
- Detailed trace logging controls for debugging integrations
- Shapes sample and package documentation
