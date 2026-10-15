"""Read TBWF built-in drawable resources from a user-supplied Hello Haylou APK.

This development tool exports drawable definitions that can be embedded in the
editor. It reads Android resource tables and binary XML directly, without
apktool, Java or additional Python dependencies. The editor's bundled library
does not require this tool or the APK at runtime.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import struct
import xml.etree.ElementTree as ET
import zipfile


def u16(data, offset):
    return struct.unpack_from('<H', data, offset)[0]


def u32(data, offset):
    return struct.unpack_from('<I', data, offset)[0]


def chunks(data, start, end):
    while start < end:
        kind, header, size = struct.unpack_from('<HHI', data, start)
        if size < header or size < 8 or start + size > end:
            raise ValueError('Invalid Android resource chunk')
        yield kind, header, start, size
        start += size


def strings(data, offset):
    count, _, flags, begin = struct.unpack_from('<IIII', data, offset + 8)
    header = u16(data, offset + 2)
    utf8 = bool(flags & 0x100)

    def length(pos):
        if utf8:
            n = data[pos]
            return (((n & 127) << 8) | data[pos + 1], pos + 2) if n & 128 else (n, pos + 1)
        n = u16(data, pos)
        return (((n & 32767) << 16) | u16(data, pos + 2), pos + 4) if n & 32768 else (n, pos + 2)

    result = []
    for index in range(count):
        pos = offset + begin + u32(data, offset + header + index * 4)
        n, pos = length(pos)
        if utf8:
            n, pos = length(pos)
        result.append(data[pos:pos + n * (1 if utf8 else 2)].decode('utf-8' if utf8 else 'utf-16le'))
    return result


def resource_table(data):
    global_strings = []
    resources = {}
    for kind, header, pos, size in chunks(data, u16(data, 2), len(data)):
        if kind == 1:
            global_strings = strings(data, pos)
        elif kind == 0x200:
            package_id = u32(data, pos + 8)
            types = strings(data, pos + u32(data, pos + 268))
            keys = strings(data, pos + u32(data, pos + 276))
            type_offset = u32(data, pos + 284) if header >= 288 else 0
            for ck, ch, cp, cs in chunks(data, pos + header, pos + size):
                if ck != 0x201:
                    continue
                type_id, flags = data[cp + 8:cp + 10]
                count, entries_start = u32(data, cp + 12), u32(data, cp + 16)
                # Prefer the default configuration for vectors and animations.
                score = sum(b != 0 for b in data[cp + 24:cp + ch])
                for index in range(count):
                    if flags & 1:
                        entry_id = u16(data, cp + ch + index * 4)
                        off = u16(data, cp + ch + index * 4 + 2) * 4
                    elif flags & 2:
                        entry_id = index
                        off = u16(data, cp + ch + index * 2)
                        if off == 0xffff:
                            continue
                        off *= 4
                    else:
                        entry_id = index
                        off = u32(data, cp + ch + index * 4)
                        if off == 0xffffffff:
                            continue
                    ep = cp + entries_start + off
                    if u16(data, ep + 2) & 1:  # complex/bag
                        continue
                    vp = ep + u16(data, ep)
                    value_type, value = data[vp + 3], u32(data, vp + 4)
                    rid = (package_id << 24) | ((type_id + type_offset) << 16) | entry_id
                    item = (types[type_id - 1], keys[u32(data, ep + 4)], value_type, value, score)
                    if rid not in resources or score < resources[rid][4]:
                        resources[rid] = item
    return global_strings, resources


def xml_document(data, resources):
    if data.lstrip().startswith(b'<'):
        return ET.fromstring(data)
    pool = []
    stack = []
    root = None

    def typed(kind, value):
        if kind == 3:
            return pool[value]
        if kind == 1:
            # Framework transparent is commonly aliased by color_transparent.
            if value == 0x0106000d:
                return '#00000000'
            item = resources.get(value)
            if item and item[0] == 'color' and item[2] in (1, 0x1c, 0x1d, 0x1e, 0x1f):
                if item[2] == 1 and item[3] == value:
                    raise ValueError('Circular Android color reference')
                return typed(item[2], item[3])
            return '@' + item[0] + '/' + item[1] if item else f'@0x{value:08x}'
        if kind == 2:
            return f'?0x{value:08x}'
        if kind == 4:
            return str(struct.unpack('<f', struct.pack('<I', value))[0])
        if kind in (5, 6):
            mantissa = value & 0xffffff00
            if mantissa & 0x80000000:
                mantissa -= 0x100000000
            return str(mantissa * (1 / 256, 1 / 32768, 1 / 8388608, 1 / 2147483648)[(value >> 4) & 3])
        if kind == 0x12:
            return 'true' if value else 'false'
        if 0x1c <= kind <= 0x1f:
            return f'#{value:08x}'
        return str(value if value < 0x80000000 else value - 0x100000000)

    for kind, header, pos, size in chunks(data, u16(data, 2), len(data)):
        if kind == 1:
            pool = strings(data, pos)
        elif kind == 0x102:
            ext = pos + header
            node = ET.Element(pool[u32(data, ext + 4)])
            attr_start, attr_size, count = struct.unpack_from('<HHH', data, ext + 8)
            for index in range(count):
                ap = ext + attr_start + index * attr_size
                name, raw = u32(data, ap + 4), u32(data, ap + 8)
                # Resolve references through the table even when raw text exists.
                node.set(pool[name], typed(data[ap + 15], u32(data, ap + 16))
                         if raw == 0xffffffff or data[ap + 15] in (1, 2) else pool[raw])
            if stack:
                stack[-1].append(node)
            else:
                root = node
            stack.append(node)
        elif kind == 0x103:
            stack.pop()
    if root is None:
        raise ValueError('Android drawable has no XML root')
    return root


def extract(apk: Path, destination: Path):
    with zipfile.ZipFile(apk) as archive:
        pool, resources = resource_table(archive.read('resources.arsc'))
        icons = {}
        destination.mkdir(parents=True, exist_ok=True)
        for _, (kind, name, value_type, value, _) in resources.items():
            if kind != 'drawable' or not name.startswith('ic_wf_') or value_type != 3:
                continue
            filename = pool[value]
            payload = archive.read(filename)
            if filename.lower().endswith('.xml'):
                output = destination / (name + '.xml')
                ET.ElementTree(xml_document(payload, resources)).write(output, encoding='utf-8', xml_declaration=True)
            else:
                output = destination / (name + Path(filename).suffix)
                output.write_bytes(payload)
            icons[name] = output.name
        if not icons:
            raise ValueError('This APK does not contain Hello Haylou watchface icons')
        stat = apk.stat()
        (destination / 'index.json').write_text(json.dumps({
            'version': 1, 'apk': str(apk.resolve()), 'size': stat.st_size, 'mtime_ns': stat.st_mtime_ns,
            'icons': icons,
        }, indent=2), encoding='utf-8')
        return icons


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('apk', type=Path)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    print(f'Loaded {len(extract(args.apk, args.destination))} TBWF built-in drawable resources.')
