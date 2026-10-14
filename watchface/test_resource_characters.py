"""PNG character resources must preserve artwork and remain live widgets."""

import tempfile
import unittest
from pathlib import Path

from PIL import Image

from build_live import prepare_glyphs, write_layout, truecolor_alpha


class CharacterImageTests(unittest.TestCase):
    def fixture(self, folder):
        paths = {}
        for digit in "9876543210/":
            image = Image.new("RGBA", (8, 8), (230, 20, 40, 255))
            image.putpixel((1, 1), (20, 40, 230, 160))
            path = folder / f"c{ord(digit)}.png"
            image.save(path)
            paths[digit] = str(path)
        return {"key": "hour", "source": "hour", "size": 8, "color": "#00FF00",
                "x": 10, "y": 20, "characters": paths, "text": "12", "spacing": 2}

    def test_digit_mapping_artwork_and_spacing(self):
        with tempfile.TemporaryDirectory() as folder:
            work = Path(folder)
            item = self.fixture(work)
            images, info = prepare_glyphs([item], work)
            self.assertEqual(info["lines"][0]["source"], 1)
            self.assertEqual(info["lines"][0]["slots"], 2)
            self.assertEqual(info["lines"][0]["pitch"], 10)
            self.assertIn("if (ch == 48) return &g0c48;", info["lookup"])
            output = Image.open(work / "g0_48.png").convert("RGBA")
            self.assertEqual(output.getpixel((0, 0)), (230, 20, 40, 255))
            self.assertEqual(output.getpixel((1, 1)), (20, 40, 230, 160))
            self.assertEqual(Image.open(work / "g0_32.png").getbbox(), None)
            write_layout(work / "layout.h", "dial_253", "test", [item], [], info)
            self.assertIn("#define LABEL_COUNT 0", (work / "layout.h").read_text())
            self.assertEqual(len(images), 11)

    def test_separator_uses_named_character_image(self):
        with tempfile.TemporaryDirectory() as folder:
            work = Path(folder)
            item = {**self.fixture(work), "source": "text", "text": "/"}
            _, info = prepare_glyphs([item], work)
            self.assertEqual(info["lines"][0]["slots"], 1)
            self.assertIn("if (ch == 47) return &g0c47;", info["lookup"])

    def test_missing_digit_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            work = Path(folder)
            item = self.fixture(work)
            del item["characters"]["9"]
            with self.assertRaisesRegex(ValueError, "imagens ausentes.*9"):
                prepare_glyphs([item], work)

    def test_resized_hand_exports_real_dimensions(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "hand.png"
            Image.new("RGBA", (10, 40), (255, 0, 0, 255)).save(path)
            pixels, header = truecolor_alpha(path, 20, 80)
            self.assertEqual(header & 31, 5)
            self.assertEqual((header >> 10) & 2047, 20)
            self.assertEqual((header >> 21) & 2047, 80)
            self.assertEqual(len(pixels), 20 * 80 * 3)


if __name__ == "__main__":
    unittest.main()
