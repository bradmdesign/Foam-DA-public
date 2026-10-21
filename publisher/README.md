# Publish Foam DA updates

The public channel is **https://github.com/bradmdesign/Foam-DA-public/releases/download/updates/feed.json**. GitHub hosts the signed feed, manifests and update packages as public release assets. Publishing remains an explicit owner action; editing source alone does not change players’ records or ads. The separate installer release stays the latest stable release; the `updates` tag is marked as a prerelease.

Players use **Settings → Updates**. Signed updates are checked at startup and hourly. Library records and sponsored messages activate without closing the game. An already-open page offers **Refresh** when its records change, preserving the page until the player is ready. Ads refresh within one minute. App updates download in the background; **Install app update + restart** applies them and reattaches running games. Macros stop during a host restart.

## First setup

`Publish.ps1 -Component Init` creates a Windows-protected RSA-3072 publisher key outside this project, under `%LOCALAPPDATA%/Foam-DA/Publisher`. This has already been done on this PC. Only the public key and channel URL ship with the app. Keep the protected key and its Windows profile backed up; losing them prevents publishing compatible updates. Running Init again preserves the existing key. No passwords or player data are sent to the update host.

Run these commands from the Foam-DA folder using Windows PowerShell. Each component has its own increasing numeric **Revision**. **Version** is its human-readable label. Never reuse a published revision or replace an immutable release file.

The first standalone installer bundles **App revision 3** (version 0.3.0) and **Library revision 1**, with **Ads revision 1** intentionally empty. Next revisions are App **4**, Library **2**, and Ads **2**. App and Library baselines are recorded in the installer to avoid downloading the same files again on first launch.

## Change an ad

Edit `publisher/ads/campaigns.json`. Empty `campaigns` means no ads. Example:

```json
{
  "schema": 1,
  "campaigns": [
    {
      "id": "beardog",
      "title": "BearDog Games",
      "text": "News from the studio",
      "href": "https://beardog.games/",
      "image": "images/beardog.png",
      "starts": "2026-11-01T00:00:00Z",
      "ends": "2026-12-01T00:00:00Z"
    }
  ]
}
```

Image, link, dates and text are optional. Put referenced images in `publisher/ads/images/`; use PNG, JPEG, WebP or GIF under 2 MB. Ads are data, not HTML or JavaScript. Active ads rotate once per minute in a compact themed strip, including the bottom-right Library. There are no tracking pixels, third-party scripts, impression uploads or forced navigation. An explicit click opens the configured HTTPS link in the default browser.

```powershell
.\publisher\Publish.ps1 -Component Ads -Version 2026.10.21 -Revision 2
```

To remove ads, clear the array and publish a newer Ads revision. Changing ads does not redistribute the Library or the game host.

## Correct Library records

Edit the website's source records and rebuild the public offline export, then publish it:

```powershell
cd site
npm run build
cd ..
.\publisher\Publish.ps1 -Component Library -Version 2026.10.22 -Revision 2
```

The full public snapshot includes its search index, pages, icons and sprites. This makes skipped releases safe to install and keeps search consistent with records. Only this component downloads; the game and private files are excluded. The current and previous Library/ad versions remain on disk; older recognized content caches are retired after a successful update. App backups remain available under `updates` for recovery.

## Update the desktop host

Build and test the host, then:

```powershell
.\publisher\Publish.ps1 -Component App -Version 0.3.1 -Revision 4
```

This packages an explicit list of app files, scripts, skin assets and compact preparation recipes. It never includes the installed Dark Ages game, sessions, screenshots, characters, browser cache or signing tools. The updater verifies the signed feed and every file before installation; keeps replaced app files; and journals installation so an interrupted replacement can be restored on next launch. If another host is open, close it before installing an app update.

## Put the release online

The command verifies the package and writes **publisher/output**. Upload the new `.fup` package and its component `.json` manifest first. Upload **feed.json** and **feed.json.sig** last. Keep the existing immutable release files. A brief mismatch between the feed/signature during upload is rejected and retried on a later check; existing content keeps working.

Serve `feed.json` and `feed.json.sig` with `Cache-Control: no-cache`. Immutable revision files may use a long cache lifetime. Deploying the folder atomically is preferable. The updater follows HTTPS redirects, so signed files can live on GitHub Releases or a CDN while beardog.games provides the feed. HTTPS credentials, if any, stay in your hosting/deployment tools, never in the app or feed.

An initial app and empty-ad release can be prepared locally before configuring hosting. A Library release still requires the public site export. The checked-in source is not automatically published when edited.

## Publish to GitHub

`github_release.py` uses the owner's existing Git Credential Manager login in memory, only against the GitHub API and upload hosts, and is restricted to `bradmdesign/Foam-DA-public`. It never stores credentials in the project or uploads them. After preparing and checking component files, publish all current output assets:

```powershell
$assets=Get-ChildItem publisher/output -File | Where-Object { $_.Extension -in '.fup','.json','.sig' } | ForEach-Object { '--asset'; $_.FullName }
python publisher/github_release.py release --tag updates --commit YOUR_SOURCE_COMMIT --title "Foam DA signed update channel" --notes publisher/update-notes.md --prerelease @assets
```

The helper uploads and checks immutable files first, then the signature and feed. A temporary feed/signature mismatch is rejected safely. Never delete the stable `updates` tag; never overwrite a different immutable asset. A feed refresh can briefly be delayed by GitHub caching, and offline checks leave the working Library available.

The optional `Deploy.ps1` and `nginx-updates.conf` support a future dedicated HTTPS static host. They are not required for the GitHub channel and do not change any other project’s services. If moving channels later, distribute a newly configured app explicitly; changing the publisher's local URL does not silently redirect already-installed clients.

## Checks

```powershell
node --test prototype/widescreen/updater/update.test.mjs prototype/widescreen/updater/transport.test.mjs
node publisher/check-channel.mjs
```

Tests use isolated fixtures and an ephemeral signing key. They cover signature rejection, unsafe paths, game/private-file exclusion, interrupted downloads, replay rejection, independent component activation, app staging/recovery, update locks, gzip-only exports, and live server content changes.
