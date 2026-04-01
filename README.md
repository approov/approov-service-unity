# Approov Unity Service

`io.approov.service.unity` is a Unity 6+ UPM package that adds Approov protection to both `UnityWebRequest` and `HttpClient`.

## Install

Add the package from a Git URL in Unity Package Manager:

```text
https://github.com/approov/approov-service-unity.git
```

After installing the package:

1. Import the `Shapes App` sample from the Package Manager if you want the demo content.
   The sample scene is not shown directly under `Packages/io.approov.service.unity` because Unity hides `Samples~` in installed packages.
   After import, open `Assets/Samples/Approov Unity Service Layer/<package-version>/Shapes App/Scenes/SampleScene.unity`.
2. Open `Tools/Approov/Approov Settings` and paste the config string from `approov sdk -getConfigString`.
   Use the same window to install the native iOS SDK if you are targeting iOS.
3. For Android, no manual `mainTemplate.gradle` or manifest edits are required. The packaged Android library resolves the Approov SDK and OkHttp from Maven automatically.
   The Android project minimum API level must be set to 23 or higher.

## Initialize

```csharp
using Approov;

ApproovService.Initialize();
```

The parameterless initializer reads the config string from project settings. Native initialization only runs on iOS and Android player builds.

## Use With UnityWebRequest

```csharp
using Approov;
using UnityEngine.Networking;

UnityWebRequest request = UnityWebRequest.Get("https://approov.io");
yield return ApproovService.SendWebRequest(request);
```

Use `ApproovService.SendWebRequest(...)` as the primary UnityWebRequest integration surface. It applies request mutation, token injection, secure-string substitution, and Approov certificate validation before dispatch.

`ApproovWebRequest` remains available for compatibility, but it should not be the primary documented path because Unity method hiding can bypass Approov processing if the instance is handled through a `UnityWebRequest` reference.

## Use With HttpClient

```csharp
using Approov;
using System.Net.Http;

HttpClient client = ApproovService.CreateHttpClient();
HttpResponseMessage response = await client.GetAsync("https://approov.io");
```

## Message Signing And Mutators

The runtime now exposes an `ApproovServiceMutator` hook point so request policy can be customized without forking the package. Install a mutator once during app startup:

```csharp
ApproovService.SetServiceMutator(new MyMutator());
```

To enable default RFC 9421 request signing with installation keys:

```csharp
ApproovDefaultMessageSigning signer = new ApproovDefaultMessageSigning()
    .SetDefaultFactory(ApproovDefaultMessageSigning.GenerateDefaultSignatureParametersFactory());

ApproovService.SetServiceMutator(signer);
```

The default signer adds `Signature` and `Signature-Input` headers only after an Approov token has been added to the request. It signs `@method`, `@target-uri`, the Approov token header, the optional trace header, selected request headers, and `Content-Digest` when the body is readable.

For direct use of the underlying SDK signing primitives:

```csharp
string accountSignature = ApproovService.GetAccountMessageSignature(message);
string installSignature = ApproovService.GetInstallMessageSignature(message);
```

## Supported Surface

- `ApproovService`
- `ApproovWebRequest`
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

Use `Tools/Approov/Approov Settings` to install the latest release, install a specific version, or reinstall the pinned version.

## Repository Role

This repository is the package-first home for the shared Approov Unity service layer. It replaces the old quickstart-style repo layout with a reusable package that can be consumed directly from GitHub and used across the main Unity networking surfaces.

## Migration From The Old Quickstart

The old flow asked users to copy `Assets/` into their project and manually fetch native binaries. The package now owns that setup:

- runtime code lives inside the UPM package
- Android dependencies are resolved automatically through Gradle/Maven
- iOS uses the built-in Unity editor installer for `Approov.xcframework`
- the demo is distributed as a Package Manager sample instead of a separate copy step

## Platform Notes

- Unity 6+ only
- Android builds require project min SDK 23 or higher
- iOS uses `Approov.xcframework`
