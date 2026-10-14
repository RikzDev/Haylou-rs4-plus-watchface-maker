"""Compile and emulate live temperature against explicit firmware mocks."""
import importlib.util
import os
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path

from build_live import GCC, compile_so, prepare_glyph_rasters, write_layout, write_res, assert_module_resources
from rs4face import pack_dial
from emulate_module import exercise_tick
from preview_layout import preview_text


@unittest.skipUnless(GCC.is_file() and importlib.util.find_spec("unicorn"), "ARM GCC and optional Unicorn required")
class WeatherRuntimeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.work = tempfile.TemporaryDirectory()
        folder = Path(cls.work.name)
        font = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Fonts" / "consola.ttf"
        widget = {"source": "temperature", "text": "25", "font": str(font), "color": "#FFFFFF",
                  "size": 24, "x": 33, "y": 55}
        rasters, glyphs = prepare_glyph_rasters([widget], folder)
        write_layout(folder / "face_layout.h", "dial_253", "weather", [widget], [], glyphs)
        images = [(name, b"\xff\xff\xff", 5 | (1 << 10) | (1 << 21)) for name in ["vscode_bg", *[name for name, _ in rasters]]]
        write_res(folder / "res.c", images)
        code, resource = compile_so(folder, "dial_253")
        assert_module_resources(code, resource, [name for name, _, _ in images])
        cls.code = code
        cls.package = folder / "weather.bin"
        cls.package.write_bytes(pack_dial([
            ("/ex/installer_wf/dial_253/dial_253.so", code),
            ("/ex/installer_wf/dial_253/dial_253_res.so", resource)], 253, "weather"))

    @classmethod
    def tearDownClass(cls):
        cls.work.cleanup()

    def run_weather(self, records=None, **kwargs):
        trace = {}
        exercise_tick(self.package, today_size=64, heart_size=8, initialize=True, iterations=5,
                      weather_records=records, trace=trace, **kwargs)
        return trace

    @staticmethod
    def record(day, temperature):
        return {"timestamp": int(datetime(2026, 10, day, tzinfo=timezone.utc).timestamp()), "temperature": temperature}

    def test_imports_and_date_selection_celsius(self):
        for symbol in (b"apph_cfg_weather_info_get", b"get_local_time_from_utc0timestamps", b"apph_cfg_unit_type_get"):
            self.assertIn(symbol, self.code)
        trace = self.run_weather([self.record(13, 99), self.record(14, 25), self.record(15, 10)])
        self.assertEqual(trace["glyph_text"], "  25")
        self.assertEqual(trace["weather_calls"], 1)
        self.assertEqual(trace["malloc_sizes"], [384])
        self.assertEqual(trace["free_calls"], 1)

    def test_negative_and_fahrenheit_conversion(self):
        for temp, unit, expected in ((-5, 1, "  -5"), (25, 2, "  77"), (-19, 2, "  -2"), (0, 1, "   0")):
            with self.subTest(temp=temp, unit=unit):
                trace = self.run_weather([self.record(14, temp)], unit_type=unit)
                self.assertEqual(trace["glyph_text"], expected)
                self.assertEqual(preview_text({"source": "temperature", "text": expected.strip()}, 4), expected)

    def test_missing_date_and_failed_allocation(self):
        self.assertEqual(self.run_weather([self.record(13, 25)])["glyph_text"], "--  ")
        self.assertEqual(self.run_weather()["glyph_text"], "--  ")
        trace = self.run_weather([self.record(14, 25)], malloc_failure=True)
        self.assertEqual(trace["glyph_text"], "--  ")
        self.assertEqual(trace["weather_calls"], 0)

    def test_refresh_on_hour_or_date_change_and_full_buffer_write(self):
        times = [(2026, 10, 14, 12, 0, 0, 3), (2026, 10, 14, 13, 0, 0, 3), (2026, 10, 15, 13, 0, 0, 4)]
        trace = self.run_weather([self.record(14, 25), self.record(15, -10)], time_sequence=times, weather_size=384)
        self.assertEqual(trace["weather_calls"], 3)
        self.assertEqual(trace["free_calls"], 3)
        self.assertEqual(trace["glyph_text"], " -10")


if __name__ == "__main__":
    unittest.main()
