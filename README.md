# Approov Unity Service

`io.approov.service.unity` is a Unity 2022 LTS and Unity 6000 UPM package that adds Approov protection to both `UnityWebRequest` and `HttpClient`.

## Install

Add the package from a Git URL in Unity Package Manager:

```text
https://github.com/approov/approov-service-unity.git
```

After installing the package, complete the project setup in Unity:

1. Import the `Shapes App` sample from the Package Manager if you want the demo content.
   The sample scene is not shown directly under `Packages/io.approov.service.unity` because Unity hides `Samples~` in installed packages.
   After import, open `Assets/Samples/Approov Unity Service Layer/<package-version>/Shapes App/Scenes/SampleScene.unity`.
2. Open `Tools/Approov/Approov Settings` and paste the config string from `approov sdk -getConfigString`.
   The editor stores the value in project settings and mirrors it into `Assets/Resources/Approov/ApproovConfig.txt` for player builds.
3. If you target Android, no manual Approov SDK download is required.
   The packaged Android library resolves `io.approov:approov-android-sdk` and `okhttp` from Maven automatically when Unity builds or exports the Gradle project.
4. If you target iOS, install the native SDK into the project from the Unity editor:
   Use `Tools/Approov/Install iOS SDK` to fetch the latest release immediately.
   Use `Tools/Approov/Approov Settings` to install the latest release, pin a specific release, or reinstall the pinned release.
   The installer downloads `Approov.xcframework` and places it at `Assets/Plugins/iOS/Approov.xcframework`.

This package intentionally differs from the raw Approov SDK integration guides:

- On Android, the package owns the native dependency wiring, so you do not fetch an `.aar` manually.
- On iOS, the package still requires an explicit native SDK fetch, but it is performed from the Unity `Tools/Approov` menu rather than by manually importing the XCFramework in Xcode.

Do not include an Approov dev key in a production app. Dev keys are for controlled development and testing only, and shipping one causes attestation to pass when it should not.

## Initialize

```csharp
using Approov;

ApproovService.Initialize();
```

Call `ApproovService.Initialize()` as early as possible during app startup, before the first protected network call.

The parameterless initializer reads the config string from the synced project asset. Native initialization only runs on iOS and Android player builds. In the Unity editor and desktop player builds, the config can exist in project settings but the native SDK remains disabled by design.

Initialization behavior in this package is:

- the first successful initialization locks the session to that config string
- a repeated call with the same config is ignored
- a repeated call with a different config throws a configuration failure

## Use With UnityWebRequest

```csharp
using Approov;
using UnityEngine.Networking;

UnityWebRequest request = UnityWebRequest.Get("https://approov.io");
yield return ApproovService.SendWebRequest(request);
```

Use `ApproovService.SendWebRequest(...)` as the primary UnityWebRequest integration surface. It applies request mutation, token injection, secure-string substitution, and Approov certificate validation before dispatch without blocking the Unity main thread during native token fetches.

## Use With HttpClient

```csharp
using Approov;
using System.Net.Http;

HttpClient client = ApproovService.CreateHttpClient();
HttpResponseMessage response = await client.GetAsync("https://approov.io");
```

`ApproovService.CreateHttpClient()` is the recommended `HttpClient` entry point. Use `CreateHttpClientHandler()` when you need to compose the handler into an existing pipeline yourself.

`ApproovHttpClientHandler` mutates outbound requests in the same way as the UnityWebRequest path. If its inner handler is an `HttpClientHandler`, it also hooks Approov pin validation into the TLS callback.

## Platform Setup Notes

### Android

- The Android bridge is packaged as `Plugins/Android/ApproovUnity.androidlib`.
- Its Gradle file declares the Approov Android SDK and OkHttp Maven dependencies for you.
- The package enforces Android `minSdkVersion` 25 or higher at build time.
- No manual `mainTemplate.gradle`, manifest, or `.aar` copy step is required for the Approov SDK itself.

### iOS

