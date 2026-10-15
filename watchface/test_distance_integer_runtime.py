"""Whole distance uses digits only, including PNG fonts without punctuation."""
import importlib.util
import os
import tempfile
import unittest
from pathlib import Path

from PIL import Image
from build_live import GCC, assert_module_resources, compile_so, prepare_glyph_rasters, write_layout, write_res
from emulate_module import exercise_tick
from preview_layout import preview_text
from rs4face import pack_dial


@unittest.skipUnless(GCC.is_file() and importlib.util.find_spec('unicorn'), 'ARM GCC and optional Unicorn required')
class IntegerDistanceRuntimeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.work = tempfile.TemporaryDirectory()
        folder = Path(cls.work.name)
        digits = {}
        for digit in '0123456789':
            file = folder / f'{digit}.png'
            Image.new('RGBA', (8, 12), (255, 255, 255, 255)).save(file)
            digits[digit] = str(file)
        base = dict(source='distance_int', text='7', x=10, y=10, color='#FFFFFF', size=24)
        font = str(Path(os.environ.get('WINDIR', r'C:\Windows')) / 'Fonts/consola.ttf')
        widgets = [{**base, 'font': font}, {**base, 'characters': digits, 'y': 50}, {**base, 'y': 90}]
        rasters, glyphs = prepare_glyph_rasters(widgets, folder)
        write_layout(folder / 'face_layout.h', 'dial_253', 'integer', widgets, [], glyphs)
        images = [(name, b'\xff\xff\xff', 5 | (1 << 10) | (1 << 21))
                  for name in ['vscode_bg', *[name for name, _ in rasters]]]
        write_res(folder / 'res.c', images)
        code, resource = compile_so(folder, 'dial_253')
        assert_module_resources(code, resource, [name for name, _, _ in images])
        cls.package = folder / 'integer.bin'
        cls.package.write_bytes(pack_dial([
            ('/ex/installer_wf/dial_253/dial_253.so', code),
            ('/ex/installer_wf/dial_253/dial_253_res.so', resource)], 253, 'integer'))

    @classmethod
    def tearDownClass(cls):
        cls.work.cleanup()

    def test_truncation_units_zero_marker_bits_and_large_values(self):
        for raw in (0, 75000, 99999, 100000, 750000, 799999, 0x7fffffff):
            for unit in (1, 2):
                with self.subTest(raw=raw, unit=unit):
                    trace = {}
                    exercise_tick(self.package, today_size=64, heart_size=8, initialize=True, trace=trace,
                                  unit_type=unit, today_values={'distance': raw | 0x80000000})
                    value = raw // 100000 if unit == 1 else (raw * 6214) // 1000000000
                    self.assertEqual(trace['glyph_text'], str(value).rjust(5) * 2)
                    self.assertEqual(trace['label_texts'], [str(value)])
                    self.assertNotIn('.', trace['glyph_text'])

    def test_preview_uses_digits_only(self):
        self.assertEqual(preview_text(dict(source='distance_int', text='7'), 5), '    7')


if __name__ == '__main__':
    unittest.main()
