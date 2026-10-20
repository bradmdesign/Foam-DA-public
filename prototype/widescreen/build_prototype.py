"""Build an isolated Dark Ages widescreen experiment from a verified installation.

No injection, registry changes, or changes to the source installation. The copy's
startup dimensions, GUI bounds, and HUD assets select native expanded rendering.
Requires pefile (see requirements.txt).
"""
import argparse
import ctypes
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct
import sys

ROOT = Path(__file__).resolve().parent
import pefile
import popup_centering

CLIENT_HASH = "054a5d6adc56099c6bfd9d2a58675aff62dc788b63209a3d906492f5b89e96c6"
SOURCE_DEFAULT = Path(r"C:\Program Files (x86)\KRU\Dark Ages")
PRESENTATION_REVISION = 11


def validate_dimensions(width, height=480):
    # Experimental preparation envelope, not a claim of gameplay validation.
    if width < 640 or width > 1920 or width % 4:
        raise ValueError("Prototype width must be a multiple of 4 between 640 and 1920")
    if height < 480 or height > 1080 or height % 4:
        raise ValueError("Prototype height must be a multiple of 4 between 480 and 1080")


def sha(data):
    return hashlib.sha256(data).hexdigest()


def read_dat(data):
    if len(data) < 21:
        raise ValueError("DAT header truncated")
    count = struct.unpack_from("<I", data)[0]
    index_end = 4 + count * 17
    if count < 2 or index_end > len(data):
        raise ValueError("Invalid legacy DAT index")
    rows = []
    for i in range(count):
        offset, raw_name = struct.unpack_from("<I13s", data, 4 + i * 17)
        name = raw_name.split(b"\0", 1)[0]
        if offset < index_end or offset > len(data):
            raise ValueError("DAT offset out of range")
        if rows and offset < rows[-1][1]:
            raise ValueError("DAT offsets are not monotonic")
        rows.append((name, offset))
    if rows[-1][0]:
        raise ValueError("DAT missing sentinel")
    entries = {}
    for (name, begin), (_, end) in zip(rows, rows[1:]):
        key = name.decode("ascii")
        if not key or key.casefold() in {k.casefold() for k in entries}:
            raise ValueError("Duplicate or empty DAT name")
        entries[key] = data[begin:end]
    return entries


def write_dat(entries):
    ordered = sorted(entries.items(), key=lambda kv: kv[0].lower())
    count = len(ordered) + 1
    offset = 4 + count * 17
    index = bytearray(struct.pack("<I", count))
    payload = bytearray()
    for name, data in ordered:
        encoded = name.encode("ascii")
        if not encoded or len(encoded) > 12:
            raise ValueError("Invalid DAT name")
        index.extend(struct.pack("<I13s", offset, encoded))
        payload.extend(data)
        offset += len(data)
    index.extend(struct.pack("<I13s", offset, b""))
    return bytes(index + payload)


def patch_layout(data, width, filename, height=480):
    # Preserve every unrelated byte, including the original line endings/encoding.
    changes = []
    for control in (b"MAP", b"CAPTURE"):
        pattern = rb'(<NAME>\s*"' + control + rb'"\s*<TYPE>\s*7\s*<RECT>\s*)(\d+)\s+(\d+)\s+(\d+)\s+(\d+)'
        matches = list(re.finditer(pattern, data))
        if len(matches) != 1:
            raise ValueError("Expected one " + control.decode() + " in " + filename)
        m = matches[0]
        old = [int(m.group(i)) for i in range(2, 6)]
        if old[:3] != [3, 3, 619] or old[3] not in (311, 406, 427):
            raise ValueError("Unexpected layout rectangle: " + repr(old))
        new = [3, 3, 619 + width - 640, old[3] + height - 480]
        replacement = m.group(1) + " ".join(map(str, new)).encode("ascii")
        data = data[:m.start()] + replacement + data[m.end():]
        changes.append({"file": filename, "control": control.decode(), "old": old, "new": new})
    return data, changes


