"""Build a live LS11 dial from a 368x448 PNG and a widget layout.

The code module is recompiled so the label positions match the editor.
The resource module embeds the background. The thumbnail is a 260x317 eZip.
"""

from __future__ import annotations

import argparse
import json
import os
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
MODULE = HERE / "module"
GCC = Path(os.environ.get("HAYLOU_ARM_GCC") or str(
    Path(os.environ.get("LOCALAPPDATA", str(Path.home() / "AppData" / "Local")))
    / "HaylouFaceEditor" / "tools" / "armgcc" / "xpack-arm-none-eabi-gcc-12.3.1-1.2"
    / "bin" / "arm-none-eabi-gcc.exe"))
if not GCC.is_file() and not os.environ.get("HAYLOU_ARM_GCC"):
    GCC = Path(os.environ.get("TEMP", tempfile.gettempdir())) / "armgcc" / "xpack-arm-none-eabi-gcc-12.3.1-1.2" / "bin" / "arm-none-eabi-gcc.exe"

SOURCES = {
    "text": 0,
    "hour": 1,
    "minute": 2,
    "second": 3,
    "month": 4,
    "day": 5,
    "wday": 6,
    "steps": 7,
    "heart": 8,
    "battery": 9,
    "scaled": 10,
    "temperature": 11,
    "calories": 12,
    "distance": 13,
    "ampm": 14,
    "distance_unit": 15,
    "year": 16,
    "distance_int": 17,
}

sys.path.insert(0, str(HERE))
from rs4face import pack_dial, parse_dial, png_to_ezip  # noqa: E402


def rgb565(value: str) -> int:
    text = value.removeprefix("#").removeprefix("0x")
    if len(text) != 6:
        raise ValueError(f"Invalid color: {value}")
    red = int(text[0:2], 16)
    green = int(text[2:4], 16)
    blue = int(text[4:6], 16)
    return ((red >> 3) << 11) | ((green >> 2) << 5) | (blue >> 3)


def c_string(value: str) -> str:
    return value.replace("\\", "\\\\").replace('"', '\\"')


def truecolor_alpha(path: Path, width: int = 0, height: int = 0) -> tuple[bytes, int]:
    """Uncompressed LV_IMG_CF_TRUE_COLOR_ALPHA. Rotation faults on ezip."""
    from PIL import Image

    image = Image.open(path).convert("RGBA")
    if width or height:
        width = width or max(1, int(image.width * height / image.height + 0.5))
        height = height or max(1, int(image.height * width / image.width + 0.5))
        if not (1 <= width <= 368 and 1 <= height <= 448):
            raise ValueError("Clock hand must fit within 368x448")
        image = image.resize((width, height), Image.Resampling.LANCZOS)
    width, height = image.size
    if not (1 <= width <= 2047 and 1 <= height <= 2047):
        raise ValueError(f"Clock hand is too large: {width}x{height}")
    raw = image.tobytes()
    pixels = bytearray()
    for offset in range(0, len(raw), 4):
        red, green, blue, alpha = raw[offset : offset + 4]
        color = ((red >> 3) << 11) | ((green >> 2) << 5) | (blue >> 3)
        pixels.append(color & 0xFF)
        pixels.append(color >> 8)
        pixels.append(alpha)
    expected = width * height * 3
    if len(pixels) != expected:
        raise RuntimeError(f"Clock hand {path.name} produced {len(pixels)} bytes")
    header = 5 | (width << 10) | (height << 21)
    return bytes(pixels), header


DYNAMIC_GLYPHS = {
    "hour": (2, "0123456789"),
    "minute": (2, "0123456789"),
    "second": (2, "0123456789"),
    "month": (2, "0123456789"),
    "day": (2, "0123456789"),
    "year": (4, "0123456789"),
    "wday": (1, "0123456789"),
    "steps": (5, "0123456789"),
    "heart": (3, "0123456789-"),
    "battery": (3, "0123456789-"),
    "scaled": (5, "0123456789"),
    "temperature": (4, "0123456789-"),
    "calories": (5, "0123456789"),
    "distance": (8, "0123456789."),
    "distance_int": (5, "0123456789"),
    "ampm": (2, "APM"),
    "distance_unit": (2, "kmi"),
}


