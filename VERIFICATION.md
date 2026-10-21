# Verification — October 21, 2026

- Native host: 598 recorded checks pass (36 geometry, 2 pixel, 6 scaling, 4 pixel-edge, 550 companion).
- Updater: 18 checks pass, including signed feeds, strict file inventories, interrupted downloads, application recovery, mandatory startup gating, offline access with no known pending update, and persistence of a required version before its package finishes downloading.
- Publisher release resume: 5 checks pass, including owner drafts that GitHub's tag endpoint does not return and pagination beyond the first release page.
- Public Library build: 15,869 prerendered routes; 15,835 records and 41,670 relations validated; complete local search index and artwork included.
- New demo images are the user's supplied application screenshots, copied without alteration. They show the legitimate host with its bottom-right Library.
- The update settings screen has no opt-out checkbox. Its native preview checks that all controls fit within the window.
- App packages are signed with the existing Windows-protected publisher key, extracted and hash-verified locally before uploading. Published installer reports record their exact source commit and every payload hash.
- Release 0.3.0 passed full installation into an empty test folder, rejection of an occupied folder, all 50,775 payload hashes, and bundled runtime execution. Its anonymous GitHub download digest and executable header/footer matched the tested EXE.
- Each new installer release is separately verified before publication; see its attached verification report for the exact version and checksum.

Game archives, active sessions, saved accounts, browser profiles and private signing material are excluded from the source and distributable inventory.