- The package expects `Assets/Plugins/iOS/Approov.xcframework` to exist in the Unity project.
- The built-in installer fetches that XCFramework from `approov/approov-ios-sdk` GitHub releases and configures the plugin importer for iOS only.
- Use the Unity menu to install it before building for iOS.
- The upstream Approov iOS SDK requirement remains iOS 12 or higher.

## Message Signing And Mutators

The runtime exposes an `ApproovServiceMutator` hook point so request policy can be customized without forking the package. Install a mutator once during app startup:

```csharp
ApproovService.SetServiceMutator(new MyMutator());
```

`MyMutator` should derive from `ApproovServiceMutator`. Override the hook methods your policy needs, typically `ShouldProcessRequest`, `HandleInterceptorFetchTokenResult`, `HandleHeaderSubstitutionResult`, `HandleQueryParamSubstitutionResult`, `HandleProcessedRequest`, and `ShouldProcessPinning`.

To enable default RFC 9421 request signing with installation keys:

```csharp
ApproovDefaultMessageSigning signer = new ApproovDefaultMessageSigning()
    .SetDefaultFactory(ApproovDefaultMessageSigning.GenerateDefaultSignatureParametersFactory());

ApproovService.SetServiceMutator(signer);
```

The default signer adds `Signature` and `Signature-Input` headers only after an Approov token has been added to the request. It signs `@method`, `@target-uri`, the Approov token header, the optional trace header, selected request headers, and `Content-Digest` when the body is readable.
If signing is configured for a tokenized request but the SDK cannot produce a signature, the request fails rather than being sent unsigned.

Use message signing only on routes whose backend verifier is configured for the chosen signing mode. The package can produce both install-key signatures and account-key signatures, but server-side verification material and acceptance policy are outside the package.

For internal end-to-end message-signing verification, including the dedicated test harness scene and verifier worker flow, see [docs/message-signing-e2e-testing.md](docs/message-signing-e2e-testing.md). The harness is test-only infrastructure and is not part of the public Shapes example flow.

For direct use of the underlying SDK signing primitives:

Pass `message` as the exact string payload to sign, typically a UTF-8 textual value such as a canonical request fragment or other plain-text payload. The runtime signs the raw string you provide, so do not pre-hash it and do not rely on implicit trimming; for example, `"orderId=12345&timestamp=1712345678"`.

```csharp
string accountSignature = ApproovService.GetAccountMessageSignature(message);
string installSignature = ApproovService.GetInstallMessageSignature(message);
```

## Supported Surface

- `ApproovService`
- `ApproovHttpClientHandler`
- `ApproovServiceMutator`
- `ApproovRequestContext`
- `ApproovRequestMutations`
- `ApproovDefaultMessageSigning`
- `ApproovException` hierarchy

Low-level native bridge details are package internals and are not the recommended integration surface.

## iOS SDK Installer

The package includes an editor installer that:

- queries GitHub Releases for `approov/approov-ios-sdk`
- downloads `Approov.xcframework`
- installs it into `Assets/Plugins/iOS/Approov.xcframework`
- pins the resolved release in project settings so the project is reproducible

Use `Tools/Approov/Install iOS SDK` for a one-click latest install, or use `Tools/Approov/Approov Settings` to install the latest release, install a specific version, or reinstall the pinned version.

## Repository Role

This repository is the package-first home for the shared Approov Unity service layer. It replaces the old quickstart-style repo layout with a reusable package that can be consumed directly from GitHub and used across the main Unity networking surfaces.

## Migration From The Old Quickstart

The old flow asked users to copy `Assets/` into their project and manually fetch native binaries. The package now owns that setup:

- runtime code lives inside the UPM package
- Android dependencies are resolved automatically through Gradle/Maven
- iOS uses the built-in Unity editor installer for `Approov.xcframework`
- the demo is distributed as a Package Manager sample instead of a separate copy step

## Platform Notes

- This package is intended only for mobile apps iOS/Android projects
- Unity 2022 LTS or Unity 6000
- Android builds require project min SDK 25 or higher
- iOS uses `Approov.xcframework`