def change_rect(data, name, transform):
    pattern = rb'(<NAME>\s*"' + re.escape(name.encode()) + rb'"\s*<TYPE>\s*\d+\s*<RECT>\s*)(\d+)\s+(\d+)\s+(\d+)\s+(\d+)'
    matches = list(re.finditer(pattern, data))
    if len(matches) != 1:
        raise ValueError("Expected one rectangle for " + name)
    match = matches[0]
    old = [int(match.group(i)) for i in range(2, 6)]
    new = transform(old)
    result = data[:match.start()] + match.group(1) + " ".join(map(str, new)).encode() + data[match.end():]
    return result, {"control": name, "old": old, "new": new}


def widen_spf(data, width, hud_top=None, height=480):
    """Re-layout one verified indexed fullscreen SPF, without rescaling its art.

    Keep the two original palettes. Reuse an existing near-black opaque palette
    entry; index 0 remains transparent. Move the world-side border and translate
    the lower HUD without changing its pixels. The unknown/reserved fields stay.
    """
    validate_dimensions(width, height)
    if data[8:10] != b"\0\0" or len(data) < 1076:
        raise ValueError("Expected an indexed SPF with both palettes")
    count = struct.unpack_from("<I", data, 1036)[0]
    record = list(struct.unpack_from("<6H5I", data, 1040))
    blob_size = struct.unpack_from("<I", data, 1072)[0]
    if (count != 1 or record[:4] != [0, 0, 640, 480] or
        record[6:] != [0, 0, 640, 307200, 0] or blob_size != 307200 or len(data) != 308276):
        raise ValueError("Unsupported fullscreen SPF record")
    opaque = next((index for index in range(1, 256)
        if all(struct.unpack_from("<H", data, start + index * 2)[0] == 1 for start in (12, 524))), None)
    if opaque is None:
        raise ValueError("Expected an existing opaque near-black palette entry")
    original = data[1076:]
    pixels = bytearray([opaque]) * (width * height)
    extra_height = height - 480
    for y in range(height):
        if hud_top is None:
            if y >= 480:
                continue
            source_y = y
        elif y < 100:
            source_y = y
        elif y < 100 + extra_height:
            # Repeat only a decorative, transparent world-border segment. The
            # lower HUD, its vine overhangs and its buttons are translated intact.
            source_y = 100 + (y - 100) % 64
        else:
            source_y = y - extra_height
        source_row = original[source_y * 640:(source_y + 1) * 640]
        row = y * width
        pixels[row:row + 640] = source_row
        if hud_top is not None and y < hud_top + extra_height:
            # The vine overhangs the 22-pixel control column by ten pixels.
            pixels[row + 608:row + width - 32] = bytes(width - 640)
            pixels[row + width - 32:row + width] = source_row[608:640]
            if y < 3:
                pixels[row + 608:row + width - 32] = bytes([source_row[607]]) * (width - 640)
        elif hud_top is not None and y < hud_top + extra_height + 3:
            # Continue the thin divider above the otherwise unchanged lower HUD.
            pixels[row + 640:row + width] = bytes([source_row[610]]) * (width - 640)
    record[2] = width
    record[3] = height
    record[8] = width
    record[9] = width * height
    result = data[:1036] + struct.pack("<I", 1) + struct.pack("<6H5I", *record)
    return result + struct.pack("<I", len(pixels)) + pixels


def widen_dialogue_spf(data, width):
    """Tile the native dialogue grain; retain palettes and both frame edges."""
    record = list(struct.unpack_from("<6H5I", data, 1040))
    if (len(data) != 70196 or data[8:10] != b"\0\0" or
            struct.unpack_from("<I", data, 1036)[0] != 1 or
            record != [0, 0, 640, 108, 0, 0, 0, 0, 640, 69120, 0]):
        raise ValueError("Unexpected dialogue frame")
    pixels = bytearray()
    for y in range(108):
        row = data[1076+y*640:1076+(y+1)*640]
        pixels.extend(row[:16])
        pixels.extend(bytes(row[16+(x-16)%608] for x in range(16, width-16)))
        pixels.extend(row[624:])
    record[2], record[8], record[9] = width, width, len(pixels)
    return data[:1040]+struct.pack("<6H5I", *record)+struct.pack("<I", len(pixels))+pixels


