# Foam DA

A Windows companion and offline reference library for Dark Ages. Foam DA combines an expanded game view, local reference search, maps, character switching, hotkeys, skill planning, and screenshots in one desktop host.

## Build and run

Requires Windows, Node.js 24 or newer, Python 3.11 or newer, the .NET Framework 4.x compiler, and Microsoft Edge WebView2 Runtime. Install Dark Ages separately. Client preparation checks the supported 7.41 executable hash before making an isolated local copy.

From this folder, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
```

This installs Python and JavaScript dependencies, prepares the local game copy and theme, builds the offline library, and compiles `prototype/widescreen/Foam-DA.exe`. The original game installation is preserved. The first build needs internet access for dependencies; normal reference browsing uses local files.

The GitHub repository keeps offline data and artwork in `resources/reference-*.zip`. `Build.ps1` restores these automatically. To unpack them without building, run `powershell -NoProfile -ExecutionPolicy Bypass -File .\Restore-Resources.ps1`. All source code remains directly browsable in the repository.

To select a different game installation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -GamePath 'D:\Games\Dark Ages'
```

Double-click **Open Foam DA.cmd** after building. For the reference website alone, run `npm run preview` in `site` and open http://127.0.0.1:4173/.

## Source layout

- `prototype/widescreen/`: C# desktop host, rendering, maps, local preferences, client preparation, and checks.
- `site/`: React and TypeScript reference website, normalized content, artwork, search, calculators, and content processing scripts.
- `requirements.txt`: Python build dependencies.
- `THIRD-PARTY-NOTICES.txt`: dependency and reference acknowledgements.

Game executables, game archives, account data, saved profiles, and release signing material are not part of the source package. Theme assets are generated from your installed client. Older local profile identifiers remain for compatibility.

## Checks

```powershell
cd site
npm run typecheck
npm test
npm run content:validate
cd ..\prototype\widescreen
.\Foam-DA.exe --host-checks
.\Foam-DA.exe --minimap-repaint-checks
.\Foam-DA.exe --macro-scheduling-checks
python -m unittest test_prototype test_popup_centering test_endpoint_patch
```

The Python client checks require the supported installed game client. The reference snapshot has incomplete coverage; unavailable facts and unresolved source differences remain marked in the data. The expanded view remains dependent on client compatibility and information supplied by the game server.

## Shout-outs and credits

Huge thanks to **Silo**, **vamistle**, **Vorlof**, and **Biomagus**, and to everyone who has shared tools, research, and knowledge with the Dark Ages community. Their work helped make production possible.

Additional thanks to:

- **Erik / Silo (Eric W. Rogers)** for SleepHunter reference material and public Dark Ages client research.
- **Vorlof** for community reference information and catalogs.
- **Russell / Sostratos** for the DA-Items archive.
- **Wanderlust contributors** and **DA-Wizard contributors** for reference information used to compare game facts.
- The maintainers of **React**, **TypeScript**, **Vinext**, **Vite**, **Pagefind**, **WebView2**, **Pillow**, **fontTools**, **pefile**, and the other dependencies used by this project.

These acknowledgements do not imply endorsement. Reference provenance remains with the content records. Required license notices are retained in `THIRD-PARTY-NOTICES.txt` and the included vendor license.

Dark Ages and its game artwork belong to their respective owners, including KRU Interactive. This is an independent community project. No license to third-party game assets is granted by this repository.

Measured package checks are recorded in `VERIFICATION.md`.
