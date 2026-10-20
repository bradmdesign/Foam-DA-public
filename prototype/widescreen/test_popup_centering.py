"""Exercise the real x86 wrapper in an isolated synthetic test process."""
import os
from pathlib import Path
import subprocess
import unittest
import build_prototype as b
import popup_centering as c


class PopupTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = (b.SOURCE_DEFAULT / 'Darkages.exe').read_bytes()
        cls.pe = b.pefile.PE(data=cls.data)
        cls.rva = cls.pe.sections[0].VirtualAddress + ((cls.pe.sections[0].Misc_VirtualSize + 15) & ~15)
        cls.tables = [c.primary_vtable(cls.pe, cls.data, n) - 0x400000 for n in c.POPUPS]
        cls.harness = b.ROOT / 'build/PopupChecks.exe'
        compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
        subprocess.run([str(compiler), '/nologo', '/platform:x86', '/out:' + str(cls.harness),
                        str(b.ROOT / 'PopupChecks.cs')], check=True, capture_output=True)

    def test_real_machine_code_centering_and_bypass_at_relocated_base(self):
        fixture = b.ROOT / 'build/popup-wrapper.bin'
        for width, height in ((960, 600), (1088, 640), (1152, 680), (1920, 1080)):
            code = c.wrapper(self.rva, self.tables, width, height)
            fixture.write_bytes(code)
            for name, table in zip(c.POPUPS, self.tables):
                with self.subTest(size=(width,height), pane=name):
                    result = subprocess.run([str(self.harness), str(fixture), str(self.rva),
                        str(width), str(height), str(table), "center" if name in ("FieldMapPane","TownMapPane") else "right"], capture_output=True, text=True, timeout=10)
                    self.assertEqual(result.returncode, 0, result.stderr + result.stdout)
                    self.assertIn('Passed 8 native x86 geometry cases', result.stdout)

    def test_hook_preserves_original_instructions_and_loaded_extent(self):
        result, _ = b.patch_executable(self.data, 1088, 640)
        pe = b.pefile.PE(data=result)
        code = c.wrapper(self.rva, self.tables, 1088, 640)
        self.assertEqual(pe.get_data(self.rva, len(code)), code)
        self.assertEqual(code[-11:-5], self.pe.get_data(0x149eb0, 6))
        self.assertLessEqual(self.rva + len(code), pe.sections[0].VirtualAddress + pe.sections[0].Misc_VirtualSize)
        self.assertEqual(pe.FILE_HEADER.NumberOfSections, 6)  # endpoint patch stays compatible
        for table in self.tables:
            self.assertEqual(int.from_bytes(pe.get_data(table+0x34,4),'little'), 0x549eb0)

    def test_real_sequence_hooks_and_counter_storage(self):
        compiler=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework/v4.0.30319/csc.exe'
        harness=b.ROOT/'build/SequenceChecks.exe'
        subprocess.run([str(compiler),'/nologo','/platform:x86','/out:'+str(harness),str(b.ROOT/'SequenceChecks.cs')],check=True,capture_output=True)
        first,last=b.ROOT/'build/sequence-begin.bin',b.ROOT/'build/sequence-end.bin'
        first.write_bytes(c.sequence_hook(0x268680,0x152820))
        last.write_bytes(c.sequence_hook(0x268700,0x4b1b0,True))
        result=subprocess.run([str(harness),str(first),str(last)],capture_output=True,text=True,timeout=10)
        self.assertEqual(result.returncode,0,result.stderr+result.stdout)
        patched,_=b.patch_executable(self.data,1088,640)
        pe=b.pefile.PE(data=patched)
        section=pe.sections[2]
        self.assertEqual(section.VirtualAddress+section.Misc_VirtualSize,c.FRAME_SEQUENCE_RVA+4)
        self.assertEqual(section.SizeOfRawData,self.pe.sections[2].SizeOfRawData)
        self.assertLess(c.FRAME_SEQUENCE_RVA+4,pe.sections[3].VirtualAddress)


if __name__ == '__main__':
    unittest.main()