def build_archive(archive, width, presentation=False, height=480):
    validate_dimensions(width, height)
    originals = read_dat(archive)
    entries = dict(originals)
    changes = []
    for name in ("_nbk_s.txt", "_nbk_l.txt"):
        entries[name], edits = patch_layout(entries[name], width, name, height)
        changes.extend(edits)
        if presentation:
            delta = width - 640
            extra_height = height - 480
            for control in ("Noname", "EMPTY"):
                entries[name], edit = change_rect(entries[name], control,
                    lambda rect: [rect[0], rect[1], rect[2] + delta, rect[3] + extra_height])
                changes.append(dict(edit, file=name))
            for control in ("BTN_OPTION", "BTN_BULLETIN", "BTN_USERS", "BTN_EMOT"):
                entries[name], edit = change_rect(entries[name], control,
                    lambda rect: [rect[0] + delta, rect[1] + extra_height, rect[2] + delta, rect[3] + extra_height])
                changes.append(dict(edit, file=name))
            if extra_height:
                stationary = {"Noname", "EMPTY", "MAP", "CAPTURE", "BTN_OPTION", "BTN_BULLETIN", "BTN_USERS", "BTN_EMOT"}
                controls = re.findall(rb'<NAME>\s*"([^"]+)"', entries[name])
                for raw_control in controls:
                    control = raw_control.decode("ascii")
                    if control in stationary:
                        continue
                    entries[name], edit = change_rect(entries[name], control,
                        lambda rect: [rect[0], rect[1] + extra_height, rect[2], rect[3] + extra_height])
                    changes.append(dict(edit, file=name))
    if presentation:
        # Dialogue sits immediately above the full HUD. Move portrait, text,
        # arrows and native click targets together; keep original font pixels.
        name = "lnpcd.txt"
        for raw in re.findall(rb'<NAME>\s*"([^"]+)"', entries[name]):
            control = raw.decode("ascii")
            def dialogue_rect(rect):
                x1, y1, x2, y2 = rect
                if control == "Noname": return [0, 0, width, height]
                if control in {"TopBtn", "CloseBtn", "UpArrow", "DownArrow"}:
                    x1 += width-640
                    x2 += width-640
                elif control != "NPCTile" and control not in {"PrevBtn", "NextBtn"}:
                    x2 += width-640
                if control == "MenuDialog": return [x1, y1, x2, y2+height-649]
                return [x1, y1+height-649, x2, y2+height-649]
            entries[name], edit = change_rect(entries[name], control, dialogue_rect)
            changes.append(dict(edit, file=name))
        entries["nd_talk.spf"] = widen_dialogue_spf(entries["nd_talk.spf"], width)
        changes.append({"file":"nd_talk.spf", "resampled":False, "new_size":[width,108]})
        # UserInfoPane builds its root rectangle from the union of these
        # controls, then normalizes every child against that root. Translating
        # all rectangles moves both artwork and native hit targets together.
        name = "_nui.txt"
        for raw_control in re.findall(rb'<NAME>\s*"([^"]+)"', entries[name]):
            entries[name], edit = change_rect(entries[name], raw_control.decode("ascii"),
                lambda rect: [rect[0] + (width-640)//2, rect[1] + (height-480)//2,
                              rect[2] + (width-640)//2, rect[3] + (height-480)//2])
            changes.append(dict(edit, file=name))
        for name, hud_top in (("_nbk_s.spf", 308), ("_nbk_l.spf", 408),
                              ("_nstart.spf", None), ("term.spf", None)):
            entries[name] = widen_spf(entries[name], width, hud_top, height)
            changes.append({"file": name, "old_size": [640, 480], "new_size": [width, height],
                            "resampled": False, "palettes_preserved": True})
        for name in ("_nstart.txt", "lterm2.txt"):
            entries[name], edit = change_rect(entries[name], "Noname",
                lambda rect: [rect[0], rect[1], width, height])
            changes.append(dict(edit, file=name))
    output = write_dat(entries)
    if read_dat(output) != entries:
        raise ValueError("DAT round-trip verification failed")
    changed_names = {edit["file"] for edit in changes}
    if any(originals[name] != entries[name] for name in originals if name not in changed_names):
        raise ValueError("Unrelated archive entry changed")
    return output, changes, len(entries)


def prepare_update(source, width, height=480):
    validate_dimensions(width, height)
    # Prepare only. Applying the update is a separate operation after game exit.
    manifest = json.loads((ROOT / "build/manifest.json").read_text())
    if manifest["logical_size"] != [width, height]:
        raise ValueError("Prepared UI update must match the running build's dimensions")
    archive = (source / "setoa.dat").read_bytes()
    if sha(archive) != manifest["source_setoa_sha256"]:
        raise ValueError("Source archive changed since the original build")
    output, changes, count = build_archive(archive, width, presentation=True, height=height)
    manifest.update({"presentation_revision": PRESENTATION_REVISION, "test_setoa_sha256": sha(output),
        "layout_changes": changes, "archive_entry_count": count,
        "ui_policy": "Original HUD pixels and control sizes; full bottom HUD translated downward, world expanded above it, side border moved to the right edge.",
        "status": "native_wider_world_confirmed_presentation_update_pending_validation"})
    destination = ROOT / "build/update"
    destination.mkdir(exist_ok=True)
    (destination / "setoa.dat").write_bytes(output)
    (destination / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(json.dumps({"prepared": str(destination), "entries_changed": sorted({c["file"] for c in changes}),
                      "running_game_modified": False}, indent=2))


def patch_executable(data, width, height=480):
    validate_dimensions(width, height)
    if sha(data) != CLIENT_HASH:
        raise ValueError("Unsupported Darkages.exe. Expected the reviewed 7.41 SHA256.")
    pe = pefile.PE(data=data)
    result = bytearray(data)
    changes = []

    def patch(va, expected, replacement, why):
        if len(expected) != len(replacement):
            raise ValueError("Patch length differs")
        offset = pe.get_offset_from_rva(va - pe.OPTIONAL_HEADER.ImageBase)
        if result[offset:offset + len(expected)] != expected:
            raise ValueError("Unexpected original bytes at " + hex(va))
        result[offset:offset + len(expected)] = replacement
        changes.append({"preferred_va": hex(va), "file_offset": hex(offset),
                        "original": expected.hex(), "replacement": replacement.hex(), "purpose": why})

    patch(0x57A7E2, bytes.fromhex("c745d880020000"), b"\xc7\x45\xd8" + struct.pack("<I", width),
          "Logical width passed to the window, VideoSystem and root ScreenPane")
    for va, prefix, old, new in ((0x41c34e, "c745e8", 480, height), (0x41c355, "c745ec", 640, width)):
        opcode = bytes.fromhex(prefix)
        patch(va, opcode+struct.pack("<I",old), opcode+struct.pack("<I",new),
              "Popup BoardManager parent clipping covers the full canvas")
    # NPCSession owns the dialogue and portrait as children. Its two native
    # constructors ignore lnpcd.txt's root rectangle and allocate a 640x480
    # parent, clipping both drawing and hit tests even when the child is wide.
    for va, prefix, old, new in (
        (0x52ba64, "c745e8", 480, height), (0x52ba6b, "c745ec", 640, width),
        (0x52bd04, "c745e8", 480, height), (0x52bd0b, "c745ec", 640, width),
    ):
        opcode = bytes.fromhex(prefix)
        patch(va, opcode+struct.pack("<I",old), opcode+struct.pack("<I",new),
              "NPCSession parent clipping and child hit bounds cover the full canvas")
    # SpelledPane constructs its own rectangle, independently of the GUI text
    # layouts. Both left and right derive from x=618; keep width and hit tests.
    patch(0x56A80E, bytes.fromhex("81c16a020000"), b"\x81\xc1" + struct.pack("<I", width - 22),
          "Active-effect pane right edge follows the expanded canvas")
    patch(0x56A821, bytes.fromhex("c745ac6a020000"), b"\xc7\x45\xac" + struct.pack("<I", width - 22),
          "Active-effect pane left edge follows the expanded canvas")
    if height != 480:
        for va, prefix, old, new, purpose in (
            (0x57A7E9, "c745dc", 480, height, "Logical height passed to window, VideoSystem and root ScreenPane"),
            (0x59DFDE, "c74208", 480, height, "GUI background bottom-strip lower edge"),
            (0x5A467F, "c745e4", 480, height, "GUIBackPane registration height"),
            (0x5411D5, "c745e0", 480, height, "Main-menu pane height after returning from the world"),
            (0x579749, "c745e8", 480, height, "Main-menu pane height during startup"),
            (0x4671BE, "817d08", 480, height, "EventMan pointer Y upper-bound comparison"),
            (0x4671C7, "c74508", 479, height - 1, "EventMan pointer Y maximum coordinate"),
        ):
            opcode = bytes.fromhex(prefix)
            patch(va, opcode + struct.pack("<I", old), opcode + struct.pack("<I", new), purpose)
    # GUIBackPane draws the background as four strips around EMPTY. Three
    # strips independently end at 640; its factory also clips all children there.
    # Noname's asset rectangle alone cannot change either native limit.
    for va, prefix, purpose in (
        (0x59DEC3, "c7410c", "GUI background top-strip right edge"),
        (0x59DF9B, "c7420c", "GUI background right-strip right edge"),
        (0x59DFEB, "c7400c", "GUI background bottom-strip right edge"),
        (0x5A4686, "c745e8", "GUIBackPane registration width, including child input and drawing"),
        (0x5411DC, "c745e4", "Main-menu pane registration width after returning from the world"),
        (0x579750, "c745ec", "Main-menu pane registration width during startup"),
    ):
        opcode = bytes.fromhex(prefix)
        patch(va, opcode + struct.pack("<I", 640), opcode + struct.pack("<I", width), purpose)
    # Win32 message translation and EventMan each clamp X independently.
    # Keep their upper-bound comparisons paired with width-minus-one outputs.
    for va, prefix, old, new, purpose in (
        (0x48F213, "817dc0", 640, width, "Win32 pointer X upper-bound comparison"),
        (0x48F21C, "c745c0", 639, width - 1, "Win32 pointer X maximum coordinate"),
        (0x46719F, "817d0c", 640, width, "EventMan pointer X upper-bound comparison"),
        (0x4671A8, "c7450c", 639, width - 1, "EventMan pointer X maximum coordinate"),
    ):
        opcode = bytes.fromhex(prefix)
        patch(va, opcode + struct.pack("<I", old), opcode + struct.pack("<I", new), purpose)
    # Skip the entire display-registry read/probe/write block. The original branch
    # at 0x57a906 initializes both windowed mode and 1x presentation before startup.
    source, target = 0x57A7F7, 0x57A906
    patch(source, bytes.fromhex("8d45cc508d4dc8"), b"\xe9" + struct.pack("<i", target-source-5) + b"\x90\x90",
          "Use the original windowed 1x branch without reading or writing the shared display setting")
    old = b"Nexon.SingleInstance\0"
    new = b"DALib.WidePrototype\0"
    if len(new) > len(old) or data.count(old) != 1:
        raise ValueError("Unexpected single-instance mutex")
    offset = data.index(old)
    replacement = new.ljust(len(old), b"\0")
    result[offset:offset+len(old)] = replacement
    changes.append({"file_offset": hex(offset), "original": old.hex(), "replacement": replacement.hex(),
                    "purpose": "Separate test-instance mutex so the user's existing game session is unaffected"})
    offset, replacement = popup_centering.install(pe, data, width, height, patch, changes)
    result[offset:offset + len(replacement)] = replacement
    for change in changes:
        offset = int(change["file_offset"], 16)
        replacement = bytes.fromhex(change["replacement"])
        result[offset:offset+len(replacement)] = replacement
    # Authenticode is absent in this reviewed binary. Maintain a valid PE checksum.
    rebuilt = pefile.PE(data=bytes(result))
    rebuilt.OPTIONAL_HEADER.CheckSum = rebuilt.generate_checksum()
    output = rebuilt.write()
    return output, changes


def prepare_client_update(source, width, height=480):
    validate_dimensions(width, height)
    manifest = json.loads((ROOT / "build/manifest.json").read_text())
    if manifest["logical_size"] != [width, height]:
        raise ValueError("Client-only update must retain the current canvas dimensions")
    if sha((ROOT / "runtime/setoa.dat").read_bytes()) != manifest["test_setoa_sha256"]:
        raise ValueError("Runtime archive does not match its manifest")
    output, patches = patch_executable((source / "Darkages.exe").read_bytes(), width, height)
    manifest.update(test_client_sha256=sha(output), executable_patches=patches,
                    presentation_revision=PRESENTATION_REVISION, status="native_ui_and_pointer_limits_corrected_pending_validation")
    destination = ROOT / "build/client-update"
    destination.mkdir(exist_ok=True)
    (destination / "Darkages.exe").write_bytes(output)
    (destination / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(json.dumps({"prepared": str(destination), "sha256": sha(output),
                      "running_game_modified": False}, indent=2))


def prepare_display_update(source, width, height, destination=None):
    validate_dimensions(width, height)
    manifest = json.loads((ROOT / "build/manifest.json").read_text())
    original = (source / "Darkages.exe").read_bytes()
    archive = (source / "setoa.dat").read_bytes()
    if sha(original) != manifest["source_client_sha256"] or sha(archive) != manifest["source_setoa_sha256"]:
        raise ValueError("Source installation changed since the validated build")
    executable, patches = patch_executable(original, width, height)
    modified_archive, edits, count = build_archive(archive, width, presentation=True, height=height)
    manifest.update(base_test_client_sha256=manifest["test_client_sha256"],
                    base_test_setoa_sha256=manifest["test_setoa_sha256"],
                    test_client_sha256=sha(executable), test_setoa_sha256=sha(modified_archive),
                    executable_patches=patches, layout_changes=edits, archive_entry_count=count,
                    logical_size=[width, height], presentation_revision=PRESENTATION_REVISION,
                    ui_policy="Original HUD pixels and control sizes; full bottom HUD translated downward, world expanded above it, side border moved to the right edge.",
                    status="taller_native_world_and_full_hud_pending_validation")
    destination = destination or ROOT / "build/display-update"
    destination.mkdir(parents=True, exist_ok=True)
    (destination / "Darkages.exe").write_bytes(executable)
    (destination / "setoa.dat").write_bytes(modified_archive)
    (destination / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(json.dumps({"prepared": str(destination), "size": [width, height], "running_game_modified": False}, indent=2))


def build(source, width, height=480):
    validate_dimensions(width, height)
    source = source.resolve(strict=True)
    runtime = (ROOT / "runtime").resolve()
    if source == runtime or runtime in source.parents or source in runtime.parents:
        raise ValueError("Source and test runtime must be separate directories")
    # Refuse before touching any assets if the test image is running or locked.
    # This never launches, stops, or attaches to a process.
    if sys.platform == "win32" and (runtime / "Darkages.exe").exists():
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32,
                                      ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
        kernel.CreateFileW.restype = ctypes.c_void_p
        kernel.CloseHandle.argtypes = [ctypes.c_void_p]
        handle = kernel.CreateFileW(str(runtime / "Darkages.exe"), 0x40000000, 0, None, 3, 0, None)
        if handle == ctypes.c_void_p(-1).value:
            raise RuntimeError("Close the test game before rebuilding; its executable is in use or not writable")
        kernel.CloseHandle(handle)
    original = (source / "Darkages.exe").read_bytes()
    executable, patches = patch_executable(original, width, height)
    archive = (source / "setoa.dat").read_bytes()
    modified_archive, layout_changes, entry_count = build_archive(archive, width, presentation=True, height=height)
    runtime.mkdir(parents=True, exist_ok=True)
    copied = []
    # Copy only program assets, never character folders, personal preferences or
    # login data. Use separate files, not hard links back into the installation.
    for path in source.iterdir():
        if path.is_file() and path.suffix.lower() in (".dat", ".dll", ".bmp", ".tbl", ".nfo"):
            if path.name.lower() == "setoa.dat":
                continue
            destination = runtime / path.name
            if not destination.exists() or destination.stat().st_size != path.stat().st_size:
                shutil.copyfile(path, destination)
            copied.append(path.name)
    for name in ("music", "npc", "maps", "metafile"):
        if (source / name).is_dir():
            shutil.copytree(source / name, runtime / name, dirs_exist_ok=True)
    # Keep executables last so an interrupted first build has no runnable client.
    (runtime / "setoa.dat").write_bytes(modified_archive)
    (runtime / "Darkages.exe").write_bytes(executable)
    manifest = {
        "status": "experimental_native_viewport_not_yet_gameplay_validated",
        "source": str(source), "runtime": str(runtime), "logical_size": [width, height],
        "source_client_sha256": sha(original), "test_client_sha256": sha(executable),
        "source_setoa_sha256": sha(archive), "test_setoa_sha256": sha(modified_archive),
        "executable_patches": patches, "layout_changes": layout_changes,
        "archive_entry_count": entry_count, "unrelated_archive_entries_byte_identical": True,
        "presentation_revision": PRESENTATION_REVISION,
        "copied_asset_names": copied,
        "ui_policy": "Original HUD pixels and control sizes; full bottom HUD translated downward, world expanded above it, side border moved to the right edge.",
        "limitations": ["No arbitrary window resizing yet", "Official-server visibility remains unchanged", "No gameplay automation", "No character or login data copied"],
    }
    (ROOT / "build").mkdir(exist_ok=True)
    (ROOT / "build/manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"runtime": str(runtime), "size": [width, height], "verified_archive_entries": entry_count,
                      "sha256": sha(executable)}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=SOURCE_DEFAULT)
    parser.add_argument("--width", type=int, default=800)
    parser.add_argument("--height", type=int, default=480)
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument("--prepare-update", action="store_true")
    modes.add_argument("--prepare-client-update", action="store_true")
    modes.add_argument("--prepare-display-update", action="store_true")
    modes.add_argument("--prepare-host-profiles", action="store_true")
    arguments = parser.parse_args()
    if arguments.prepare_host_profiles:
        for width, height in ((960, 600), (1088, 640), (1152, 680)):
            prepare_display_update(arguments.source, width, height, ROOT / "build/profiles" / f"{width}x{height}")
    elif arguments.prepare_update:
        prepare_update(arguments.source, arguments.width, arguments.height)
    elif arguments.prepare_client_update:
        prepare_client_update(arguments.source, arguments.width, arguments.height)
    elif arguments.prepare_display_update:
        prepare_display_update(arguments.source, arguments.width, arguments.height)
    else:
        build(arguments.source, arguments.width, arguments.height)
