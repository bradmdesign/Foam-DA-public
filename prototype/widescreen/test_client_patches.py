import gzip
import struct
import unittest
import build_client_patches as patch
import build_prototype as client


class ClientPatchChecks(unittest.TestCase):
    def test_executable_delta_reuses_original_bytes_and_preserves_changes(self):
        source = bytes(range(256))*100
        target = bytearray(source)
        target[17:20] = b'abc'
        encoded = patch.encode(source, bytes(target))
        self.assertEqual(patch.decode(source, encoded), bytes(target))
        self.assertLess(len(encoded), 200)

    def test_archive_can_move_unchanged_members_and_resize_modified_members(self):
        source = client.write_dat({'a.txt': b'keep this', 'b.txt': b'old', 'c.txt': b'also unchanged', 'empty.txt': b''})
        target = client.write_dat({'a.txt': b'keep this', 'b.txt': b'expanded replacement data', 'c.txt': b'also unchanged', 'empty.txt': b''})
        self.assertEqual(patch.decode(source, patch.encode(source, target, True)), target)

    def test_wrong_version_and_damaged_output_are_rejected(self):
        source = b'unchanged prefix___old suffix'
        target = b'unchanged prefix___new suffix'
        encoded = patch.encode(source, target)
        with self.assertRaises(ValueError):
            patch.decode(b'?' + source[1:], encoded)
        raw = bytearray(gzip.decompress(encoded))
        raw[52] ^= 1
        with self.assertRaises(ValueError):
            patch.decode(source, gzip.compress(raw))

    def test_copy_outside_source_is_rejected(self):
        source = b'original'
        raw = bytearray(gzip.decompress(patch.encode(source, source)))
        struct.pack_into('<q', raw, 89, -1)
        with self.assertRaises(ValueError):
            patch.decode(source, gzip.compress(raw))


if __name__ == '__main__':
    unittest.main()
