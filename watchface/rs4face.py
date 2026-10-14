"""Build and inspect Haylou RS4 Plus (LS11) photo watchfaces.

The watch stores a SiFli "dial" package. The photo face used by Haylou Fun is
id 254 and contains two eZip images: a 368x448 background and a 260x317
thumbnail. The clock drawn on top of that photo is fixed by the watch.
"""

from __future__ import annotations

import argparse
import binascii
import json
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

SCREEN = (368, 448)
THUMB = (260, 317)
BG_PATH = "/ex/resource/custom_wf/custom_wf_bg.bin"
TN_PATH = "/ex/installer_wf/custom_wf_tn.bin"
CUSTOM_ID = 254
DEFAULT_NAME = "动态表盘"
HERE = Path(__file__).resolve().parent
EZIP = HERE / "tools" / "ezip.exe"

FORMATS = {
    1: "eZip without alpha",
    2: "eZip with alpha",
    4: "Pixels without alpha",
    5: "Pixels with alpha",
}


def crc16(blob: bytes) -> int:
    return binascii.crc_hqx(blob, 0) & 0xFFFF


def store_crc(value: int) -> int:
    value &= 0xFFFF
    if value & 0x8000:
        return value | 0xFFFF0000
    return value


def parse_dial(data: bytes) -> dict:
    if data[:4] != b"dial":
        raise ValueError("File is not a dial package (missing 'dial' magic)")
    version, ident, count, header_size = struct.unpack_from("<HHHH", data, 8)
    if header_size < 0x40 or header_size > len(data):
        raise ValueError(f"Invalid header: {header_size}")
    name = data[0x10:0x20].split(b"\x00", 1)[0].decode("utf-8", "replace")
    files = []
    offset = 0x40
    for _ in range(count):
        if offset + 64 > header_size:
            raise ValueError("Truncated file table")
        raw_crc, rel, length = struct.unpack_from("<III", data, offset)
        path = data[offset + 12 : offset + 64].split(b"\x00", 1)[0].decode("ascii", "replace")
        start = header_size + rel
        blob = data[start : start + length]
        if len(blob) != length:
            raise ValueError(f"{path}: truncated file")
        files.append(
            {
                "path": path,
                "crc16": raw_crc & 0xFFFF,
                "crc_ok": crc16(blob) == (raw_crc & 0xFFFF),
                "data": blob,
            }
        )
        offset += 64
    entry_crc = struct.unpack_from("<I", data, 0x20)[0] & 0xFFFF
    return {
        "version": version,
        "id": ident,
        "name": name,
        "header_size": header_size,
        "entry_crc_ok": crc16(data[0x40:header_size]) == entry_crc,
        "files": files,
    }


def pack_dial(files: list[tuple[str, bytes]], ident: int, name: str, version: int = 0x0701) -> bytes:
    if not files:
        raise ValueError("Package has no files")
    raw_name = name.encode("utf-8")
    if len(raw_name) > 15:
        raise ValueError("Name is too long: maximum 15 UTF-8 bytes")
    header_size = 0x40 + len(files) * 64
    header = bytearray(header_size)
    header[0:4] = b"dial"
    header[4:8] = b"\xff\xff\xff\xff"
    struct.pack_into("<HHHH", header, 8, version, ident, len(files), header_size)
    header[0x10 : 0x10 + len(raw_name)] = raw_name
    header[0x24:0x40] = b"\xff" * (0x40 - 0x24)
    payload = bytearray()
    for index, (path, blob) in enumerate(files):
        encoded = path.encode("ascii")
        if len(encoded) > 51:
            raise ValueError(f"Path is too long: {path}")
        entry = bytearray(64)
        struct.pack_into(
            "<III",
            entry,
            0,
            store_crc(crc16(blob)),
            len(payload),
            len(blob),
        )
        entry[12 : 12 + len(encoded)] = encoded
        start = 0x40 + index * 64
        header[start : start + 64] = entry
        payload += blob
    struct.pack_into("<I", header, 0x20, store_crc(crc16(header[0x40:header_size])))
    return bytes(header) + bytes(payload)


