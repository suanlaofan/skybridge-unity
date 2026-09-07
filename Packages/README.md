# Local SDK dependencies

Use Unity **6000.5.4f1** with Android Build Support, SDK/NDK and OpenJDK.

Before opening a fresh clone:

1. Obtain the **PICO XR Unity SDK 0.13.1** from PICO's developer SDK distribution and copy its package contents into `Packages/com.bytedance.pico.xr/`. The folder must contain `package.json` with version `0.13.1`. This proprietary SDK is installed locally and is not redistributed as repository source.
2. The development workspace also uses **MCP for Unity 10.1.0**. Install its package into `Packages/com.coplaydev.unity-mcp/` from [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp). It is optional for game development: if it is not needed, remove the `com.coplaydev.unity-mcp` entry from `manifest.json`; Unity regenerates the lockfile on import.
3. Open the project, allow Unity to resolve the remaining registry packages, then open `Assets/Scenes/SkybridgeMain.unity`.

Both local package references are project-relative. Do not copy machine-specific absolute SDK paths into the manifest. Build the APK using **Skybridge > PICO > Build Android APK**; this also applies the game artwork to the default, legacy, round and adaptive Android icon slots.

The release APK already contains the required runtime libraries and does not require these development packages to install.
