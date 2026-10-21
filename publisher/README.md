# Publish Foam DA updates

The public signed feed is hosted at **https://github.com/bradmdesign/Foam-DA-public/releases/download/updates/feed.json**. Publishing is an explicit owner action; editing source alone does not change installed applications.

The host automatically checks for updates before opening and periodically while running. A pending app update blocks the old host until the signed replacement is installed. Game processes remain open and are reattached afterward. Library records update as a separate verified snapshot.

## Signing key

`Publish.ps1 -Component Init` initializes the publisher's Windows-protected RSA-3072 key outside the project. Only the public key is distributed. Preserve the original signing key and its Windows profile; never upload it or replace the pinned key in an existing channel. The updater sends no accounts, passwords or game data.

## Library records

Edit the records, rebuild the public export, then prepare a strictly increasing revision:

```powershell
cd site
npm run build
cd ..
.\publisher\Publish.ps1 -Component Library -Version 2026.10.22 -Revision 2
```

The snapshot includes pages, search, icons and sprites. It excludes game files and user data. Initial bundled versions are recorded so a new installation does not download the same Library again.

## App update

Build and check the host, then prepare its next revision:

```powershell
.\publisher\Publish.ps1 -Component App -Version 0.3.1 -Revision 4
```

App packages use an explicit allowlist and signed file hashes. Replacement is staged, verified and journaled. Interrupted changes are recovered before startup. User settings, screenshots, characters and game sessions are preserved.

## Publish

Run `node publisher/check-channel.mjs` to verify local output. Upload immutable component files first, followed by the feed signature and feed. Never overwrite a published revision. The GitHub helper checks the uploaded asset hashes, resumes an existing draft and refuses ambiguous drafts.

```powershell
$assets=Get-ChildItem publisher/output -File | Where-Object { $_.Extension -in '.fup','.json','.sig' } | ForEach-Object { '--asset'; $_.FullName }
python publisher/github_release.py release --tag updates --commit YOUR_SOURCE_COMMIT --title "Foam DA signed update channel" --notes publisher/update-notes.md --prerelease @assets
```

The helper uses the owner's existing Git Credential Manager session in memory and only sends authentication to GitHub API/upload hosts. It is restricted to this repository. The `updates` prerelease stays separate from the latest stable Windows installer.

## Checks

```powershell
node --test prototype/widescreen/updater/update.test.mjs prototype/widescreen/updater/transport.test.mjs
python -m unittest discover -s publisher -p test_github_release.py
```
