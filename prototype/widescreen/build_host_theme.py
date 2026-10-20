"""Derive the local host skin from the user's installed DA assets, read-only.

SPF: first palette RGB565. LFT: 4-byte header, 11-byte glyph records,
bitmap region at 0xAFFF9, 32-bit-padded one-bit rows. Validated against
render_font_load_lft/build_glyph_mask in this exact 7.4.1 client.
The output is a local derivative of game assets, not a redistributable font pack.
"""
import base64
from collections import Counter
import hashlib
import json
from pathlib import Path
import struct
import sys
from PIL import Image
from build_prototype import read_dat, SOURCE_DEFAULT, ROOT, pefile, CLIENT_HASH, sha


def build_cursors(output):
    data = (SOURCE_DEFAULT / 'Darkages.exe').read_bytes()
    if sha(data) != CLIENT_HASH:
        raise ValueError('Unrecognized cursor source executable')
    pe = pefile.PE(data=data)
    resources = {entry.id: entry for entry in pe.DIRECTORY_ENTRY_RESOURCE.entries}
    group = next(entry for entry in resources[12].directory.entries if entry.id == 110)
    record = group.directory.entries[0].data.struct
    group_data = pe.get_data(record.OffsetToData, record.Size)
    if struct.unpack_from('<HHH', group_data) != (0, 2, 1):
        raise ValueError('Unexpected game cursor group')
    resource_id = struct.unpack_from('<H', group_data, 18)[0]
    cursor = next(entry for entry in resources[1].directory.entries if entry.id == resource_id)
    record = cursor.directory.entries[0].data.struct
    blob = pe.get_data(record.OffsetToData, record.Size)
    hot_x, hot_y = struct.unpack_from('<HH', blob)
    dib = blob[4:]
    width, double_height = struct.unpack_from('<ii', dib, 4)
    header = struct.pack('<HHH', 0, 2, 1)
    (output / 'game.cur').write_bytes(header + struct.pack('<BBBBHHII', width, double_height // 2, 0, 0, hot_x, hot_y, len(dib), 22) + dib)
    # A transparent 1x1 cursor only over the game control; no global HideCursor.
    dib = struct.pack('<IiiHHIIiiII', 40, 1, 2, 1, 1, 0, 8, 0, 0, 0, 0) + bytes(8) + bytes(4) + bytes([128, 0, 0, 0])
    (output / 'empty.cur').write_bytes(header + struct.pack('<BBBBHHII', 1, 1, 0, 0, 0, 0, len(dib), 22) + dib)
    # The normal visible game arrow is software art, not resource 110. The
    # native EPF palette selector (0x4830AD) assigns mouse.epf palette 3.
    entries = read_dat((SOURCE_DEFAULT / 'setoa.dat').read_bytes())
    sprite, palette = entries['mouse.epf'], entries['gui03.pal']
    count, width, height, _, table = struct.unpack_from('<HHHHI', sprite)
    if (count, width, height) != (3, 33, 32) or len(palette) != 768:
        raise ValueError('Unexpected DA mouse art')
    top, left, bottom, right, start, _ = struct.unpack_from('<hhhhII', sprite, 12 + table)
    pixels = sprite[12 + start:12 + start + (right-left)*(bottom-top)]
    arrow = Image.frombytes('P', (right-left, bottom-top), pixels)
    arrow.putpalette(palette); arrow.info['transparency'] = 0
    arrow = arrow.convert('RGBA')
    # The software cursor uses a 32x32 canvas; the EPF's final column is empty.
    if arrow.crop((32, 0, 33, 32)).getbbox():
        raise ValueError('Nonempty cursor overflow column')
    arrow = arrow.crop((0, 0, 32, 32))
    tip = min(((x,y) for y in range(32) for x in range(32) if arrow.getpixel((x,y))[3]), key=lambda p:(p[1],p[0]))
    xor = b''.join(bytes((b,g,r,a)) for y in range(31,-1,-1) for x in range(32) for r,g,b,a in [arrow.getpixel((x,y))])
    mask = b''.join(bytes(sum((1 << (7-i)) if arrow.getpixel((block*8+i,y))[3]==0 else 0 for i in range(8)) for block in range(4)) for y in range(31,-1,-1))
    dib = struct.pack('<IiiHHIIiiII',40,32,64,1,32,0,len(xor)+len(mask),0,0,0,0)+xor+mask
    (output/'game.cur').write_bytes(header+struct.pack('<BBBBHHII',32,32,0,0,tip[0],tip[1],len(dib),22)+dib)
    arrow.save(output/'game-cursor.png')


def archive_entry(path, wanted):
    data = path.read_bytes()
    count = struct.unpack_from('<I', data)[0]
    for i in range(count - 1):
        offset, name = struct.unpack_from('<I13s', data, 4 + i * 17)
        if name.split(b'\0')[0].decode('ascii') == wanted:
            end = struct.unpack_from('<I', data, 4 + (i + 1) * 17)[0]
            if not 0 <= offset < end <= len(data):
                raise ValueError('Invalid archive extent')
            return data[offset:end]
    raise ValueError('Missing ' + wanted)


def build():
    from fontTools.fontBuilder import FontBuilder
    from fontTools.pens.ttGlyphPen import TTGlyphPen
    output = ROOT / 'theme'
    output.mkdir(exist_ok=True)
    build_cursors(output)
    spf = read_dat((SOURCE_DEFAULT / 'setoa.dat').read_bytes())['_nbk_s.spf']
    if len(spf) != 308276:
        raise ValueError('Unexpected HUD asset')
    palette = struct.unpack_from('<256H', spf, 12)
    rgb = [(((p >> 11) & 31) * 255 // 31, ((p >> 5) & 63) * 255 // 63, (p & 31) * 255 // 31) for p in palette]
    hud = Image.new('RGB', (640, 480))
    hud.putdata([rgb[p] for p in spf[1076:]])
    crops = {'panel-frame': (86, 331, 532, 442), 'input-frame': (87, 310, 531, 333),
             'grain': (180, 350, 244, 414), 'button': (567, 377, 588, 398)}
    for name, rect in crops.items():
        hud.crop(rect).save(output / (name + '.png'))

    lft = archive_entry(SOURCE_DEFAULT / 'national.dat', 'da.lft')
    if struct.unpack_from('<HH', lft) != (12, 12):
        raise ValueError('Unexpected DA font metrics')
    glyphs, widths, cmap = {}, {}, {}
    glyphs['.notdef'] = TTGlyphPen(None).glyph()
    widths['.notdef'] = (384, 0)
    glyphs['space'] = TTGlyphPen(None).glyph()
    widths['space'] = (384, 0)
    cmap[32] = 'space'
    for code in range(33, 256):
        advance, left, top, right, bottom, byte_count, offset = struct.unpack_from('<5BHI', lft, 4 + code * 11)
        if not offset or not advance or right <= left or bottom <= top:
            continue
        width, height = right - left, bottom - top
        stride = ((width + 31) // 32) * 4
        data = lft[0xAFFF9 + offset:0xAFFF9 + offset + byte_count]
        if len(data) < stride * height or right > 24 or bottom > 24:
            raise ValueError('Invalid glyph bounds')
        pen = TTGlyphPen(None)
        for y in range(height):
            x = 0
            while x < width:
                if not (data[y * stride + x // 8] & (0x80 >> (x % 8))):
                    x += 1
                    continue
                start = x
                while x < width and data[y * stride + x // 8] & (0x80 >> (x % 8)):
                    x += 1
                x0, x1, y0 = (left + start) * 64, (left + x) * 64, (10 - top - y - 1) * 64
                pen.moveTo((x0, y0)); pen.lineTo((x0, y0 + 64)); pen.lineTo((x1, y0 + 64)); pen.lineTo((x1, y0)); pen.closePath()
        name = 'uni%04X' % code
        glyphs[name] = pen.glyph()
        widths[name] = (advance * 64, left * 64)
        cmap[code] = name
    font = FontBuilder(768, isTTF=True)
    font.setupGlyphOrder(list(glyphs))
    font.setupCharacterMap(cmap)
    font.setupGlyf(glyphs)
    font.setupHorizontalMetrics(widths)
    font.setupHorizontalHeader(ascent=640, descent=-128)
    font.setupNameTable({'familyName': 'DA Local Bitmap', 'styleName': 'Regular', 'uniqueFontIdentifier': 'DA Local Bitmap v1',
                        'fullName': 'DA Local Bitmap', 'psName': 'DALocalBitmap', 'version': 'Version 1.0'})
    font.setupOS2(sTypoAscender=640, sTypoDescender=-128, usWinAscent=640, usWinDescent=128)
    font.setupPost()
    font.setupMaxp()
    font.save(output / 'da-local.ttf')
    colors = Counter(hud.crop(crops['grain']).getdata())
    (output / 'provenance.json').write_text(json.dumps({'source': str(SOURCE_DEFAULT), 'hud': '_nbk_s.spf',
        'hud_sha256': hashlib.sha256(spf).hexdigest(), 'crops': crops, 'grain_colors': colors.most_common(8),
        'font': 'national.dat/da.lft', 'font_sha256': hashlib.sha256(lft).hexdigest(), 'glyph_count': len(cmap),
        'local_use': 'Generated from the installed game; do not distribute these extracted assets.'}, indent=2))
    # The local site's CSP permits same-origin fonts but not data: fonts.
    # Keep the font local and content-addressed; only the host opts into it.
    font_bytes = (output / 'da-local.ttf').read_bytes()
    font_name = 'da-local.ttf'
    font_public = ROOT.parents[1] / 'site/public/assets/host'
    font_public.mkdir(parents=True, exist_ok=True)
    (font_public / font_name).write_bytes(font_bytes)
    embedded = {'@font-face{font-family:"DA Local Bitmap";src:url("/assets/host/' + font_name + '") format("truetype");font-display:swap}'}
    variables = []
    for name in crops:
        variables.append('--da-' + name + ':url("data:image/png;base64,' + base64.b64encode((output / (name + '.png')).read_bytes()).decode() + '")')
    css = '\n'.join(embedded) + '\n:root{' + ';'.join(variables) + '}\n'
    (output / 'assets.css').write_text(css)
    print(json.dumps({'status': 'built', 'glyphs': len(cmap), 'textures': list(crops)}))


if __name__ == '__main__':
    build()
