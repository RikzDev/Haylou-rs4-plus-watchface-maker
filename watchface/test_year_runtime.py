"""Check live four-digit years with TTF glyphs, PNG digits and system labels."""
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
class YearRuntimeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.work = tempfile.TemporaryDirectory()
        folder = Path(cls.work.name)
        characters = {}
        for digit in '0123456789':
            file = folder / f'{digit}.png'
            Image.new('RGBA', (8, 12), (255, 255, 255, 255)).save(file)
            characters[digit] = str(file)
        widget = dict(source='year', text='2026', x=10, y=10, color='#FFFFFF', size=24)
        font = str(Path(os.environ.get('WINDIR', r'C:\Windows')) / 'Fonts/consola.ttf')
        widgets = [{**widget, 'font': font}, {**widget, 'characters': characters, 'y': 50},
                   {**widget, 'y': 90}]
        rasters, glyphs = prepare_glyph_rasters(widgets, folder)
        write_layout(folder / 'face_layout.h', 'dial_253', 'year', widgets, [], glyphs)
        images = [(name, b'\xff\xff\xff', 5 | (1 << 10) | (1 << 21))
                  for name in ['vscode_bg', *[name for name, _ in rasters]]]
        write_res(folder / 'res.c', images)
        code, resource = compile_so(folder, 'dial_253')
        assert_module_resources(code, resource, [name for name, _, _ in images])
        cls.package = folder / 'year.bin'
        cls.package.write_bytes(pack_dial([
            ('/ex/installer_wf/dial_253/dial_253.so', code),
            ('/ex/installer_wf/dial_253/dial_253_res.so', resource)], 253, 'year'))

    @classmethod
    def tearDownClass(cls):
        cls.work.cleanup()

    def test_year_changes_at_new_year_in_all_renderers(self):
        last_day = (2026, 12, 31, 23, 59, 59, 4)
        times = [last_day, last_day, (2027, 1, 1, 0, 0, 0, 5), (2040, 2, 29, 12, 0, 0, 3)]
        for mode in (1, 2):
            with self.subTest(time_format=mode):
                trace = {}
                exercise_tick(self.package, today_size=64, heart_size=8, initialize=True, iterations=3,
                              time_format=mode, time_sequence=times, record_ticks=True, trace=trace)
                self.assertEqual([tick['glyph_text'] for tick in trace['ticks']],
                                 ['20262026', '20272027', '20402040'])
                self.assertEqual([tick['labels'] for tick in trace['ticks']], [['2026'], ['2027'], ['2040']])

    def test_preview_matches_full_year(self):
        for year in ('2000', '2026', '2100'):
            self.assertEqual(preview_text(dict(source='year', text=year), 4), year)


if __name__ == '__main__':
    unittest.main()
