"""Preview must use the actual exported glyph cells and runtime padding."""
import os
import tempfile
import unittest
from pathlib import Path

from PIL import Image
from build_live import prepare_glyphs
from preview_layout import render_preview, preview_text


class PreviewLayoutTests(unittest.TestCase):
    def test_runtime_padding_and_missing_health_values(self):
        self.assertEqual(preview_text({"source": "battery", "text": "15"}, 3), " 15")
        self.assertEqual(preview_text({"source": "heart", "text": "--"}, 3), "-- ")
        self.assertEqual(preview_text({"source": "steps", "text": "1234"}, 5), " 1234")
        self.assertEqual(preview_text({"source": "hour", "text": "4"}, 2), "04")
        self.assertEqual(preview_text({"source": "text", "text": "Oi"}, 2), "Oi")

    def test_font_preview_uses_exported_pixels_and_positions(self):
        font = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Fonts" / "consola.ttf"
        if not font.exists():
            self.skipTest("Consolas Windows fixture unavailable")
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp)
            widgets = [
                {"source": "battery", "text": "15", "font": str(font), "size": 29,
                 "color": "#FF8800", "x": 51, "y": 87},
                {"source": "text", "text": "Wi", "font": str(font), "size": 29,
                 "color": "#FF8800", "x": 119, "y": 240},
            ]
            preview = render_preview(widgets, folder / "preview")
            (folder / "export").mkdir()
            _, exported = prepare_glyphs(widgets, folder / "export")
            for widget, record, line in zip(widgets, preview, exported["lines"]):
                self.assertEqual((record["x"], record["y"]), (widget["x"], widget["y"]))
                self.assertEqual(record["pitch"], line["pitch"])
                image = Image.open(folder / "preview" / record["file"]).convert("RGBA")
                expected = Image.new("RGBA", image.size)
                for slot, ch in enumerate(record["text"]):
                    glyph = Image.open(folder / "export" / f"g{line['set']}_{ord(ch)}.png").convert("RGBA")
                    expected.alpha_composite(glyph, (slot * line["pitch"], 0))
                self.assertEqual(image.tobytes(), expected.tobytes())


if __name__ == "__main__":
    unittest.main()