def glyph_request(item: dict) -> dict | None:
    font = str(item.get("font") or "").strip()
    characters = item.get("characters") or {}
    if not font and not characters:
        return None
    if characters:
        if not isinstance(characters, dict) or any(len(ch) != 1 or ord(ch) > 127 for ch in characters):
            raise ValueError("Character maps require individual ASCII keys")
        for ch, file in characters.items():
            if not Path(file).is_file():
                raise ValueError(f"Character image {ch!r} not found: {file}")
    elif not Path(font).is_file():
        raise ValueError(f"Font not found: {font}")
    source = str(item.get("source", "text"))
    size = max(8, min(120, int(item.get("size", 24))))
    if source == "text":
        text = str(item.get("text") or " ")
        if len(text) > 12:
            raise ValueError(f"Text is too long to rasterize: {text}")
        alphabet = "".join(dict.fromkeys(text + " "))
        return {
            "font": font,
            "characters": characters,
            "size": size,
            "color": str(item.get("color", "#FFFFFF")),
            "slots": len(text),
            "alphabet": alphabet,
            "text": text,
        }
    if source not in DYNAMIC_GLYPHS:
        raise ValueError(f"Unknown data source: {source}")
    slots, alphabet = DYNAMIC_GLYPHS[source]
    return {
        "font": font,
        "characters": characters,
        "size": size,
        "color": str(item.get("color", "#FFFFFF")),
        "slots": slots,
        "alphabet": alphabet + " ",
        "text": "",
    }


def render_glyph(font, size: int, fill: tuple[int, int, int, int], ch: str, cell: tuple[int, int], dest: Path) -> None:
    from PIL import Image, ImageDraw

    image = Image.new("RGBA", cell, (0, 0, 0, 0))
    if ch != " ":
        draw = ImageDraw.Draw(image)
        draw.text((cell[0] / 2, cell[1] / 2), ch, font=font, fill=fill, anchor="mm")
    image.save(dest)


