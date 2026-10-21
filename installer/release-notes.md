## Install Foam DA

Download **Foam-DA-Setup-0.3.0.exe**, run it and choose a new writable folder. On first launch, select your separately installed Dark Ages 7.41 folder.

- Complete offline Library, search, record icons and sprites.
- Expanded game view, character tabs, minimap, macros, hotkeys and screenshots.
- Signed automatic updates for the app, Library and optional sponsored messages.
- Bundled local Node runtime and Microsoft-signed WebView2 prerequisite. No Python, Node, Git, GitHub account or .NET SDK installation needed.
- No full game, user profiles, saved accounts, screenshots, browser sessions or private signing keys included.

The installer is built from this release's exact source commit. Its signed **setup-report.json** records that commit and every installed file hash. The companion **.sha256** file verifies the downloadable EXE. Foam DA does not yet have a Microsoft Authenticode certificate, so Windows may show an unknown-publisher prompt; the internal publisher signature verifies the installer payload and updates.

Supported systems: 64-bit Windows 10/11 with its .NET Framework support and a compatible, separately installed Dark Ages client. Select an empty install folder; existing installations use **Settings → Updates**.

[Screenshot demo](https://github.com/bradmdesign/Foam-DA-public/blob/main/docs/demo/README.md) · [Source and build instructions](https://github.com/bradmdesign/Foam-DA-public)
