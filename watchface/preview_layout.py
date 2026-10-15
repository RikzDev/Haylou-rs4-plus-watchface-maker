"""Render editor text with the same raster cells and placement as the package."""
import argparse
import json
import sys
from pathlib import Path

from PIL import Image
from build_live import prepare_glyph_rasters


def preview_text(item, slots):
    source = item.get("source", "text")
    text = str(item.get("text") or " ")
    if source == "text":
        return text[:slots].ljust(slots)
    if source in ("heart", "battery", "temperature") and text.startswith("--"):
        return "--"[:slots].ljust(slots)
    text = text[-slots:]
    return text.rjust(slots, "0" if source in ("hour", "minute", "second", "month", "day", "year") else " ")


def render_preview(widgets, folder):
    folder.mkdir(parents=True, exist_ok=True)
    rasters, info = prepare_glyph_rasters(widgets, folder)
    glyphs = {symbol: Image.open(path).convert("RGBA") for symbol, path in rasters}
    result = []
    for line in info["lines"]:
        index = line["widget_index"]
        image = Image.new("RGBA", (line["width"] + (line["slots"] - 1) * line["pitch"], line["height"]))
        text = preview_text(widgets[index], line["slots"])
        for slot, char in enumerate(text):
            glyph = glyphs.get(f"g{line['set']}c{ord(char)}", glyphs[f"g{line['set']}c32"])
            image.alpha_composite(glyph, (slot * line["pitch"], 0))
        filename = f"widget_{index}.png"
        image.save(folder / filename)
        result.append({"index": index, "file": filename, "width": image.width, "height": image.height,
                       "x": line["x"], "y": line["y"], "pitch": line["pitch"], "text": text})
    (folder / "manifest.json").write_text(json.dumps(result), encoding="utf-8")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--layout", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        render_preview(json.loads(args.layout.read_text(encoding="utf-8"))["widgets"], args.output)
    except (ValueError, RuntimeError, OSError) as exc:
        print(f"Error: {exc}", file=sys.stderr)
        sys.exit(1)
