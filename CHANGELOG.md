# Changelog

All notable changes to this package are documented in this file.

The format is based on Keep a Changelog and this package follows Semantic Versioning.

## [Unreleased]

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
