"""Independent PE/disassembly checks against the C# endpoint fixture."""
from pathlib import Path
import sys
import unittest
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32


class EndpointTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.pe = pefile.PE(str(Path(__file__).with_name('build') / 'endpoint-image-check.exe'))
        cls.decoder = Cs(CS_ARCH_X86, CS_MODE_32)

    def test_wrapper_preserves_initializer_then_parses_and_fails_closed(self):
        p = self.pe
        s = p.sections[-1]
        self.assertEqual(s.Name.rstrip(b'\0'), b'.dalib')
        self.assertEqual(s.Characteristics, 0x60000020)
        start = p.OPTIONAL_HEADER.ImageBase + s.VirtualAddress
        code = list(self.decoder.disasm(s.get_data()[:s.Misc_VirtualSize], start))
        self.assertEqual([(i.mnemonic, i.op_str) for i in code[:5]], [
            ('push', 'ecx'), ('call', '0x433380'), ('pop', 'ecx'),
            ('call', '0x433010'), ('test', 'al, al')])
        self.assertEqual(code[5].op_str, hex(code[-1].address))
        self.assertEqual(code[-1].mnemonic, 'ret')
        self.assertEqual(code[6].op_str, '1')
        # A call/pop computes the IAT address without a new absolute relocation.
        pc = code[7].address + code[7].size
        delta = int(code[9].op_str.split(', ')[1], 16)
        self.assertEqual((pc + delta) & 0xffffffff, 0x669124)
        self.assertEqual(code[10].op_str, 'dword ptr [edx]')
        imports = {i.address: i.name for d in p.DIRECTORY_ENTRY_IMPORT for i in d.imports}
        self.assertEqual(imports[0x669124], b'ExitProcess')

    def test_call_redirect_fallback_disable_and_checksum(self):
        p = self.pe
        call = next(self.decoder.disasm(p.get_data(0x32253, 5), 0x432253))
        self.assertEqual(call.op_str, hex(0x400000 + p.sections[-1].VirtualAddress))
        fail = next(self.decoder.disasm(p.get_data(0x1655f4, 10), 0x5655f4))
        self.assertEqual((fail.mnemonic, fail.op_str), ('jmp', '0x5668ff'))
        self.assertEqual(p.OPTIONAL_HEADER.CheckSum, p.generate_checksum())
        self.assertEqual(p.get_overlay_data_start_offset(), None)


if __name__ == '__main__':
    unittest.main()
