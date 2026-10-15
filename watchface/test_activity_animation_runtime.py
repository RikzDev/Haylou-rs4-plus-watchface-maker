"""Exercise configurable steps, kcal and whole-distance progress in ARM code."""
import importlib.util
import tempfile
import unittest
from pathlib import Path

from build_live import (GCC, animation_range, assert_module_resources, compile_so,
                        prepare_animations, write_layout, write_res)
from emulate_module import exercise_tick
from rs4face import pack_dial


class ActivityRangeValidationTests(unittest.TestCase):
    def test_default_ranges_and_explicit_overrides(self):
        for source, maximum in (('steps', 10000), ('calories', 1000), ('distance', 10)):
            self.assertEqual(animation_range(dict(source=source)), (0, maximum))
            self.assertEqual(animation_range(dict(source=source, min_value=20, max_value=99999)), (20, 99999))

    def test_invalid_ranges_rejected_before_image_processing(self):
        for source in ('steps', 'calories', 'distance'):
            for minimum, maximum in ((-1, 100), (10, 10), (20, 10), (0, 100000),
                                     (0, 9.5), (0, '100'), (False, 100), (0, None)):
                with self.subTest(source=source, minimum=minimum, maximum=maximum):
                    with tempfile.TemporaryDirectory() as folder, self.assertRaisesRegex(ValueError, 'range must use whole numbers'):
                        prepare_animations([dict(source=source, min_value=minimum, max_value=maximum,
                                                 frames=['missing.png'])], Path(folder))


@unittest.skipUnless(GCC.is_file() and importlib.util.find_spec('unicorn'), 'ARM GCC and optional Unicorn required')
class ActivityAnimationRuntimeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.work = tempfile.TemporaryDirectory()
        folder = Path(cls.work.name)
        cls.configurations = []
        for source, maximum in (('steps', 10000), ('calories', 1000), ('distance', 10)):
            cls.configurations += [(source, 5, 0, maximum, False), (source, 6, 2, 12, True),
                                   (source, 1, 0, maximum, False), (source, 64, 0, 99999, True)]
        animations = [dict(x=index, y=100, interval=400, source=source,
                           **(dict(min_value=minimum, max_value=maximum) if explicit else {}),
                           symbols=[f'anim_{index}_{frame}' for frame in range(count)],
                           offsets=[dict(x=frame, y=-frame) for frame in range(count)])
                      for index, (source, count, minimum, maximum, explicit) in enumerate(cls.configurations)]
        write_layout(folder / 'face_layout.h', 'dial_253', 'progress', [], [], [], animations)
        images = [(name, b'\xff\xff\xff', 5 | (1 << 10) | (1 << 21))
                  for name in ['vscode_bg', *[name for animation in animations for name in animation['symbols']]]]
        write_res(folder / 'res.c', images)
        code, resource = compile_so(folder, 'dial_253')
        assert_module_resources(code, resource, [name for name, _, _ in images])
        cls.package = folder / 'progress.bin'
        cls.package.write_bytes(pack_dial([
            ('/ex/installer_wf/dial_253/dial_253.so', code),
            ('/ex/installer_wf/dial_253/dial_253_res.so', resource)], 253, 'progress'))

    @classmethod
    def tearDownClass(cls):
        cls.work.cleanup()

    def frames(self, raw, unit=1, iterations=1):
        trace = {}
        exercise_tick(self.package, today_size=64, heart_size=8, initialize=True, iterations=iterations,
                      today_values=raw, unit_type=unit, trace=trace)
        objects = [obj for obj in trace['draw_objects'] if str(obj['source']).startswith('anim_')]
        frames = [int(obj['source'].rsplit('_', 1)[1]) for obj in objects]
        steps = min(raw.get('steps', 1234) & 0x7fffffff, 99999)
        calories = min((raw.get('calories', 23000) & 0x7fffffff) // 1000, 99999)
        distance = raw.get('distance', 0) & 0x7fffffff
        centi = distance * 6214 // 10000000 if unit == 2 else distance // 1000
        values = dict(steps=steps, calories=calories, distance=centi // 100)
        self.assertEqual(len(frames), len(self.configurations))
        for index, (source, count, minimum, maximum, _) in enumerate(self.configurations):
            value = values[source]
            expected = 0 if value <= minimum else count - 1 if value >= maximum else (value - minimum) * count // (maximum - minimum + 1)
            self.assertEqual(frames[index], expected, (source, count, value, minimum, maximum))
            self.assertEqual(objects[index]['position'], (index + expected, 100 - expected))
        return frames

    def test_boundaries_nonzero_minimum_and_no_wrap_for_any_activity(self):
        values = (0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 99, 100, 199, 200, 201,
                  399, 400, 401, 799, 800, 801, 999, 1000, 1999, 2000, 2001, 7999, 8000, 8001,
                  9999, 10000, 10001, 15625, 20000)
        observed = [self.frames(dict(steps=value, calories=value * 1000, distance=value * 100000))
                    for value in values]
        for index in range(len(self.configurations)):
            column = [frames[index] for frames in observed]
            self.assertEqual(column, sorted(column))
        # Five-frame sequences never wrap at goal+1.
        self.assertEqual(observed[values.index(10000)][0], 4)
        self.assertEqual(observed[values.index(10001)][0], 4)

    def test_distance_uses_whole_display_units_and_metadata_flags_are_ignored(self):
        for raw in (0, 999, 99999, 100000, 160934, 500000, 999999, 1000000, 123456789, 0x7fffffff):
            for unit in (1, 2):
                for flag in (0, 0x80000000):
                    self.frames(dict(steps=flag | 12345, calories=flag | 11000, distance=flag | raw), unit=unit)

    def test_large_values_clamp_without_overflow_and_remain_stable(self):
        raw = dict(steps=0xffffffff, calories=0xffffffff, distance=0xffffffff)
        for unit in (1, 2):
            self.assertEqual(self.frames(raw, unit=unit, iterations=500), self.frames(raw, unit=unit))


if __name__ == '__main__':
    unittest.main()
