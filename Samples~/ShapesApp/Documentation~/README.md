# Shapes App Sample

This sample demonstrates the Shapes demo backend across both `UnityWebRequest` and `HttpClient`, with runtime controls for Approov, endpoint version, and message-signature mode.

## Setup

1. Import the sample from Package Manager.
2. Open `Assets/Samples/Approov Unity Service Layer/<package-version>/Shapes App/Scenes/SampleScene.unity`.
3. Open `Tools/Approov/Approov Settings` and paste the config string from `approov sdk -getConfigString`.
4. Use the same window to install the iOS SDK if you are building for iOS.
5. Build for Android or iOS.

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

When `Signature` is set to `Install` or `Account`, the sample installs a sample-specific mutator that signs only the protected `v5 /shapes/` request. The `Hello` request always stays unsigned so it remains a clean health check.

## Expected Demo Flows

- `Approov Off + v1`: `Shapes` should still return a shape because only the API key is required.
- `Approov Off + v3`: `Shapes` should fail because the Approov token is missing.
- `Approov On + v3`: `Shapes` should return a shape when the app is correctly configured with Approov.
- `Approov On + v5 + None`: `Shapes` should fail because the message signature is missing.
- `Approov On + v5 + Install` or `Account`: `Shapes` should return a shape when the selected signature type is available and validates successfully.
