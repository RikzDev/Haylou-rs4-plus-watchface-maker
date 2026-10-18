"""Exercise live weather images, independently of temperature text, in ARM."""
import importlib.util
import os
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path

from build_live import (GCC, assert_module_resources, compile_so, elf_dynamic_symbols,
                        prepare_animations, prepare_glyph_rasters, write_layout, write_res)
from emulate_module import exercise_tick
from rs4face import pack_dial


class WeatherFrameValidationTests(unittest.TestCase):
    def test_weather_requires_fixed_sixteen_condition_slots(self):
        for count in (1, 15, 17, 64):
            with self.subTest(count=count), tempfile.TemporaryDirectory() as folder:
                with self.assertRaisesRegex(ValueError, 'exactly 16 condition icons'):
                    prepare_animations([dict(source='weather', frames=['missing.png'] * count)], Path(folder))


@unittest.skipUnless(GCC.is_file() and importlib.util.find_spec('unicorn'), 'ARM GCC and optional Unicorn required')
class WeatherIconRuntimeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.work = tempfile.TemporaryDirectory()
        cls.packages = {}
        cls.code = {}
        for with_temperature in (False, True):
            folder = Path(cls.work.name) / str(with_temperature)
            folder.mkdir()
            widgets = [dict(source='temperature', text='25', font=str(Path(os.environ.get('WINDIR', r'C:\Windows')) / 'Fonts/consola.ttf'),
                            size=12, color='#FFFFFF', x=0, y=0)] if with_temperature else []
            rasters, glyphs = prepare_glyph_rasters(widgets, folder)
            animation = dict(source='weather', x=40, y=50, interval=400,
                             symbols=[f'anim_0_{index}' for index in range(16)],
                             offsets=[dict(x=index, y=-index) for index in range(16)])
            write_layout(folder / 'face_layout.h', 'dial_253', 'weather_icons', widgets, [], glyphs, [animation])
            images = [(name, b'\xff\xff\xff', 5 | (1 << 10) | (1 << 21))
                      for name in ['vscode_bg', *animation['symbols'], *[name for name, _ in rasters]]]
            write_res(folder / 'res.c', images)
            code, resource = compile_so(folder, 'dial_253')
            assert_module_resources(code, resource, [name for name, _, _ in images])
            path = folder / 'weather.bin'
            path.write_bytes(pack_dial([
                ('/ex/installer_wf/dial_253/dial_253.so', code),
                ('/ex/installer_wf/dial_253/dial_253_res.so', resource)], 253, 'weather_icons'))
            cls.packages[with_temperature] = path
            cls.code[with_temperature] = code

    @classmethod
    def tearDownClass(cls):
        cls.work.cleanup()

    @staticmethod
    def record(condition, day=15, temperature=25):
        return dict(condition=condition, temperature=temperature,
                    timestamp=int(datetime(2026, 10, day, 12, tzinfo=timezone.utc).timestamp()))

    def run_weather(self, condition=1, hour=12, with_temperature=False, **kwargs):
        trace = {}
        arguments = {'weather_records': [self.record(condition)],
                     'time_sequence': [(2026, 10, 15, hour, 0, 0, 4)],
                     'iterations': 1, **kwargs}
        exercise_tick(self.packages[with_temperature], today_size=64, heart_size=8, initialize=True,
                      trace=trace, **arguments)
        image = next(obj for obj in trace['draw_objects'] if obj['source'].startswith('anim_0_'))
        index = int(image['source'].rsplit('_', 1)[1])
        self.assertEqual(image['position'], (40 + index, 50 - index))
        return index, trace

    def test_all_condition_bytes_use_confirmed_day_night_mapping_or_fallback(self):
        for hour in (12, 22):
            for condition in range(256):
                with self.subTest(hour=hour, condition=condition):
                    expected = condition if 1 <= condition <= 12 else 0
                    if hour == 22:
                        expected = {1: 13, 2: 14, 4: 15}.get(expected, expected)
                    self.assertEqual(self.run_weather(condition, hour)[0], expected)

    def test_day_night_boundaries_use_original_hour_in_twelve_hour_mode(self):
        for hour in (0, 5, 6, 12, 18, 19, 23):
            for condition, night in ((1, 13), (2, 14), (4, 15)):
                for time_format in (1, 2):
                    self.assertEqual(self.run_weather(condition, hour, time_format=time_format)[0],
                                     condition if 6 <= hour <= 18 else night)

    def test_missing_forecast_and_allocation_failure_use_no_data(self):
        for records in ([], [self.record(1, day=14)]):
            self.assertEqual(self.run_weather(weather_records=records)[0], 0)
        index, trace = self.run_weather(malloc_failure=True)
        self.assertEqual(index, 0)
        self.assertEqual(trace['weather_calls'], 0)

    def test_current_day_is_selected_from_seven_records_with_full_write_and_stable_cache(self):
        records = [self.record(1, day=day) for day in range(9, 15)] + [self.record(8)]
        index, trace = self.run_weather(weather_records=records, weather_size=384, iterations=500)
        self.assertEqual(index, 8)
        self.assertEqual(trace['weather_calls'], 1)
        self.assertEqual(trace['malloc_sizes'], [384])
        self.assertEqual(trace['free_calls'], 1)

    def test_temperature_and_icons_share_weather_fetch_and_date_refresh(self):
        times = [(2026, 10, 15, hour, 0, 0, 4) for hour in (18, 18, 19)]
        index, trace = self.run_weather(1, with_temperature=True, weather_records=[self.record(1, temperature=-5)],
                                        unit_type=2, time_sequence=times, iterations=2, record_ticks=True)
        self.assertEqual(index, 13)
        self.assertEqual(trace['glyph_text'], '  23')
        self.assertEqual(trace['weather_calls'], 2)
        self.assertEqual(trace['malloc_sizes'], [384, 384])
        for code in self.code.values():
            imports = elf_dynamic_symbols(code)
            self.assertFalse(any(name.startswith('__aeabi_') and not section for name, section in imports.items()))


if __name__ == '__main__':
    unittest.main()
