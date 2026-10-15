"""Exercise decoded activity fields and AM/PM in actual generated ARM code."""
import importlib.util
import os
import tempfile
import unittest
from pathlib import Path

from build_live import (GCC, assert_module_resources, compile_so, elf_dynamic_symbols,
                        prepare_animations, prepare_glyph_rasters, write_layout, write_res)
from emulate_module import exercise_tick
from preview_layout import preview_text
from rs4face import pack_dial


@unittest.skipUnless(GCC.is_file() and importlib.util.find_spec('unicorn'), 'ARM GCC and optional Unicorn required')
class ActivityRuntimeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.work = tempfile.TemporaryDirectory()
        folder = Path(cls.work.name)
        font = str(Path(os.environ.get('WINDIR', r'C:\Windows')) / 'Fonts/consola.ttf')
        cls.sources = ['calories', 'distance', 'distance_unit', 'ampm', 'hour', 'steps']
        widgets = [dict(source=source, x=0, y=index * 20, color='#FFFFFF', font=font, size=12)
                   for index, source in enumerate(cls.sources)]
        # The same sources also exercise the system-label path.
        widgets += [{**widget, 'font': '', 'x': 100} for widget in widgets]
        rasters, glyphs = prepare_glyph_rasters(widgets, folder)
        animations = [dict(x=0, y=200, interval=400, source='ampm', symbols=['anim_0_0', 'anim_0_1'],
                           offsets=[dict(x=0, y=0), dict(x=3, y=5)])]
        for index, source in enumerate(('calories', 'distance'), start=1):
            animations.append(dict(x=0, y=220 + index * 20, interval=400, source=source,
                                   symbols=[f'anim_{index}_{frame}' for frame in range(3)],
                                   offsets=[dict(x=0, y=0)] * 3))
        write_layout(folder / 'face_layout.h', 'dial_253', 'activity', widgets, [], glyphs, animations)
        images = [(name, b'\xff\xff\xff', 5 | (1 << 10) | (1 << 21))
                  for name in ['vscode_bg', *[name for animation in animations for name in animation['symbols']],
                               *[name for name, _ in rasters]]]
        write_res(folder / 'res.c', images)
        cls.code, resource = compile_so(folder, 'dial_253')
        assert_module_resources(cls.code, resource, [name for name, _, _ in images])
        cls.package = folder / 'activity.bin'
        cls.package.write_bytes(pack_dial([
            ('/ex/installer_wf/dial_253/dial_253.so', cls.code),
            ('/ex/installer_wf/dial_253/dial_253_res.so', resource)], 253, 'activity'))

    @classmethod
    def tearDownClass(cls):
        cls.work.cleanup()

    def run_activity(self, **kwargs):
        trace = {}
        exercise_tick(self.package, today_size=64, heart_size=8, initialize=True, trace=trace,
                      **{'iterations': 3, 'time_format': 2, **kwargs})
        return trace

    def test_metric_fields_flags_and_system_labels(self):
        trace = self.run_activity(today_values={'calories': 0x80000000 | 300999,
                                                'distance': 0x80000000 | 750000, 'steps': 12345})
        self.assertEqual(trace['label_texts'], ['300', '7.50', 'km', 'PM', '12', '12345'])
        self.assertEqual(trace['glyph_text'], '  300    7.50kmPM1212345')

    def test_imperial_conversion_and_large_values_without_overflow(self):
        for raw in (0, 1000, 100000, 750000, 12345678, 0x7fffffff):
            with self.subTest(raw=raw):
                trace = self.run_activity(unit_type=2, today_values={'distance': raw})
                value = (raw * 6214) // 10000000
                self.assertEqual(trace['label_texts'][1:3], [f'{value // 100}.{value % 100:02}', 'mi'])
        imports = elf_dynamic_symbols(self.code)
        self.assertFalse(any(name.startswith('__aeabi_') and not section for name, section in imports.items()))

    def test_limits_and_full_firmware_output_buffer(self):
        trace = self.run_activity(today_values={'calories': 0x7fffffff, 'distance': 0x7fffffff,
                                                'steps': 0xffffffff}, iterations=500)
        self.assertEqual(trace['label_texts'][:3], ['99999', '21474.83', 'km'])
        self.assertEqual(trace['label_texts'][-1], '99999')

    def test_midnight_noon_and_pm_use_unmodified_hour(self):
        hours = [0, 0, 11, 12, 23, 0]
        times = [(2026, 10, 15, hour, 0, 0, 4) for hour in hours]
        trace = self.run_activity(time_sequence=times, iterations=5, record_ticks=True)
        self.assertEqual([tick['labels'][3:5] for tick in trace['ticks']],
                         [['AM', '12'], ['AM', '11'], ['PM', '12'], ['PM', '11'], ['AM', '12']])
        self.assertTrue(all(tick['hidden'] == [False] for tick in trace['ticks']))
        # Noon selects the PM frame and its independent offset.
        noon = self.run_activity(time_sequence=[times[3]])
        image = next(obj for obj in noon['draw_objects'] if obj['source'] == 'anim_0_1')
        self.assertEqual(image['position'], (3, 205))

    def test_switching_24h_and_12h_hides_and_restores_ampm(self):
        times = [(2026, 10, 15, 23, 0, 0, 4)]
        trace = self.run_activity(time_sequence=times, time_format_sequence=[1, 1, 2, 1, 2],
                                  iterations=4, record_ticks=True)
        self.assertEqual([tick['labels'][3:5] for tick in trace['ticks']],
                         [['', '23'], ['PM', '11'], ['', '23'], ['PM', '11']])
        self.assertEqual([tick['hidden'] for tick in trace['ticks']], [[True], [False], [True], [False]])
        self.assertEqual([tick['glyph_text'][15:17] for tick in trace['ticks']], ['  ', 'PM', '  ', 'PM'])

    def test_ampm_requires_two_frames(self):
        with tempfile.TemporaryDirectory() as folder, self.assertRaisesRegex(ValueError, 'exactly two frames'):
            prepare_animations([dict(source='ampm', frames=['dummy.png'])], Path(folder))

    def test_activity_frame_selectors_use_calories_and_display_unit(self):
        for unit, frame in ((1, 'anim_2_2'), (2, 'anim_2_0')):
            trace = self.run_activity(unit_type=unit, today_values={'calories': 11000, 'distance': 500000})
            sources = [obj['source'] for obj in trace['draw_objects']]
            self.assertIn('anim_1_2', sources)
            self.assertIn(frame, sources)

    def test_preview_samples_match_padding(self):
        self.assertEqual(preview_text(dict(source='distance', text='7.50'), 8), '    7.50')
        self.assertEqual(preview_text(dict(source='ampm', text='AM'), 2), 'AM')
        self.assertEqual(preview_text(dict(source='ampm', text=''), 2), '  ')


if __name__ == '__main__':
    unittest.main()
