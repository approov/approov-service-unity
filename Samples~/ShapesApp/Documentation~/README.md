# Shapes App Sample

This sample now contains two scenes:

- `Scenes/SampleScene.unity`: the original Shapes demo backend sample
- `Scenes/MessageSigningHarnessScene.unity`: a dedicated internal test harness for install message-signing verification against a verifier worker

The Shapes scene demonstrates the Shapes demo backend across both `UnityWebRequest` and `HttpClient`, with runtime controls for Approov, endpoint version, message-signature mode, and a built-in automated scenario runner.
The message-signing harness scene is test-only infrastructure. It is not part of the public Shapes example and should not be treated as the package's primary sample flow.

## Setup

1. Import the sample from Package Manager.
2. For the public example flow, open:
   `Assets/Samples/Approov Unity Service Layer/<package-version>/Shapes App/Scenes/SampleScene.unity`.
3. Open `MessageSigningHarnessScene.unity` only when you are doing internal message-signing verification against the worker.
4. Open `Tools/Approov/Approov Settings` and paste the config string from `approov sdk -getConfigString`.
5. If you are building for Android, no extra Approov SDK download is required. The package resolves the Android Approov SDK from Maven automatically.
6. If you are building for iOS, install `Approov.xcframework` from the Unity menu before building:
   `Tools/Approov/Install iOS SDK` for the latest release, or `Tools/Approov/Approov Settings` to pin a version.
7. Build for Android or iOS.

Do not ship a production app with an Approov dev key. A dev key is for controlled development and test flows only, and including it in a production build causes attestation to pass when it should not.

The sample uses the package runtime only and does not require extra JSON packages.

## Endpoint Modes

- `v1`: `Hello` is public and `Shapes` requires the `Api-Key` header only.
- `v3`: `Hello` is public and `Shapes` requires an Approov token.
- `v5`: `Hello` is public and `Shapes` uses `Api-Key` plus an Approov token, with this sample also using it as the message-signing validation flow.

The endpoint description shown in the sample UI is based on the observed behavior of the demo API on April 1, 2026.

## Runtime Controls

- `Approov Enabled`: turns Approov processing on or off for the outgoing request. If initialization fails, the sample shows the failure and does not silently downgrade.
- `Transport`: switch between `UnityWebRequest` and `HttpClient`.
- `Endpoint`: choose `v1`, `v3`, or `v5`.
- `Signature`: choose `None`, `Install`, or `Account`. Signature mode is only applied to `v5 /shapes/`.
- `Run Auto Test`: runs the full supported matrix across transports, endpoint versions, Approov on/off, and the v5 signature modes, then prints expected-versus-actual results in the Unity console and on screen.

When `Signature` is set to `Install` or `Account`, the sample installs a sample-specific mutator that signs only the protected `v5 /shapes/` request. The `Hello` request always stays unsigned so it remains a clean health check.

The sample also enables detailed service-layer trace logging by default so that request mutation, token fetch, signing, and transport errors are visible in the console while you exercise the flows.
It also writes a persistent diagnostics file named `approov-shapes-diagnostics.log` under `Application.persistentDataPath`, and the sample logs that full path at startup.

For a Cloudflare-worker-based end-to-end verifier setup, see [docs/message-signing-e2e-testing.md](../../../docs/message-signing-e2e-testing.md).

## Message Signing Harness Scene

This scene exists only to exercise and diagnose message-signing behavior against a controlled verifier backend. It is not part of the published Shapes sample workflow.

Use `MessageSigningHarnessScene.unity` when you want to validate install message signatures against the verifier worker instead of the Shapes backend.
The checked-in harness does not ship with a prefilled worker URL or dev key. Provide those only in your internal test environment.

The harness scene provides:

- a runtime `Worker URL` field
- `Approov Enabled` and transport selection
- `None` versus `Install` signature modes
- a manual `Call Worker` path to inspect the raw worker response
- a `Run Current Test` path that validates the worker result against the selected scenario
- an automated matrix across transport, Approov on/off, and install-signature modes

Expected harness outcomes:

- `Approov Off`: worker should return `tokenResult=MISSING_HEADER`
- `Approov On + None`: worker should return `tokenResult=PASS` and `messageSigningResult=MISSING_HEADERS`
- `Approov On + Install`: worker should return `tokenResult=PASS` and `messageSigningResult=VALID`

## Expected Demo Flows

- `Approov Off + v1`: `Shapes` should still return a shape because only the API key is required.
- `Approov Off + v3`: `Shapes` should fail because the Approov token is missing.
- `Approov On + v3`: `Shapes` should return a shape when the app is correctly configured with Approov.
- `Approov Off + v5`: `Shapes` should fail because the Approov token is missing. The sample still sends the `Api-Key` header for v5.
- `Approov On + v5 + None`: `Shapes` should fail with a message-signature error.
- `Approov On + v5 + Install` or `Account`: `Shapes` should return a shape when the selected signature type is available and validates successfully.
