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
2. For iOS, install the native SDK through `Tools/Approov/Install iOS SDK` or `Tools/Approov/Approov Settings`.
3. For Android, no manual `mainTemplate.gradle` or manifest edits are required. The packaged Android library resolves the Approov SDK and OkHttp from Maven automatically.

## Initialize

```csharp
using Approov;

ApproovService.Initialize("<your-config-string>");
```

## Use With UnityWebRequest

```csharp
using Approov;

ApproovWebRequest request = ApproovWebRequest.Get("https://approov.io");
```

## Use With HttpClient

```csharp
using Approov;
using System.Net.Http;

HttpClient client = ApproovService.CreateHttpClient();
HttpResponseMessage response = await client.GetAsync("https://approov.io");
```

## Supported Surface

- `ApproovService`
- `ApproovWebRequest`
- `ApproovHttpClientHandler`
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
- Android min SDK defaults to 23 through the packaged Android library
- iOS uses `Approov.xcframework`
