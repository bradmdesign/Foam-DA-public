"""Build small widescreen recipes, never a copy of the installed game.

End users apply these recipes locally through Foam DA; Python is build-time only.
"""
import argparse
import gzip
import hashlib
import json
from pathlib import Path
import struct
import build_prototype as client

ROOT = Path(__file__).resolve().parent
PROFILES = ((960, 600), (1088, 640), (1152, 680))


def dat_spans(data):
    client.read_dat(data)  # validate the complete index first
    count = struct.unpack_from('<I', data)[0]
    rows = [struct.unpack_from('<I13s', data, 4 + i * 17) for i in range(count)]
    return 4 + count * 17, [(name.split(b'\0', 1)[0], begin, end)
                              for (begin, name), (end, _) in zip(rows, rows[1:])]


def same_length_ops(source, target):
    if len(source) != len(target):
        raise ValueError('The executable recipe requires a same-length image')
    cursor = 0
    while cursor < len(target):
        start = cursor
        equal = source[cursor] == target[cursor]
        while cursor < len(target) and (source[cursor] == target[cursor]) == equal:
            cursor += 1
        yield (0, start, cursor-start) if equal else (1, target[start:cursor])


def archive_ops(source, target):
    _, originals = dat_spans(source)
    header, targets = dat_spans(target)
    by_name = {name: (begin, end) for name, begin, end in originals}
    yield (1, target[:header])
    for name, begin, end in targets:
        if begin == end:
            continue
        old = by_name.get(name)
        if old and source[old[0]:old[1]] == target[begin:end]:
            yield (0, old[0], end-begin)
        else:
            yield (1, target[begin:end])


def encode(source, target, archive=False):
    ops = list(archive_ops(source, target) if archive else same_length_ops(source, target))
    raw = bytearray(b'FDP1')
    raw += struct.pack('<q', len(source)) + hashlib.sha256(source).digest()
    raw += struct.pack('<q', len(target)) + hashlib.sha256(target).digest()
    raw += struct.pack('<i', len(ops))
    for op in ops:
        raw.append(op[0])
        if op[0] == 0:
            raw += struct.pack('<qi', op[1], op[2])
        else:
            raw += struct.pack('<i', len(op[1])) + op[1]
    return gzip.compress(raw, compresslevel=9, mtime=0)


def decode(source, patch):
    """Independent build verification; the shipped host has its own decoder."""
    raw = memoryview(gzip.decompress(patch))
    if bytes(raw[:4]) != b'FDP1':
        raise ValueError('Invalid recipe')
    source_size = struct.unpack_from('<q', raw, 4)[0]
    target_size = struct.unpack_from('<q', raw, 44)[0]
    if len(source) != source_size or hashlib.sha256(source).digest() != bytes(raw[12:44]):
        raise ValueError('Source version differs')
    count = struct.unpack_from('<i', raw, 84)[0]
    at = 88
    result = bytearray()
    for _ in range(count):
        kind = raw[at]; at += 1
        if kind == 0:
            start, size = struct.unpack_from('<qi', raw, at); at += 12
            if start < 0 or size <= 0 or start + size > len(source):
                raise ValueError('Copy outside source')
            result += source[start:start+size]
        elif kind == 1:
            size = struct.unpack_from('<i', raw, at)[0]; at += 4
            if size <= 0 or at + size > len(raw):
                raise ValueError('Truncated literal')
            result += raw[at:at+size]; at += size
        else:
            raise ValueError('Invalid operation')
        if len(result) > target_size:
            raise ValueError('Output overflow')
    if at != len(raw) or len(result) != target_size or hashlib.sha256(result).digest() != bytes(raw[52:84]):
        raise ValueError('Output verification failed')
    return bytes(result)


def build(source, output=None):
    source = Path(source).resolve(strict=True)
    output = Path(output or ROOT / 'client-patches')
    original = (source / 'Darkages.exe').read_bytes()
    archive = (source / 'setoa.dat').read_bytes()
    if client.sha(original) != client.CLIENT_HASH:
        raise ValueError('Unsupported installed client. The supported version is Dark Ages 7.41.')
    profiles = {}
    for width, height in PROFILES:
        name = f'{width}x{height}'
        image, _ = client.patch_executable(original, width, height)
        layout, _, _ = client.build_archive(archive, width, presentation=True, height=height)
        folder = output / name
        folder.mkdir(parents=True, exist_ok=True)
        row = {'width': width, 'height': height, 'test_client_sha256': client.sha(image),
               'test_setoa_sha256': client.sha(layout)}
        for filename, base, target, is_archive in [('client.patch.gz', original, image, False),
                                                  ('layout.patch.gz', archive, layout, True)]:
            patch = encode(base, target, is_archive)
            if decode(base, patch) != target:
                raise ValueError('Recipe did not reconstruct the verified client')
            (folder / filename).write_bytes(patch)
            row[filename.replace('.patch.gz', '_patch_sha256')] = client.sha(patch)
        profiles[name] = row
    assets = sorted(p.name for p in source.iterdir() if p.is_file() and
                    p.suffix.lower() in ('.dat', '.dll', '.bmp', '.tbl', '.nfo') and p.name.lower() != 'setoa.dat')
    catalog = {'version': 1, 'source_client_sha256': client.sha(original),
               'source_setoa_sha256': client.sha(archive), 'assets': assets, 'profiles': profiles}
    (output / 'catalog.json').write_text(json.dumps(catalog, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({'patch_bytes': sum(p.stat().st_size for p in output.rglob('*') if p.is_file()),
                      'profiles': list(profiles), 'game_files_bundled': False}, indent=2))
    return catalog


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=client.SOURCE_DEFAULT)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    build(args.source, args.output)
