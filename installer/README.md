# Standalone Windows installer

Players download **Foam-DA-Setup-0.3.0.exe** from [GitHub Releases](https://github.com/bradmdesign/Foam-DA-public/releases/latest). No GitHub account, Python, Node, Git or .NET SDK is needed. Supported 64-bit Windows 10/11 provides .NET Framework; the installer includes the offline Microsoft WebView2 prerequisite and uses it only when needed.

Choose a new empty writable folder. Setup verifies the publisher-signed inventory and every file, stages installation beside the destination, and creates an optional desktop shortcut. On first launch, choose the separately installed Dark Ages 7.41 folder. The game itself is not included. Existing installations receive later changes through **Settings → Updates**.

The complete offline Library, icons, search index, local Node runtime, desktop host, compact game recipes, updater and dependency notices are included. No user accounts, screenshots, saved settings, sessions, browser profiles, private keys or full game archives are packaged. Screenshots are saved locally beside the app.

The publisher's RSA signature protects the payload inventory and updates. It is separate from Microsoft Authenticode: the Foam DA EXE does not currently have a commercial Authenticode certificate, so Windows may show an unknown-publisher warning. The included Microsoft prerequisite retains its Microsoft signature.

## Build the same release from source

Build and check the application using the root README. Initialize your own publisher channel for a fork; the official signing key is never distributed. Publish the Library and App components before packaging so the channel records the exact bundled baselines. This prevents first launch from downloading the same Library again.

```powershell
python package_lite.py --output Foam-DA-Lite-0.3.0.zip
python installer/build_setup.py --archive releases/Foam-DA-Lite-0.3.0.zip --webview2 C:/Downloads/MicrosoftEdgeWebView2RuntimeInstallerX64.exe --version 0.3.0 --commit YOUR_PUBLISHED_40_CHARACTER_COMMIT
```

Obtain Microsoft's Evergreen Standalone Installer for x64 from the [official WebView2 distribution page](https://developer.microsoft.com/microsoft-edge/webview2/). The builder checks its Microsoft Authenticode signature. Build requirements remain Python 3.11+, Windows' .NET Framework compiler, and the publisher's local signing tool; players do not need these build tools.

`setup-report.json` records the exact source commit, each installed file's SHA-256 and the package version. `setup-report.sig` is verified against the embedded public key. The same commit is named by the GitHub release tag. The installer contains compiled/generated products of that source, with the public Library snapshot and runtime dependencies.

## Verify without opening a game

`Foam-DA-Setup-0.3.0.exe --verify-package` checks the signature, footer, inventory, and every payload hash. `--install-test C:/an-empty-test-folder` exercises the complete extraction and activation without installing prerequisites, creating shortcuts, or launching the app. It refuses nonempty destinations. `--ui-check C:/preview.png` renders the setup form without installing anything.

The installer deliberately never replaces a nonempty application folder. Use the signed updater for existing installations. Installed files live in the selected folder and may be removed after closing the host and its game tabs; no system-wide Foam DA service is installed.