def image_header(blob: bytes) -> tuple[int, int, int] | None:
    if len(blob) < 4:
        return None
    word = struct.unpack_from("<I", blob, 0)[0]
    fmt = word & 0x1F
    width = (word >> 10) & 0x7FF
    height = (word >> 21) & 0x7FF
    if fmt in FORMATS and 1 <= width <= 1024 and 1 <= height <= 1024:
        return fmt, width, height
    return None


def require_ezip() -> Path:
    if not EZIP.is_file():
        raise SystemExit(f"eZip converter not found: {EZIP}")
    return EZIP


def run_ezip(args: list[str], cwd: Path) -> None:
    result = subprocess.run(
        [str(require_ezip()), *args],
        cwd=cwd,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if result.returncode != 0:
        detail = (result.stdout + result.stderr).strip()
        raise RuntimeError(detail or f"eZip failed with exit code {result.returncode}")


def png_to_ezip(png: Path) -> bytes:
    with tempfile.TemporaryDirectory(prefix="rs4ezip-") as folder:
        work = Path(folder)
        run_ezip(
            ["-convert", str(png), "-rgb565", "-binfile", "2", "-binext", ".bin"],
            work,
        )
        found = list(work.rglob("*.bin"))
        if len(found) != 1:
            raise RuntimeError(f"eZip did not produce exactly one .bin in {work}")
        return found[0].read_bytes()


def ezip_to_png(blob: bytes, dest: Path) -> bool:
    header = image_header(blob)
    if header is None or header[0] not in (1, 2):
        return False
    with tempfile.TemporaryDirectory(prefix="rs4dec-") as folder:
        work = Path(folder)
        source = work / "image.bin"
        source.write_bytes(blob)
        out = work / "png"
        out.mkdir()
        try:
            run_ezip(
                ["-convert", str(source), "-spt", "1", "-dpt", "0", "-outdir", str(out)],
                work,
            )
        except RuntimeError:
            return False
        produced = list(out.rglob("*.png"))
        if not produced:
            return False
        dest.write_bytes(produced[0].read_bytes())
        return True


def fit_image(source: Path, size: tuple[int, int], mode: str, pad: tuple[int, int, int, int]):
    from PIL import Image

    image = Image.open(source).convert("RGBA")
    width, height = size
    if mode == "contain":
        image.thumbnail((width, height), Image.Resampling.LANCZOS)
        canvas = Image.new("RGBA", size, pad)
        canvas.paste(image, ((width - image.width) // 2, (height - image.height) // 2), image)
        return canvas
    scale = max(width / image.width, height / image.height)
    resized = image.resize(
        (max(1, round(image.width * scale)), max(1, round(image.height * scale))),
        Image.Resampling.LANCZOS,
    )
    left = (resized.width - width) // 2
    top = (resized.height - height) // 2
    return resized.crop((left, top, left + width, top + height))


def parse_color(text: str) -> tuple[int, int, int, int]:
    raw = text.removeprefix("#")
    if len(raw) != 6 or any(c not in "0123456789abcdefABCDEF" for c in raw):
        raise argparse.ArgumentTypeError("Color must be RRGGBB, for example 000000")
    return (int(raw[0:2], 16), int(raw[2:4], 16), int(raw[4:6], 16), 255)


def cmd_info(path: Path) -> None:
    dial = parse_dial(path.read_bytes())
    print(f"{path}")
    print(f"  version 0x{dial['version']:04x}  ID {dial['id']}  name {dial['name']!r}")
    print(f"  table CRC: {'ok' if dial['entry_crc_ok'] else 'ERRADO'}")
    for item in dial["files"]:
        header = image_header(item["data"])
        extra = ""
        if header:
            fmt, width, height = header
            extra = f"  image {width}x{height} {FORMATS[fmt]}"
        flag = "ok" if item["crc_ok"] else "INVALID"
        print(f"  {item['path']}  {len(item['data'])} bytes  crc {flag}{extra}")


def cmd_unpack(path: Path, dest: Path, export_png: bool) -> None:
    dial = parse_dial(path.read_bytes())
    dest.mkdir(parents=True, exist_ok=True)
    manifest_files = []
    for index, item in enumerate(dial["files"]):
        filename = f"{index:02d}_{Path(item['path']).name}"
        (dest / filename).write_bytes(item["data"])
        entry = {"path": item["path"], "file": filename}
        if export_png and ezip_to_png(item["data"], dest / f"{filename}.png"):
            entry["png"] = f"{filename}.png"
        manifest_files.append(entry)
    manifest = {
        "version": dial["version"],
        "id": dial["id"],
        "name": dial["name"],
        "files": manifest_files,
    }
    (dest / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Extracted to {dest}")


def cmd_pack(source: Path, dest: Path) -> None:
    manifest = json.loads((source / "manifest.json").read_text(encoding="utf-8"))
    files = []
    for item in manifest["files"]:
        files.append((item["path"], (source / item["file"]).read_bytes()))
    blob = pack_dial(files, int(manifest["id"]), manifest["name"], int(manifest["version"]))
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(blob)
    print(f"Written {dest} ({len(blob)} bytes)")


SAMPLES = {
    "hour": "12",
    "timeSeparator": ":",
    "minute": "34",
    "month": "10",
    "dateSeparator": "/",
    "day": "12",
    "battery": "15",
    "weatherTemperature": "23",
    "step": "1234",
    "heartRate": "72",
}


def argb_color(value: int) -> tuple[int, int, int, int]:
    value &= 0xFFFFFFFF
    alpha = (value >> 24) & 255
    red = (value >> 16) & 255
    green = (value >> 8) & 255
    blue = value & 255
    if alpha == 0:
        alpha = 255
    return red, green, blue, alpha


def cmd_tbwf(source: Path, dest: Path, name: str) -> None:
    import io
    import zipfile

    from PIL import Image, ImageDraw, ImageFont

    with zipfile.ZipFile(source) as package:
        description = json.loads(package.read("TiBoWaFe.json"))
        background = Image.open(io.BytesIO(package.read("/resourceImage/dial.png"))).convert("RGBA")
        font_bytes = package.read("/font/CONSOLA.ttf") if "/font/CONSOLA.ttf" in package.namelist() else None
    if background.size != SCREEN:
        fitted = fit_image_obj(background, SCREEN)
    else:
        fitted = background
    with tempfile.TemporaryDirectory(prefix="rs4tbwf-") as folder:
        png = Path(folder) / "dial.png"
        fitted.save(png)
        cmd_create(png, dest, "cover", (0, 0, 0, 255), name)
    preview = fitted.copy()
    draw = ImageDraw.Draw(preview)
    elements = description.get("elements", {})
    for key, sample in SAMPLES.items():
        element = elements.get(key)
        if not element or not element.get("visible"):
            continue
        size = element.get("font", {}).get("size", 0.06)
        px = max(8, round(float(size) * SCREEN[1]))
        fill = argb_color(int(element.get("color", {}).get("colors", [-1])[0]))
        x = round(float(element.get("x", 0)) * SCREEN[0])
        y = round(float(element.get("y", 0)) * SCREEN[1])
        font = ImageFont.load_default()
        if font_bytes:
            font = ImageFont.truetype(io.BytesIO(font_bytes), px)
        draw.text((x, y), sample, font=font, fill=fill)
    preview_path = dest.with_suffix(".preview.png")
    preview.convert("RGB").save(preview_path)
    print(f"TBWF preview: {preview_path}")
    print("The .bin contains only artwork. The watch draws time, date and weekday at its fixed positions, not the TBWF positions.")


def fit_image_obj(image, size: tuple[int, int]):
    from PIL import Image

    width, height = size
    scale = max(width / image.width, height / image.height)
    resized = image.resize(
        (max(1, round(image.width * scale)), max(1, round(image.height * scale))),
        Image.Resampling.LANCZOS,
    )
    left = (resized.width - width) // 2
    top = (resized.height - height) // 2
    return resized.crop((left, top, left + width, top + height))


def cmd_create(image: Path, dest: Path, fit: str, pad: tuple[int, int, int, int], name: str) -> None:
    background = fit_image(image, SCREEN, fit, pad)
    thumbnail = fit_image(image, THUMB, fit, pad)
    with tempfile.TemporaryDirectory(prefix="rs4face-") as folder:
        work = Path(folder)
        bg_png = work / "bg.png"
        tn_png = work / "tn.png"
        background.save(bg_png)
        thumbnail.save(tn_png)
        bg = png_to_ezip(bg_png)
        tn = png_to_ezip(tn_png)
    bg_header = image_header(bg)
    tn_header = image_header(tn)
    if bg_header != (2, *SCREEN):
        raise RuntimeError(f"fundo inesperado: {bg_header}")
    if tn_header != (2, *THUMB):
        raise RuntimeError(f"miniatura inesperada: {tn_header}")
    blob = pack_dial([(BG_PATH, bg), (TN_PATH, tn)], CUSTOM_ID, name)
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(blob)
    guide = dest.with_suffix(".guia.png")
    preview = background.copy()
    draw_layout(preview)
    preview.convert("RGB").save(guide)
    print(f"Written {dest} ({len(blob)} bytes)")
    print(f"Fixed-clock placement guide: {guide}")
    print("This file replaces the photo. Time, date and weekday keep the firmware's fixed positions.")


def draw_layout(image) -> None:
    from PIL import ImageDraw

    draw = ImageDraw.Draw(image)
    boxes = {
        "hora": (33, 43, 33 + 62 * 2, 43 + 96),
        "minuto": (213, 43, 213 + 62 * 2, 43 + 96),
        "dia": (119, 148, 119 + 26 * 2, 148 + 53),
        "semana": (185, 148, 185 + 93, 148 + 53),
    }
    for _label, box in boxes.items():
        draw.rectangle(box, outline=(255, 255, 255, 230), width=2)


def main() -> None:
    parser = argparse.ArgumentParser(description="Haylou RS4 Plus photo watchfaces")
    sub = parser.add_subparsers(dest="command", required=True)

    info = sub.add_parser("info", help="Inspect a .bin package")
    info.add_argument("bin", type=Path)

    unpack = sub.add_parser("unpack", help="Extract the internal files")
    unpack.add_argument("bin", type=Path)
    unpack.add_argument("dest", type=Path)
    unpack.add_argument("--png", action="store_true", help="Also export eZip images")

    pack = sub.add_parser("pack", help="Rebuild a .bin from unpacked files")
    pack.add_argument("source", type=Path)
    pack.add_argument("bin", type=Path)

    create = sub.add_parser("create", help="Generate a 368x448 photo watchface")
    create.add_argument("image", type=Path)
    create.add_argument("-o", "--output", type=Path, required=True)
    create.add_argument("--fit", choices=("cover", "contain"), default="cover")
    create.add_argument("--pad", type=parse_color, default=(0, 0, 0, 255))
    create.add_argument("--name", default=DEFAULT_NAME)

    tbwf = sub.add_parser("tbwf", help="Convert a TBWF to an LS11 photo face")
    tbwf.add_argument("tbwf", type=Path)
    tbwf.add_argument("-o", "--output", type=Path, required=True)
    tbwf.add_argument("--name", default=DEFAULT_NAME)

    args = parser.parse_args()
    if args.command == "info":
        cmd_info(args.bin)
    elif args.command == "unpack":
        cmd_unpack(args.bin, args.dest, args.png)
    elif args.command == "pack":
        cmd_pack(args.source, args.bin)
    elif args.command == "create":
        cmd_create(args.image, args.output, args.fit, args.pad, args.name)
    elif args.command == "tbwf":
        cmd_tbwf(args.tbwf, args.output, args.name)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, RuntimeError, OSError) as exc:
        print(f"Error: {exc}", file=sys.stderr)
        sys.exit(1)
