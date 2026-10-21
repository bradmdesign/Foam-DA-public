# Standalone release verification — October 21, 2026

- Full clean dependency installation and production Library build completed: 15,869 prerendered routes, 15,835 validated records, 41,670 relations, and a complete Pagefind search index.
- All 16 updater/HTTPS tests pass, including the new bundled-Library baseline and binary Pagefind search assets. Production TLS validation is unchanged.
- Native host rebuilt; its recorded 598 checks pass, and the update settings preview renders within its bounds.
- App revision 3 and Library revision 1 were signed with the existing protected publisher key and extracted with per-file verification before release. Ads revision 1 is empty.
- The standalone installer uses an explicit Lite inventory, a publisher-signed file report, a payload checksum, and atomic staging into a new empty folder. It includes the Microsoft-signed offline WebView2 prerequisite and bundled Node runtime. Game archives, sessions, user settings and signing secrets are excluded.
- GitHub releases name the source commit used by setup; setup-report.json records the same commit and all installed hashes. The release includes an external SHA-256 checksum for the downloadable EXE.

Earlier verification history follows. Older notes about the missing export or unavailable update host describe the earlier packages, not this release.

# Verification

Checked on Windows on October 20, 2026.

## Local-install Lite update

- Compact game recipes: 1,740,814 bytes for the three supported viewports, replacing full shipped client/profile files.
- Independent C# reconstruction: 19 checks passed, including matching both target hashes for all three profiles, cache reuse, rejection of mismatched/truncated inputs, and unchanged original game hashes.
- Host checks passed after removing the generated `runtime` and full `build/profiles` copies: 36 geometry, 2 pixel, 6 scaling, 4 pixel-edge and 550 companion cases. Minimap repaint and macro scheduling checks passed.
- Python delta checks: 4 passed, including relocated archive members, zero-length members, wrong input hashes and invalid copy bounds.
- Combined Python preparation/popup/delta suite: 26 tests passed after the changes.
- The removed runtime's 1,171 files were verified against the original installation or its prepared manifest before removal. Original game files, personal profiles and screenshots were retained.
- A fresh offline website dependency install encountered insufficient disk space. Its incomplete dependency output was removed. The website source was unchanged by this update; a new complete standalone Library export has not been built. The separate host-update package requires an existing Library installation.
- `releases/Foam-DA-Lite-HostUpdate.zip`: 2,276,764 bytes, 26 entries; inventory, per-file hashes and ZIP CRC checks passed. No game executables/DAT archives, full viewport profiles, sessions or user data are included. WebView2 license/notice files are included. SHA-256: `594d86685bcd420fe1cd8d7f9b2b976a48c6b134e231507c3b2f35cc495ab4bb`.

## Earlier source-package checks

- Standalone x86 C# launcher compilation passed. Two unused-field compiler warnings remain.
- Reference website: all 62 tests passed.
- Content validation: 15,835 entries and 41,670 relations passed, with no pending editorial review or unrepresented cached sources.
- Launcher host checks passed: 36 geometry cases, 2 pixel cases, 6 scaling cases, 4 pixel-edge cases, and 550 companion cases.
- Minimap repaint and macro scheduling checks passed.
- Client preparation and popup checks: 22 Python tests passed against the supported installed game client.
- Endpoint patch disassembly: 2 Python tests passed against the fixture produced by the launcher checks.

JavaScript checks used the existing local dependency installation. A fresh dependency install and a complete production website export were not completed for this package. Game runtime data and generated client fixtures are local build outputs and are excluded from the source archive.
# Signed updater verification — October 20, 2026

- Native host rebuilt and installed in the local source folder. Existing host regression checks passed: 36 geometry, 2 pixel, 6 scaling, 4 pixel-edge and 550 companion cases (598 total).
- 15 updater/HTTPS checks passed, covering signature and checksum rejection, path restrictions, truncated downloads, component activation, replay protection, locks, current/previous content retention, app staging, recovery, damaged-cache repair and fresh-install baselines. HTTPS transport checks use a disposable local CA through Node's ordinary trust configuration; production has no certificate-validation bypass.
- The real Windows-protected publisher key signed the prepared channel. Its App revision 2 was downloaded, verified and installed into a disposable fixture; all 27 app files matched and the fixture's private screenshot remained untouched. Ads revision 1 is intentionally empty. No Library release was built: the full static export still requires its normal website build.
- New local server switched Library content and ads without restarting. Deleted pages did not fall through to obsolete bundled records. Gzip-only exports and older page assets remain supported.
- Isolated headless-browser checks rendered the sponsored strip at 600×260 and 1200×700. The native Updates dialog passed bounds checks and was visually inspected. No game or player browser tab was controlled by these checks.
- `releases/Foam-DA-Lite-Updater-2026.10.20.zip`: 2,295,374 bytes; SHA-256 `ffa43930ac4c9396c483b3abdd18452a4b271de05cc04fc0f445105157ba9c5d`. Existing Library/Node runtime required; no complete game, player profiles, captures, publisher tools or signing material bundled.
- DigitalOcean upload script passed signature/checksum validation and preview. Existing protected project SSH connected successfully. The temporary root migration identity was rejected; its recorded expiry is October 17. Live upload and Nginx setup await renewed administrator access; no remote files or services changed during verification.
