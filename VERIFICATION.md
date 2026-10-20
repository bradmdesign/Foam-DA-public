# Verification

Checked on Windows on October 20, 2026.

- Standalone x86 C# launcher compilation passed. Two unused-field compiler warnings remain.
- Reference website: all 62 tests passed.
- Content validation: 15,835 entries and 41,670 relations passed, with no pending editorial review or unrepresented cached sources.
- Launcher host checks passed: 36 geometry cases, 2 pixel cases, 6 scaling cases, 4 pixel-edge cases, and 550 companion cases.
- Minimap repaint and macro scheduling checks passed.
- Client preparation and popup checks: 22 Python tests passed against the supported installed game client.
- Endpoint patch disassembly: 2 Python tests passed against the fixture produced by the launcher checks.

JavaScript checks used the existing local dependency installation. A fresh dependency install and a complete production website export were not completed for this package. Game runtime data and generated client fixtures are local build outputs and are excluded from the source archive.
