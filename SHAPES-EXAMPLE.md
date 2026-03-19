# Shapes Example

The Shapes example is now distributed as a Package Manager sample.

## Use it

1. Install `io.approov.service.unity` from its Git URL.
2. Import the `Shapes App` sample from the Package Manager.
3. Open `Assets/Samples/Approov Unity Service Layer/<package-version>/Shapes App/Scenes/SampleScene.unity`.
4. Open `Tools/Approov/Approov Settings` and paste the config string from `approov sdk -getConfigString`.
5. Use the same settings window to install the iOS SDK if you are targeting iOS.

Unity hides `Samples~` inside the installed package, so the sample scene will not appear directly under `Packages/io.approov.service.unity`.

The sample now uses the package runtime directly and no longer relies on the legacy copy-`Assets` quickstart flow.
