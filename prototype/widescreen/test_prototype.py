"""Checks for guarded native patches and lossless archive changes; never runs DA."""
import struct
import re
import unittest
from pathlib import Path

import build_prototype as prototype
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
import pefile


class PrototypeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.original = (prototype.SOURCE_DEFAULT / "Darkages.exe").read_bytes()
        cls.archive = (prototype.SOURCE_DEFAULT / "setoa.dat").read_bytes()

    def test_wrong_client_is_rejected(self):
        modified = bytearray(self.original)
        modified[-1] ^= 1
        with self.assertRaises(ValueError):
            prototype.patch_executable(bytes(modified), 800)

    def test_only_declared_executable_bytes_change(self):
        result, changes = prototype.patch_executable(self.original, 800)
        pe = pefile.PE(data=result)
        allowed = set()
        for change in changes:
            offset = int(change["file_offset"], 16)
            allowed.update(range(offset, offset + len(bytes.fromhex(change["replacement"]))))
        checksum = pe.OPTIONAL_HEADER.get_field_absolute_offset("CheckSum")
        allowed.update(range(checksum, checksum + 4))
        self.assertEqual(len(result), len(self.original))
        self.assertTrue(all(i in allowed for i, (a, b) in enumerate(zip(self.original, result)) if a != b))
        self.assertEqual(pe.OPTIONAL_HEADER.CheckSum, pe.generate_checksum())
        decoder = Cs(CS_ARCH_X86, CS_MODE_32)
        width_instruction = next(decoder.disasm(pe.get_data(0x17a7e2, 7), 0x57a7e2))
        self.assertEqual(width_instruction.mnemonic, "mov")
        self.assertEqual(width_instruction.op_str, "dword ptr [ebp - 0x28], 0x320")
        branch = next(decoder.disasm(pe.get_data(0x17a7f7, 7), 0x57a7f7))
        self.assertEqual((branch.mnemonic, branch.op_str), ("jmp", "0x57a906"))

    def test_archive_preserves_every_unrelated_payload(self):
        original = prototype.read_dat(self.archive)
        entries = dict(original)
        for name in ("_nbk_s.txt", "_nbk_l.txt"):
            entries[name], changes = prototype.patch_layout(entries[name], 800, name)
            self.assertEqual({c["control"] for c in changes}, {"MAP", "CAPTURE"})
            for change in changes:
                self.assertEqual(change["new"][2] - change["old"][2], 160)
                self.assertEqual(change["new"][:2], change["old"][:2])
                self.assertEqual(change["new"][3], change["old"][3])
        decoded = prototype.read_dat(prototype.write_dat(entries))
        self.assertEqual(decoded, entries)
        self.assertEqual({k for k in original if original[k] != decoded[k]}, {"_nbk_s.txt", "_nbk_l.txt"})

    def test_effect_pane_keeps_its_width_at_right_edge(self):
        for width in (960, 1088, 1152):
            result, _ = prototype.patch_executable(self.original, width, 640)
            pe = pefile.PE(data=result)
            right = struct.unpack('<I', pe.get_data(0x16a810, 4))[0]
            left = struct.unpack('<I', pe.get_data(0x16a824, 4))[0]
            self.assertEqual((left, right), (width - 22, width - 22))

    def test_mail_parent_clip_and_dialogue_geometry(self):
        result, _ = prototype.patch_executable(self.original, 1088, 640)
        pe = pefile.PE(data=result)
        self.assertEqual(struct.unpack('<I', pe.get_data(0x1c351,4))[0],640)
        self.assertEqual(struct.unpack('<I', pe.get_data(0x1c358,4))[0],1088)
        packed, _, _ = prototype.build_archive(self.archive,1088,presentation=True,height=640)
        new=prototype.read_dat(packed)
        controls={m[0].decode():tuple(map(int,m[1:])) for m in re.findall(
            rb'<NAME>\s*"([^"]+)"\s*<TYPE>\s*\d+\s*<RECT>\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)',new['lnpcd.txt'])}
        self.assertEqual(controls['MessageDialog'],(0,363,1088,471))
        self.assertEqual(controls['CloseBtn'],(1007,441,1068,463))
        self.assertEqual(controls['Text'],(26,391,1054,440))
        old=prototype.read_dat(self.archive)['nd_talk.spf'];wide=new['nd_talk.spf']
        self.assertEqual(old[:1040],wide[:1040])
        self.assertEqual(len(wide),1076+1088*108)
        for y in range(108):
            self.assertEqual(wide[1076+y*1088:1092+y*1088],old[1076+y*640:1092+y*640])
            self.assertEqual(wide[1076+(y+1)*1088-16:1076+(y+1)*1088],old[1076+(y+1)*640-16:1076+(y+1)*640])

    def test_both_npc_session_constructors_enclose_dialogue_and_click_targets(self):
        # Reproduce the reported bug: the child can be wide while its native
        # parent clips the far-right text/Close button. Exercise both factories.
        for width,height in ((960,600),(1088,640),(1152,680)):
            result,_=prototype.patch_executable(self.original,width,height)
            pe=pefile.PE(data=result)
            packed,_,_=prototype.build_archive(self.archive,width,presentation=True,height=height)
            layout=prototype.read_dat(packed)['lnpcd.txt']
            controls={m[0].decode():tuple(map(int,m[1:])) for m in re.findall(
                rb'<NAME>\s*"([^\"]+)"\s*<TYPE>\s*\d+\s*<RECT>\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)',layout)}
            for factory in (0x52ba56,0x52bcf6):
                instructions=list(Cs(CS_ARCH_X86,CS_MODE_32).disasm(pe.get_data(factory-0x400000,28),factory))
                self.assertEqual([i.mnemonic for i in instructions],['mov']*4)
                # Native RECT storage is top,left,bottom,right.
                native=[int(i.op_str.split(', ')[1],0) for i in instructions]
                self.assertEqual(native,[0,0,height,width])
                for key in ('MessageDialog','Text','CloseBtn','PrevBtn','NextBtn','NPCTile'):
                    left,top,right,bottom=controls[key]
                    self.assertGreaterEqual(left,native[1],key)
                    self.assertGreaterEqual(top,native[0],key)
                    self.assertLessEqual(right,native[3],key)
                    self.assertLessEqual(bottom,native[2],key)
                self.assertGreater(controls['CloseBtn'][0],640)

    def test_native_ui_clipping_and_redraw_widths_match_the_canvas(self):
        result, _ = prototype.patch_executable(self.original, 800)
        pe = pefile.PE(data=result)
        decoder = Cs(CS_ARCH_X86, CS_MODE_32)
        expected = {
            0x59dec3: "dword ptr [ecx + 0xc], 0x320",
            0x59df9b: "dword ptr [edx + 0xc], 0x320",
            0x59dfeb: "dword ptr [eax + 0xc], 0x320",
            0x5a4686: "dword ptr [ebp - 0x18], 0x320",
            0x5411dc: "dword ptr [ebp - 0x1c], 0x320",
            0x579750: "dword ptr [ebp - 0x14], 0x320",
        }
        for address, operands in expected.items():
            instruction = next(decoder.disasm(pe.get_data(address - 0x400000, 7), address))
            self.assertEqual((instruction.mnemonic, instruction.op_str), ("mov", operands))

    def test_unexpected_layout_is_rejected(self):
        entries = prototype.read_dat(self.archive)
        altered = entries["_nbk_s.txt"].replace(b'"MAP"', b'"OTHER"')
        with self.assertRaises(ValueError):
            prototype.patch_layout(altered, 800, "_nbk_s.txt")

    def test_both_pointer_clamps_allow_the_entire_wider_canvas(self):
        result, _ = prototype.patch_executable(self.original, 800)
        pe = pefile.PE(data=result)
        decoder = Cs(CS_ARCH_X86, CS_MODE_32)
        expected = {
            0x48f213: ("cmp", "dword ptr [ebp - 0x40], 0x320"),
            0x48f21c: ("mov", "dword ptr [ebp - 0x40], 0x31f"),
            0x46719f: ("cmp", "dword ptr [ebp + 0xc], 0x320"),
            0x4671a8: ("mov", "dword ptr [ebp + 0xc], 0x31f"),
        }
        for address, decoded in expected.items():
            instruction = next(decoder.disasm(pe.get_data(address - 0x400000, 7), address))
            self.assertEqual((instruction.mnemonic, instruction.op_str), decoded)

    def test_bad_dat_offsets_are_rejected(self):
        data = bytearray(prototype.write_dat({"a.txt": b"a", "b.txt": b"b"}))
        struct.pack_into("<I", data, 4, 0)
        with self.assertRaises(ValueError):
            prototype.read_dat(data)

    def test_invalid_width_is_rejected_before_io(self):
        for width in (0, 639, 641, 1924):
            with self.assertRaises(ValueError):
                prototype.build(Path("does-not-exist"), width)

    def test_presentation_keeps_hud_and_palettes_and_moves_border(self):
        entries = prototype.read_dat(self.archive)
        for name, hud_top in (("_nbk_s.spf", 308), ("_nbk_l.spf", 408)):
            old = entries[name]
            new = prototype.widen_spf(old, 800, hud_top)
            self.assertEqual(old[:1040], new[:1040])
            frame = struct.unpack_from("<6H5I", new, 1040)
            self.assertEqual(frame[:4], (0, 0, 800, 480))
            self.assertEqual(frame[8:10], (800, 800 * 480))
            self.assertEqual(len(new), 1076 + 800 * 480)
            old_pixels, new_pixels = old[1076:], new[1076:]
            for y in range(hud_top, 480):
                self.assertEqual(old_pixels[y*640:(y+1)*640], new_pixels[y*800:y*800+640])
            for y in range(3, hud_top):
                self.assertEqual(new_pixels[y*800+608:y*800+768], bytes(160))
                self.assertEqual(new_pixels[y*800+768:(y+1)*800], old_pixels[y*640+608:(y+1)*640])
            # No-op width must be an exact byte-for-byte SPF round trip.
            self.assertEqual(prototype.widen_spf(old, 640, hud_top), old)

    def test_presentation_changes_only_nine_named_entries(self):
        original = prototype.read_dat(self.archive)
        archive, _, _ = prototype.build_archive(self.archive, 800, presentation=True)
        new = prototype.read_dat(archive)
        self.assertEqual({name for name in original if original[name] != new[name]},
            {"_nbk_s.txt", "_nbk_l.txt", "_nbk_s.spf", "_nbk_l.spf", "_nstart.spf", "term.spf", "_nstart.txt", "lterm2.txt", "_nui.txt", "lnpcd.txt", "nd_talk.spf"})

    def test_character_popup_moves_as_one_native_layout(self):
        original = prototype.read_dat(self.archive)["_nui.txt"]
        packed, _, _ = prototype.build_archive(self.archive, 1088, presentation=True, height=640)
        changed = prototype.read_dat(packed)["_nui.txt"]
        pattern = rb'(<RECT>\s*)(\d+)\s+(\d+)\s+(\d+)\s+(\d+)'
        before, after = list(re.finditer(pattern, original)), list(re.finditer(pattern, changed))
        self.assertEqual(len(before), 10)
        self.assertEqual(len(before), len(after))
        for a, b in zip(before, after):
            old = [int(a.group(i)) for i in range(2, 6)]
            new = [int(b.group(i)) for i in range(2, 6)]
            self.assertEqual(new, [old[0]+224, old[1]+80, old[2]+224, old[3]+80])
        self.assertEqual(re.sub(pattern, b'<RECT>', original), re.sub(pattern, b'<RECT>', changed))

    def test_height_extension_preserves_the_validated_480_build(self):
        result, changes = prototype.patch_executable(self.original, 800, 480)
        # Restore the later effect-pane and popup-only changes before
        # comparing the historical 480-high fixture.
        historical = bytearray(result)
        for change in changes:
            if (change.get('preferred_va') in ('0x56a80e', '0x56a821') or
                    'popup' in change['purpose'].lower() or
                    change['purpose'].startswith('NPCSession') or
                    change['purpose'].startswith('Native popup') or change['purpose'].startswith('Frame sequence')):
                offset = int(change['file_offset'], 16)
                old = bytes.fromhex(change['original'])
                historical[offset:offset+len(old)] = old
        pe = pefile.PE(data=bytes(historical))
        pe.OPTIONAL_HEADER.CheckSum = pe.generate_checksum()
        archive, _, _ = prototype.build_archive(self.archive, 800, presentation=True, height=480)
        # Revision 8 centers character sheets. Restore that layout only before
        # checking the historical archive fixture.
        entries = prototype.read_dat(archive)
        for name in ("_nui.txt", "lnpcd.txt", "nd_talk.spf"):
            entries[name] = prototype.read_dat(self.archive)[name]
        archive = prototype.write_dat(entries)
        self.assertEqual(prototype.sha(pe.write()), "17a71271b3b46bb35e6324c5d00ac3498f360a360b5df132d530a170ffba9156")
        self.assertEqual(prototype.sha(archive), "d30dbb35fbdaff9809b4a93f78cd4555995a69858f47b12cb45ec97e7c7cb267")

    def test_invalid_height_is_rejected_before_io(self):
        for height in (0, 479, 481, 1084):
            with self.assertRaises(ValueError):
                prototype.build(Path("does-not-exist"), 800, height)

    def test_taller_native_canvas_and_pointer_bounds(self):
        result, changes = prototype.patch_executable(self.original, 800, 600)
        pe = pefile.PE(data=result)
        allowed = set()
        for change in changes:
            offset = int(change["file_offset"], 16)
            allowed.update(range(offset, offset + len(bytes.fromhex(change["replacement"]))))
        checksum = pe.OPTIONAL_HEADER.get_field_absolute_offset("CheckSum")
        allowed.update(range(checksum, checksum + 4))
        self.assertEqual(len(result), len(self.original))
        self.assertTrue(all(i in allowed for i, (a, b) in enumerate(zip(self.original, result)) if a != b))
        self.assertEqual(pe.OPTIONAL_HEADER.CheckSum, pe.generate_checksum())
        decoder = Cs(CS_ARCH_X86, CS_MODE_32)
        for address, operation in {
            0x57a7e9: ("mov", "dword ptr [ebp - 0x24], 0x258"),
            0x59dfde: ("mov", "dword ptr [edx + 8], 0x258"),
            0x5a467f: ("mov", "dword ptr [ebp - 0x1c], 0x258"),
            0x5411d5: ("mov", "dword ptr [ebp - 0x20], 0x258"),
            0x579749: ("mov", "dword ptr [ebp - 0x18], 0x258"),
            0x4671be: ("cmp", "dword ptr [ebp + 8], 0x258"),
            0x4671c7: ("mov", "dword ptr [ebp + 8], 0x257"),
        }.items():
            instruction = next(decoder.disasm(pe.get_data(address - 0x400000, 7), address))
            self.assertEqual((instruction.mnemonic, instruction.op_str), operation)

    def test_taller_assets_move_the_full_hud_without_resampling(self):
        original = prototype.read_dat(self.archive)
        packed, _, _ = prototype.build_archive(self.archive, 800, presentation=True, height=600)
        taller = prototype.read_dat(packed)
        self.assertEqual({k for k in original if original[k] != taller[k]},
            {"_nbk_s.txt", "_nbk_l.txt", "_nbk_s.spf", "_nbk_l.spf", "_nstart.spf", "term.spf", "_nstart.txt", "lterm2.txt", "_nui.txt", "lnpcd.txt", "nd_talk.spf"})
        for name, hud in (("_nbk_s.spf", 308), ("_nbk_l.spf", 408)):
            old, new = original[name], taller[name]
            self.assertEqual(old[:1040], new[:1040])
            frame = struct.unpack_from("<6H5I", new, 1040)
            self.assertEqual(frame[:4], (0, 0, 800, 600))
            self.assertEqual(frame[8:10], (800, 480000))
            self.assertEqual(len(new), 481076)
            for y in range(hud, 480):
                self.assertEqual(old[1076+y*640:1076+(y+1)*640], new[1076+(y+120)*800:1076+(y+120)*800+640])
            for y in range(100, 220):
                self.assertEqual(new[1076+y*800+100:1076+y*800+750], bytes(650))
        for name in ("_nstart.spf", "term.spf"):
            for y in range(480):
                self.assertEqual(original[name][1076+y*640:1076+(y+1)*640], taller[name][1076+y*800:1076+y*800+640])
        def rectangles(data):
            return {m[0].decode(): tuple(map(int, m[1:])) for m in re.findall(
                rb'<NAME>\s*"([^"]+)"\s*<TYPE>\s*\d+\s*<RECT>\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)', data)}
        for name, map_bottom in (("_nbk_s.txt", 431), ("_nbk_l.txt", 547)):
            old, new = rectangles(original[name]), rectangles(taller[name])
            self.assertEqual(new["MAP"], (3, 3, 779, map_bottom))
            self.assertEqual(new["Noname"], (0, 0, 800, 600))
            self.assertEqual(new["EMPTY"][:2], old["EMPTY"][:2])
            for control in old.keys() - {"MAP", "CAPTURE", "EMPTY", "Noname"}:
                a, b = old[control], new[control]
                self.assertEqual((a[2]-a[0], a[3]-a[1]), (b[2]-b[0], b[3]-b[1]), control)
                self.assertEqual(b[1]-a[1], 120, control)

    def test_hd_profiles_preserve_hud_payloads_and_match_native_limits(self):
        original = prototype.read_dat(self.archive)
        decoder = Cs(CS_ARCH_X86, CS_MODE_32)
        for width, height in ((1280, 720), (1600, 900), (1920, 1080)):
            with self.subTest(size=(width, height)):
                exe, _ = prototype.patch_executable(self.original, width, height)
                pe = pefile.PE(data=exe)
                self.assertEqual(pe.OPTIONAL_HEADER.CheckSum, pe.generate_checksum())
                for address, value in ((0x57a7e2, width), (0x57a7e9, height),
                    (0x5a4686, width), (0x5a467f, height),
                    (0x48f213, width), (0x48f21c, width-1),
                    (0x46719f, width), (0x4671a8, width-1),
                    (0x4671be, height), (0x4671c7, height-1)):
                    instruction = next(decoder.disasm(pe.get_data(address-0x400000, 7), address))
                    self.assertEqual(int(instruction.op_str.rsplit(', ', 1)[1], 0), value)
                packed, _, _ = prototype.build_archive(self.archive, width, presentation=True, height=height)
                entries = prototype.read_dat(packed)
                self.assertEqual({k for k in original if original[k] != entries[k]},
                    {"_nbk_s.txt", "_nbk_l.txt", "_nbk_s.spf", "_nbk_l.spf", "_nstart.spf", "term.spf", "_nstart.txt", "lterm2.txt", "_nui.txt", "lnpcd.txt", "nd_talk.spf"})
                for name, start in (("_nbk_s.spf", 308), ("_nbk_l.spf", 408)):
                    old, new = original[name], entries[name]
                    self.assertEqual(new[:1040], old[:1040])
                    frame = struct.unpack_from('<6H5I', new, 1040)
                    self.assertEqual(frame[:4], (0, 0, width, height))
                    self.assertEqual(frame[8:10], (width, width*height))
                    self.assertEqual(len(new), 1076+width*height)
                    for y in range(start, 480):
                        destination = 1076+(y+height-480)*width
                        self.assertEqual(new[destination:destination+640], old[1076+y*640:1076+(y+1)*640])
                for name, bottom in (("_nbk_s.txt", height-169), ("_nbk_l.txt", height-53)):
                    rect = re.search(rb'<NAME>\s*"MAP"\s*<TYPE>\s*7\s*<RECT>\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)', entries[name])
                    self.assertEqual(tuple(map(int, rect.groups())), (3, 3, width-21, bottom))


if __name__ == "__main__":
    unittest.main(verbosity=2)