def prepare_glyph_rasters(widgets: list, work: Path) -> tuple[list[tuple[str, Path]], dict]:
    from PIL import Image, ImageDraw, ImageFont

    def group_key(plan):
        if plan["characters"]:
            return ("images", tuple(sorted(plan["characters"].items())), plan["size"], plan["color"])
        return ("font", plan["font"], plan["size"], plan["color"])

    plans = [glyph_request(item) for item in widgets]
    groups: dict[tuple, set[str]] = {}
    for plan in plans:
        if plan is None:
            continue
        key = group_key(plan)
        groups.setdefault(key, set()).update(plan["alphabet"])
    rasters: list[tuple[str, Path]] = []
    set_of: dict[tuple, int] = {}
    pitch_of: dict[int, int] = {}
    height_of: dict[int, int] = {}
    fallback: dict[int, str] = {}
    externs = []
    lookups = []
    for index, (key, chars) in enumerate(groups.items()):
        kind, asset, size, color = key
        ordered = "".join(sorted(chars, key=ord))
        if len(ordered) > 40:
            raise ValueError("Too many characters requested for one font")
        character_images = {}
        if kind == "images":
            originals = {ch: Image.open(file).convert("RGBA") for ch, file in asset}
            missing = set(ordered) - originals.keys() - {" ", "-", "."}
            if missing:
                raise ValueError(f"Missing character images: {''.join(sorted(missing))}")
            largest = max(max(image.size) for image in originals.values())
            scale = size / largest
            character_images = {ch: image.resize(
                (max(1, int(image.width * scale + 0.5)), max(1, int(image.height * scale + 0.5))),
                Image.Resampling.LANCZOS,
            ) for ch, image in originals.items()}
            cell_w = max(image.width for image in character_images.values())
            cell_h = max(image.height for image in character_images.values())
        else:
            font = ImageFont.truetype(asset, size)
            ascent, descent = font.getmetrics()
            cell_h = max(1, ascent + descent)
            widths = []
            for ch in ordered:
                box = font.getbbox("0" if ch == " " else ch)
                widths.append(max(1, box[2] - box[0]))
            cell_w = max(widths)
        red, green, blue = rgb565_bytes(color)
        fill = (red, green, blue, 255)
        symbols = []
        for ch in ordered:
            png = work / f"g{index}_{ord(ch)}.png"
            if kind == "images":
                canvas = Image.new("RGBA", (cell_w, cell_h), (0, 0, 0, 0))
                if ch in character_images:
                    canvas.paste(character_images[ch], (0, 0))
                elif ch == "-":
                    ImageDraw.Draw(canvas).line(
                        (cell_w // 4, cell_h // 2, cell_w * 3 // 4, cell_h // 2),
                        fill=fill, width=max(1, cell_h // 12),
                    )
                elif ch == ".":
                    dot = max(1, cell_h // 10)
                    ImageDraw.Draw(canvas).rectangle(
                        (cell_w // 2, cell_h - dot, cell_w // 2 + dot - 1, cell_h - 1), fill=fill)
                canvas.save(png)
            else:
                render_glyph(font, size, fill, ch, (cell_w, cell_h), png)
            symbol = f"g{index}c{ord(ch)}"
            rasters.append((symbol, png))
            externs.append(f"extern img_dsc_t {symbol};")
            symbols.append((ch, symbol))
        set_of[key] = index
        pitch_of[index] = cell_w
        height_of[index] = cell_h
        space = next(symbol for ch, symbol in symbols if ch == " ")
        fallback[index] = space
        tests = " ".join(f"if (ch == {ord(ch)}) return &{symbol};" for ch, symbol in symbols)
        lookups.append(f"if (set == {index}) {{ {tests} return &{space}; }}")
    lines = []
    slot = 0
    for widget_index, (item, plan) in enumerate(zip(widgets, plans)):
        if plan is None:
            continue
        key = group_key(plan)
        set_index = set_of[key]
        pitch = pitch_of[set_index] + int(item.get("spacing", 0) or 0)
        if not 1 <= pitch <= 368:
            raise ValueError("Character spacing puts the advance outside the 1–368 pixel range")
        lines.append(
            {
                "x": int(item["x"]),
                "y": int(item["y"]),
                "widget_index": widget_index,
                "width": pitch_of[set_index],
                "height": height_of[set_index],
                "pitch": pitch,
                "slots": plan["slots"],
                "source": SOURCES[str(item.get("source", "text"))],
                "set": set_index,
                "slot": slot,
                "text": plan["text"],
            }
        )
        slot += plan["slots"]
    if slot > 64:
        raise ValueError("The watchface needs too many numeric glyph slots")
    blank = fallback.get(0, "")
    tail = f"return &{blank};" if blank else "return 0;"
    info = {
        "lines": lines,
        "slots": slot,
        "externs": " ".join(externs),
        "lookup": (" ".join(lookups) + " " + tail).strip(),
    }
    return rasters, info


def prepare_glyphs(widgets: list, work: Path) -> tuple[list[tuple[str, bytes, int]], dict]:
    rasters, info = prepare_glyph_rasters(widgets, work)
    images = []
    for symbol, png in rasters:
        blob = png_to_ezip(png)
        if len(blob) < 8:
            raise RuntimeError(f"Incomplete eZip glyph: {symbol}")
        images.append((symbol, blob[4:], int.from_bytes(blob[:4], "little")))
    return images, info


def rgb565_bytes(color: str) -> tuple[int, int, int]:
    text = str(color).strip().lstrip("#")
    if len(text) != 6:
        raise ValueError(f"Invalid color: {color}")
    return int(text[0:2], 16), int(text[2:4], 16), int(text[4:6], 16)


def layer_index(item: dict, default: int) -> int:
    value = item.get("z_index", default)
    if isinstance(value, bool) or not isinstance(value, int) or not -(2**31) <= value < 2**31:
        raise ValueError("Z-index must be a signed 32-bit integer")
    return value


def draw_order(widgets: list, hands: list, glyphs: dict, animations: list) -> list[tuple[int, int]]:
    """Bottom to top; equal indices follow the editor's stable collection order.

    Kinds: 1/2/3 hour/minute/second hand, 4 glyph line, 5 animation, 6 label.
    All slots of a text element remain together in the same layer.
    """
    entries = []
    for index, item in enumerate(animations):
        entries.append((layer_index(item, 0), 0, index, 5, index))
    hand_kinds = {"hour": 1, "minute": 2, "second": 3}
    for index, item in enumerate(hands):
        if item.get("kind") in hand_kinds:
            entries.append((layer_index(item, 100), 1, index, hand_kinds[item["kind"]], 0))
    glyph_by_widget = {line.get("widget_index", index): index for index, line in enumerate(glyphs.get("lines", []))}
    label_index = 0
    for index, item in enumerate(widgets):
        z = layer_index(item, 200)
        if index in glyph_by_widget:
            entries.append((z, 2, index, 4, glyph_by_widget[index]))
        elif not str(item.get("font") or "").strip() and not item.get("characters"):
            entries.append((z, 2, index, 6, label_index))
            label_index += 1
    return [(kind, index) for _, _, _, kind, index in sorted(entries)]


def write_layout(path: Path, face: str, title: str, widgets: list, hands: list, glyphs: dict | None = None, animations: list[dict] | None = None) -> None:
    labels = [item for item in widgets if not str(item.get("font") or "").strip() and not item.get("characters")]
    if len(labels) > 32:
        raise ValueError("The face supports at most 32 system labels")
    lines = [
        f'#define FACE "{c_string(face)}"',
        f'#define TITLE "{c_string(title)}"',
        "#define SRC_TEXT 0",
        "#define SRC_HOUR 1",
        "#define SRC_MINUTE 2",
        "#define SRC_SECOND 3",
        "#define SRC_MONTH 4",
        "#define SRC_DAY 5",
        "#define SRC_WDAY 6",
        "#define SRC_STEPS 7",
        "#define SRC_HEART 8",
        "#define SRC_BATTERY 9",
        "#define SRC_SCALED 10",
        "#define SRC_TEMPERATURE 11",
        "#define SRC_CALORIES 12",
        "#define SRC_DISTANCE 13",
        "#define SRC_AMPM 14",
        "#define SRC_DISTANCE_UNIT 15",
        "#define SRC_YEAR 16",
        "#define SRC_DISTANCE_INT 17",
        f"#define HAS_TEMPERATURE {int(any(item.get('source') == 'temperature' for item in widgets))}",
        f"#define HAS_DISTANCE {int(any(item.get('source') in ('distance', 'distance_int', 'distance_unit') for item in widgets) or any(item.get('source') == 'distance' for item in (animations or [])))}",
        f"#define HAS_AMPM_ANIMATION {int(any(item.get('source') == 'ampm' for item in (animations or [])))}",
        "typedef struct {",
        "    int x;",
        "    int y;",
        "    unsigned color;",
        "    int size;",
        "    int source;",
        "    const char *text;",
        "} label_def_t;",
        "static const label_def_t label_defs[] = {",
    ]
    for item in labels:
        source = str(item.get("source", "text"))
        if source not in SOURCES:
            raise ValueError(f"Unknown data source: {source}")
        text = c_string(str(item.get("text", "")))
        lines.append(
            "    {%d, %d, 0x%04Xu, %d, %d, \"%s\"},"
            % (int(item["x"]), int(item["y"]), rgb565(item["color"]), 1, SOURCES[source], text)
        )
    if labels:
        lines += [
            "};",
            f"#define LABEL_COUNT {len(labels)}",
        ]
    else:
        lines += [
            "    {0, 0, 0, 0, 0, \"\"},",
            "};",
            "#define LABEL_COUNT 0",
        ]
    info = glyphs or {"lines": [], "slots": 0, "externs": "", "lookup": "return 0;"}
    lines.append(f"#define GLYPH_LINE_COUNT {len(info['lines'])}")
    lines.append(f"#define GLYPH_SLOT_COUNT {int(info['slots'])}")
    lines.append(f"#define GLYPH_EXTERNS {info['externs'] or ''}")
    lines.append(f"#define GLYPH_LOOKUP {info['lookup']}")
    if info["lines"]:
        lines += [
            "typedef struct {",
            "    int x;",
            "    int y;",
            "    int pitch;",
            "    int slots;",
            "    int source;",
            "    int set;",
            "    int slot;",
            "    const char *text;",
            "} glyph_line_t;",
            "static const glyph_line_t glyph_lines[] = {",
        ]
        for line in info["lines"]:
            lines.append(
                '    {%d, %d, %d, %d, %d, %d, %d, "%s"},'
                % (
                    line["x"],
                    line["y"],
                    line["pitch"],
                    line["slots"],
                    line["source"],
                    line["set"],
                    line["slot"],
                    c_string(line["text"]),
                )
            )
        lines.append("};")
    animation_info = animations or []
    animation_externs = []
    animation_defs = []
    for index, animation in enumerate(animation_info):
        symbols = animation["symbols"]
        animation_externs.extend(f"extern img_dsc_t {symbol};" for symbol in symbols)
        animation_defs.append(
            "static const img_dsc_t *const animation_%d_frames[] = {%s};"
            % (index, ", ".join("&" + symbol for symbol in symbols))
        )
        offsets = animation["offsets"]
        animation_defs.append(
            "static const int animation_%d_offsets[] = {%s};"
            % (index, ", ".join(str(value) for offset in offsets for value in (offset["x"], offset["y"])))
        )
    if animation_info:
        rows = []
        for index, animation in enumerate(animation_info):
            source = {
                "static": 11,
                "loop": 0,
                "battery": 1,
                "weekday": 2,
                "day": 3,
                "month": 4,
                "second": 5,
                "minute": 6,
                "hour": 7,
                "steps": 8,
                "heart": 9,
                "scaled": 10,
                "calories": 12,
                "distance": 13,
                "ampm": 14,
            }.get(animation["source"], 0)
            rows.append(
                "{%d, %d, %d, %d, %d, animation_%d_frames, animation_%d_offsets}"
                % (
                    animation["x"],
                    animation["y"],
                    animation["interval"],
                    len(animation["symbols"]),
                    source,
                    index,
                    index,
                )
            )
        animation_defs.append(
            "static const animation_def_t animation_defs[] = {%s};" % ", ".join(rows)
        )
    lines.append(f"#define ANIMATION_COUNT {len(animation_info)}")
    lines.append(f"#define ANIMATION_EXTERN {' '.join(animation_externs)}")
    lines.append(f"#define ANIMATION_DEFS {' '.join(animation_defs)}")
    by_kind = {str(item.get("kind")): item for item in hands}
    for kind in ("hour", "minute", "second"):
        item = by_kind.get(kind)
        upper = kind.upper()
        if not item:
            lines.append(f"#define HAS_{upper} 0")
            continue
        lines.append(f"#define HAS_{upper} 1")
        lines.append(f"#define {upper}_PIVOT_X {int(item['pivotx'])}")
        lines.append(f"#define {upper}_PIVOT_Y {int(item['pivoty'])}")
        lines.append(f"#define {upper}_POS_X {int(item['posx'])}")
        lines.append(f"#define {upper}_POS_Y {int(item['posy'])}")
    order = draw_order(widgets, hands, info, animation_info)
    lines += [
        "typedef struct { int kind; int index; } draw_item_t;",
        f"#define DRAW_ORDER_COUNT {len(order)}",
        "static const draw_item_t draw_items[] = {" + (", ".join("{%d, %d}" % entry for entry in order) or "{0, 0}") + "};",
    ]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def prepare_animations(animations: list, work: Path) -> tuple[list[tuple[str, bytes, int]], list[dict]]:
    """Pack frame sequences as ordinary eZip images for lv_img_set_src."""
    images: list[tuple[str, bytes, int]] = []
    result: list[dict] = []
    for index, raw in enumerate(animations):
        source = str(raw.get("source", "loop"))
        if source not in ("static", "loop", "battery", "weekday", "day", "month", "second", "minute", "hour", "steps", "heart", "scaled", "calories", "distance", "ampm"):
            raise ValueError(f"Unknown animation mode: {source}")
        frames = [Path(str(path)) for path in raw.get("frames", [])]
        if not 1 <= len(frames) <= 64:
            raise ValueError("An animation must have 1–64 frames")
        if source == "ampm" and len(frames) != 2:
            raise ValueError("AM/PM requires exactly two frames: AM first, PM second")
        raw_offsets = raw.get("offsets", [])
        offsets = []
        for frame_index in range(len(frames)):
            raw_offset = raw_offsets[frame_index] if frame_index < len(raw_offsets) else {}
            if not isinstance(raw_offset, dict):
                raw_offset = {}
            offsets.append(
                {
                    # Projects written by older editor builds used C#'s
                    # default property spelling (X/Y).  Accept both forms so
                    # a saved per-frame alignment is never silently lost.
                    "x": max(-368, min(368, int(raw_offset.get("x", raw_offset.get("X", 0)) or 0))),
                    "y": max(-448, min(448, int(raw_offset.get("y", raw_offset.get("Y", 0)) or 0))),
                }
            )
        symbols = []
        for frame_index, frame in enumerate(frames):
            if not frame.is_file():
                raise ValueError(f"Frame not found: {frame}")
            source_file = frame
            width = int(raw.get("width", 0) or 0)
            height = int(raw.get("height", 0) or 0)
            if width or height:
                from PIL import Image

                original = Image.open(frame).convert("RGBA")
                width = width or max(1, round(original.width * height / original.height))
                height = height or max(1, round(original.height * width / original.width))
                if not (1 <= width <= 368 and 1 <= height <= 448):
                    raise ValueError("Animation frames must fit within 368x448")
                source_file = work / f"anim_{index}_{frame_index}.png"
                original.resize((width, height), Image.Resampling.LANCZOS).save(source_file)
            blob = png_to_ezip(source_file)
            if len(blob) < 8:
                raise RuntimeError(f"Incomplete eZip frame: {frame.name}")
            symbol = f"anim_{index}_{frame_index}"
            images.append((symbol, blob[4:], int.from_bytes(blob[:4], "little")))
            symbols.append(symbol)
        result.append(
            {
                "x": int(raw["x"]),
                "y": int(raw["y"]),
                "interval": max(200, min(10000, int(raw.get("interval", 1000)))),
                "source": source,
                "z_index": layer_index(raw, 0),
                "symbols": symbols,
                "offsets": offsets,
            }
        )
    return images, result


def write_res(path: Path, images: list[tuple[str, bytes, int]]) -> None:
    parts = [
        "typedef struct { unsigned header; unsigned data_size; const unsigned char *data; } img_dsc_t;",
        # The built-in resource manager needs a nonempty, NULL-terminated
        # language pack, even when all text is supplied by our own widgets.
        # This is a resource symbol, not an export provided by the firmware.
        "typedef struct { const char *locale; const void *translation; } lv_i18n_lang_t;",
        'static const lv_i18n_lang_t face_language = { "en_us", 0 };',
        "const lv_i18n_lang_t *const lv_i18n_lang_pack[] = { &face_language, 0 };",
    ]
    for symbol, pixels, header in images:
        rows = []
        for offset in range(0, len(pixels), 16):
            chunk = pixels[offset : offset + 16]
            rows.append("  " + ",".join(f"0x{byte:02x}" for byte in chunk) + ",")
        parts.append(
            f'__attribute__((section(".ROM3_IMG.{symbol}"), used, aligned(4)))'
        )
        parts.append(f"static const unsigned char {symbol}_px[] = {{")
        parts.extend(rows)
        parts.append("};")
        parts.append(
            f'const img_dsc_t {symbol} = {{ 0x{header:08X}u, sizeof({symbol}_px), {symbol}_px }};'
        )
    path.write_text("\n".join(parts) + "\n", encoding="ascii")


def dsc_for(face: str) -> bytes:
    parts = [
        (0x06, face),
        (0x0A, face),
        (0x08, f"{face}.so"),
        (0x09, f"{face}_tn.bin"),
        (0x07, f"{face}_res.so"),
    ]
    out = bytearray()
    for kind, text in parts:
        raw = text.encode("ascii") + b"\x00"
        out.append(kind)
        out.append(len(raw))
        out += raw
    return bytes(out)


def run(cmd: list[str]) -> None:
    result = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        detail = (result.stdout + "\n" + result.stderr).strip()
        raise RuntimeError(detail or f"Command failed: {cmd[0]}")


def compile_so(work: Path, face: str) -> tuple[bytes, bytes]:
    if not GCC.is_file():
        raise RuntimeError(f"ARM GCC not found at {GCC}")
    flags = [
        str(GCC),
        "-mcpu=cortex-m33",
        "-mthumb",
        "-mfpu=fpv5-sp-d16",
        "-mfloat-abi=hard",
        "-Os",
        "-fPIC",
        "-fno-builtin",
        # The LS11 loader resolves ABS32/GLOB_DAT relocations used by the
        # firmware table, but not a compiler-created PLT/JUMP_SLOT call to
        # memset.  Keep small initialization loops as ordinary instructions.
        "-fno-tree-loop-distribute-patterns",
        "-nostdlib",
    ]
    code = work / "code.so"
    res = work / "res.so"
    run(
        [
            *flags,
            "-shared",
            "-nostartfiles",
            f"-include{work / 'face_layout.h'}",
            "-Wl,-z,max-page-size=4",
            f"-Wl,-soname,{face}.so",
            str(MODULE / "clock.c"),
            str(MODULE / "imports.c"),
            "-o",
            str(code),
        ]
    )
    run(
        [
            *flags,
            "-shared",
            "-nostartfiles",
            "-Wl,-z,max-page-size=4",
            f"-Wl,-soname,{face}_res.so",
            str(work / "res.c"),
            "-o",
            str(res),
        ]
    )
    return code.read_bytes(), res.read_bytes()


def elf_sections(blob: bytes) -> list[tuple[int, int, int, int, int]]:
    """Return type, file offset, size, linked section and entry size (ELF32)."""
    if blob[:7] != b"\x7fELF\x01\x01\x01" or len(blob) < 52:
        raise RuntimeError("The module is not little-endian ELF32")
    offset = int.from_bytes(blob[32:36], "little")
    stride, count = struct.unpack_from("<HH", blob, 46)
    if stride < 40 or offset + stride * count > len(blob):
        raise RuntimeError("Invalid ELF section table")
    result = []
    for index in range(count):
        fields = struct.unpack_from("<10I", blob, offset + stride * index)
        kind, start, size, link, entry_size = fields[1], fields[4], fields[5], fields[6], fields[9]
        if kind != 8 and start + size > len(blob):
            raise RuntimeError("Truncated ELF section")
        result.append((kind, start, size, link, entry_size))
    return result


def elf_dynamic_symbols(blob: bytes) -> dict[str, int]:
    sections = elf_sections(blob)
    symbols = {}
    for kind, start, size, link, stride in sections:
        if kind != 11:  # SHT_DYNSYM
            continue
        if stride != 16 or size % stride or link >= len(sections):
            raise RuntimeError("Invalid ELF symbol table")
        _, names_start, names_size, _, _ = sections[link]
        names = blob[names_start : names_start + names_size]
        for offset in range(start, start + size, stride):
            name_offset, _, _, _, _, section = struct.unpack_from("<IIIBBH", blob, offset)
            end = names.find(b"\x00", name_offset)
            if end < 0:
                raise RuntimeError("Invalid ELF symbol name")
            name = names[name_offset:end].decode("ascii")
            if name:
                symbols[name] = section
    return symbols


def elf_exported_u32(blob: bytes, name: str) -> int | None:
    sections = elf_sections(blob)
    section_table = int.from_bytes(blob[32:36], "little")
    section_stride = int.from_bytes(blob[46:48], "little")
    for kind, start, size, link, stride in sections:
        if kind != 11:
            continue
        _, names_start, names_size, _, _ = sections[link]
        names = blob[names_start:names_start + names_size]
        for offset in range(start, start + size, stride):
            name_offset, value, symbol_size, _, _, section = struct.unpack_from("<IIIBBH", blob, offset)
            end = names.find(b"\x00", name_offset)
            if names[name_offset:end].decode("ascii") != name:
                continue
            if not section or section >= len(sections) or symbol_size != 4:
                return None
            address = struct.unpack_from("<I", blob, section_table + section * section_stride + 12)[0]
            _, section_start, section_size, _, _ = sections[section]
            relative = value - address
            if relative < 0 or relative + 4 > section_size:
                return None
            return struct.unpack_from("<I", blob, section_start + relative)[0]
    return None


def assert_module_resources(code: bytes, resource: bytes, image_names: list[str]) -> None:
    """Reject missing resource exports before the watch attempts installation."""
    exports = elf_dynamic_symbols(resource)
    imports = elf_dynamic_symbols(code)
    for name in ["lv_i18n_lang_pack", *image_names]:
        if not exports.get(name):
            raise RuntimeError(f"Required resource missing from res.so: {name}")
        if name not in imports:
            raise RuntimeError(f"Module does not reference resource: {name}")
    for blob in (code, resource):
        for kind, start, size, _, stride in elf_sections(blob):
            if kind != 9:  # SHT_REL
                continue
            if stride != 8 or size % stride:
                raise RuntimeError("Invalid ELF relocation table")
            for offset in range(start, start + size, stride):
                relocation = struct.unpack_from("<I", blob, offset + 4)[0] & 0xFF
                if relocation not in (2, 21, 23):  # ABS32, GLOB_DAT, RELATIVE
                    raise RuntimeError(f"Relocation unsupported by LS11: {relocation}")


def validate_upload(blob: bytes, ident: int) -> None:
    """Check the actual saved package before starting a Bluetooth transfer."""
    package = parse_dial(blob)
    if package["id"] != ident:
        raise RuntimeError(f"Package ID {package['id']} differs from upload ID {ident}")
    if not package["entry_crc_ok"] or any(not item["crc_ok"] for item in package["files"]):
        raise RuntimeError("Package CRC is invalid")
    resources = {item["path"]: item["data"] for item in package["files"]}
    for path, code in resources.items():
        if not path.endswith(".so") or path.endswith("_res.so"):
            continue
        imports = elf_dynamic_symbols(code)
        # Older working modules did not initialize a resource manager.
        # For those that do, its language pack must be in the paired ELF.
        if "lv_i18n_lang_pack" not in imports:
            continue
        resource = resources.get(path[:-3] + "_res.so")
        if resource is None:
            raise RuntimeError("Resource module missing from package")
        image_names = [name for name in imports if name == "vscode_bg"
                       or name.endswith("_hand") or name.startswith("anim_")
                       or (name.startswith("g") and name[1:2].isdigit() and "c" in name)]
        assert_module_resources(code, resource, image_names)
        if "vscode_bg" in imports and elf_exported_u32(code, "haylou_editor_abi_version") != 2:
            raise RuntimeError(
                "Binary from an editor version with insufficient data buffers, "
                "associated with a watch crash. Rebuild the binary with the updated editor."
            )


def retarget_live_package(blob: bytes, ident: int) -> bytes:
    """Move an editor face to another three-digit slot without changing art."""
    package = parse_dial(blob)
    previous = package["id"]
    validate_upload(blob, previous)
    if not 1 <= ident <= 65534:
        raise ValueError("ID must be between 1 and 65534")
    old, new = f"dial_{previous}".encode("ascii"), f"dial_{ident}".encode("ascii")
    if len(old) != len(new):
        raise ValueError("Slot retargeting must preserve the ELF name length")
    files = []
    found_identity = False
    for item in package["files"]:
        data = item["data"]
        if data[:4] == b"\x7fELF":
            patched = bytearray(data)
            section_offset = int.from_bytes(data[32:36], "little")
            stride = int.from_bytes(data[46:48], "little")
            names_index = int.from_bytes(data[50:52], "little")
            names_header = struct.unpack_from("<10I", data, section_offset + names_index * stride)
            names = data[names_header[4]:names_header[4] + names_header[5]]
            for index, (_, start, size, _, _) in enumerate(elf_sections(data)):
                name_offset = struct.unpack_from("<I", data, section_offset + index * stride)[0]
                name = names[name_offset:names.find(b"\0", name_offset)]
                if name not in (b".rodata", b".dynstr"):
                    continue
                section = data[start:start + size]
                if name == b".rodata" and not item["path"].endswith("_res.so") and old + b"\0" in section:
                    found_identity = True
                # Change identity/SONAME strings only. Pixel data, relocation
                # offsets and symbol names remain byte-for-byte unchanged.
                for suffix in (b"\0", b".so\0", b"_res.so\0"):
                    section = section.replace(old + suffix, new + suffix)
                patched[start:start + size] = section
            data = bytes(patched)
        elif item["path"].endswith(".dsc"):
            data = data.replace(old, new)
        path = item["path"].replace(old.decode("ascii"), new.decode("ascii"))
        files.append((path, data))
    if not found_identity:
        raise RuntimeError("Registered module identity not found; slot change cancelled")
    result = pack_dial(files, ident, package["name"], package["version"])
    validate_upload(result, ident)
    return result


def assert_rom_images(blob: bytes, expected: int) -> None:
    """Pixels left in ordinary rodata fault the watch on the first draw."""
    if blob[:4] != b"\x7fELF" or len(blob) < 52:
        raise RuntimeError("res.so is not an ELF file")
    section_off = int.from_bytes(blob[32:36], "little")
    entry_size = int.from_bytes(blob[46:48], "little")
    count = int.from_bytes(blob[48:50], "little")
    name_index = int.from_bytes(blob[50:52], "little")
    if entry_size < 40 or section_off + count * entry_size > len(blob):
        raise RuntimeError("Resource ELF has an invalid section table")

    def header(index: int) -> bytes:
        start = section_off + index * entry_size
        return blob[start : start + entry_size]

    names_header = header(name_index)
    names_off = int.from_bytes(names_header[16:20], "little")
    names_size = int.from_bytes(names_header[20:24], "little")
    names = blob[names_off : names_off + names_size]
    rom = 0
    rodata = 0
    for index in range(count):
        item = header(index)
        name_off = int.from_bytes(item[0:4], "little")
        size = int.from_bytes(item[20:24], "little")
        end = names.find(b"\x00", name_off)
        name = names[name_off:end].decode("ascii", "replace")
        if name.startswith(".ROM3_IMG."):
            rom += 1
        elif name == ".rodata":
            rodata = size
    if rom != expected:
        raise RuntimeError(
            f"ROM3_IMG expected {expected} images but found {rom}"
        )
    if rodata > 64:
        raise RuntimeError(
            f"Pixels ended up in rodata ({rodata} bytes), which crashes the watch"
        )


def build(png: Path, layout_path: Path, ident: int, name: str, title: str, dest: Path) -> None:
    if not 1 <= ident <= 65534:
        raise ValueError("ID must be between 1 and 65534")
    raw_name = name.encode("utf-8")
    if not raw_name or len(raw_name) > 15:
        raise ValueError("Package name must contain 1–15 bytes")
    layout = json.loads(layout_path.read_text(encoding="utf-8"))
    widgets = list(layout["widgets"])
    hands = list(layout.get("hands") or [])
    animations = list(layout.get("animations") or [])
    face = f"dial_{ident}"
    from PIL import Image

    image = Image.open(png).convert("RGBA")
    if image.size != (368, 448):
        raise ValueError("The background must be 368x448")
    thumb = image.resize((260, 317), Image.Resampling.LANCZOS)
    with tempfile.TemporaryDirectory(prefix="rs4live-") as folder:
        work = Path(folder)
        image.save(work / "bg.png")
        thumb.save(work / "tn.png")
        background = png_to_ezip(work / "bg.png")
        thumbnail = png_to_ezip(work / "tn.png")
        if len(background) < 8:
            raise RuntimeError("eZip produced an incomplete background")
        header = int.from_bytes(background[:4], "little")
        images = [("vscode_bg", background[4:], header)]
        for hand in hands:
            kind = str(hand.get("kind", ""))
            if kind not in ("hour", "minute", "second"):
                raise ValueError(f"Unknown clock hand: {kind}")
            pixels, header = truecolor_alpha(Path(hand["file"]), int(hand.get("width", 0)), int(hand.get("height", 0)))
            images.append((f"{kind}_hand", pixels, header))
        glyph_images, glyph_info = prepare_glyphs(widgets, work)
        images.extend(glyph_images)
        animation_images, animation_info = prepare_animations(animations, work)
        images.extend(animation_images)
        write_layout(work / "face_layout.h", face, title or name, widgets, hands, glyph_info, animation_info)
        write_res(work / "res.c", images)
        if hands:
            print(f"Clock hands: {len(hands)}")
        if glyph_images:
            print(f"Font rasterized into {len(glyph_images)} images")
        if animation_images:
            print(f"Animations rasterized into {len(animation_images)} images")
        code, resource = compile_so(work, face)
    assert_rom_images(resource, len(images))
    assert_module_resources(code, resource, [symbol for symbol, _, _ in images])
    root = f"/ex/installer_wf/{face}"
    blob = pack_dial(
        [
            (f"{root}/{face}.dsc", dsc_for(face)),
            (f"{root}/{face}.so", code),
            (f"{root}/{face}_res.so", resource),
            (f"{root}/{face}_tn.bin", thumbnail),
        ],
        ident,
        name,
    )
    checked = parse_dial(blob)
    if checked["id"] != ident or not checked["entry_crc_ok"]:
        raise RuntimeError("The generated package failed validation")
    if any(not item["crc_ok"] for item in checked["files"]):
        raise RuntimeError("An internal file CRC failed")
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(blob)
    print(f"Written {dest} ({len(blob)} bytes), ID {ident}")
    if any(item.get("characters") for item in widgets):
        print("Numbers imported from PNG character images")
    elif any(str(item.get("font") or "").strip() for item in widgets):
        print("Font text exported as image glyphs")
    else:
        print("System font fixed at size 1")


def main() -> None:
    parser = argparse.ArgumentParser(description="Build a live RS4 Plus watchface")
    parser.add_argument("--png", type=Path, required=True)
    parser.add_argument("--layout", type=Path, required=True)
    parser.add_argument("--id", type=int, required=True)
    parser.add_argument("--name", required=True)
    parser.add_argument("--title", default="VS Code")
    parser.add_argument("-o", "--output", type=Path, required=True)
    args = parser.parse_args()
    build(args.png, args.layout, args.id, args.name, args.title, args.output)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, RuntimeError, OSError) as exc:
        print(f"Error: {exc}", file=sys.stderr)
        sys.exit(1)
