"""Check heart progress frame selection in the generated ARM module."""
import importlib.util
import tempfile
import unittest
from pathlib import Path

from build_live import (GCC, animation_range, assert_module_resources, compile_so,
                        prepare_animations, write_layout, write_res)
from emulate_module import exercise_tick
from rs4face import pack_dial


class HeartRangeValidationTests(unittest.TestCase):
    def test_legacy_defaults_and_configured_range(self):
        self.assertEqual(animation_range({'source': 'heart'}), (0, 200))
        self.assertEqual(animation_range(dict(source='heart', min_value=60, max_value=100)), (60, 100))
        self.assertEqual(animation_range(dict(source='loop', min_value=-1)), (0, 200))

    def test_invalid_ranges_are_rejected_before_loading_images(self):
        for minimum, maximum in ((-1, 200), (50, 50), (100, 60), (0, 256),
                                 (True, 200), (0, 100.5), ('0', 200), (None, 200)):
            with self.subTest(minimum=minimum, maximum=maximum):
                with tempfile.TemporaryDirectory() as folder, self.assertRaisesRegex(ValueError, 'Heart rate range'):
                    prepare_animations([dict(source='heart', min_value=minimum, max_value=maximum,
                                             frames=['missing.png'])], Path(folder))


@unittest.skipUnless(GCC.is_file() and importlib.util.find_spec('unicorn'), 'ARM GCC and optional Unicorn required')
class HeartAnimationRuntimeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.work = tempfile.TemporaryDirectory()
        folder = Path(cls.work.name)
        cls.configurations = [(5, 0, 200), (6, 0, 200), (5, 60, 100), (1, 0, 200), (64, 0, 255)]
        animations = [dict(x=index * 10, y=200, interval=400, source='heart',
                           **({} if index < 2 else dict(min_value=minimum, max_value=maximum)),
                           symbols=[f'anim_{index}_{frame}' for frame in range(count)],
                           offsets=[dict(x=frame, y=frame * 2) for frame in range(count)])
                      for index, (count, minimum, maximum) in enumerate(cls.configurations)]
        write_layout(folder / 'face_layout.h', 'dial_253', 'heart_range', [], [], [], animations)
        images = [(name, b'\xff\xff\xff', 5 | (1 << 10) | (1 << 21))
                  for name in ['vscode_bg', *[name for animation in animations for name in animation['symbols']]]]
        write_res(folder / 'res.c', images)
        code, resource = compile_so(folder, 'dial_253')
        assert_module_resources(code, resource, [name for name, _, _ in images])
        cls.package = folder / 'heart.bin'
        cls.package.write_bytes(pack_dial([
            ('/ex/installer_wf/dial_253/dial_253.so', code),
            ('/ex/installer_wf/dial_253/dial_253_res.so', resource)], 253, 'heart_range'))

    @classmethod
    def tearDownClass(cls):
        cls.work.cleanup()

    def frames(self, bpm, flags=1, iterations=1):
        trace = {}
        exercise_tick(self.package, today_size=64, heart_size=8, initialize=True,
                      heart_bpm=bpm, heart_flags=flags, iterations=iterations, trace=trace)
        images = [obj for obj in trace['draw_objects'] if str(obj['source']).startswith('anim_')]
        frames = [int(obj['source'].rsplit('_', 1)[1]) for obj in images]
        self.assertEqual(len(frames), len(self.configurations))
        self.assertEqual([obj['position'] for obj in images],
                         [(index * 10 + frame, 200 + frame * 2) for index, frame in enumerate(frames)])
        return frames

    def test_every_bpm_is_monotonic_and_clamps_without_wrapping(self):
        observed = [self.frames(bpm) for bpm in range(256)]
        for index, (count, minimum, maximum) in enumerate(self.configurations):
            with self.subTest(count=count, minimum=minimum, maximum=maximum):
                column = [frames[index] for frames in observed]
                self.assertEqual(column, sorted(column))
                self.assertEqual(set(column), set(range(count)))
                self.assertEqual(column[minimum], 0)
                self.assertEqual(column[maximum], count - 1)
        self.assertEqual(observed[74][:2], observed[75][:2])
        # Five default frames divide the inclusive 0..200 range into buckets.
        self.assertEqual([observed[bpm][0] for bpm in (0, 40, 41, 80, 81, 120, 121, 160, 161, 200, 255)],
                         [0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 4])

    def test_missing_reading_uses_first_frame_and_valid_reading_stays_stable(self):
        self.assertEqual(self.frames(180, flags=0), [0] * 5)
        self.assertEqual(self.frames(0), [0] * 5)
        self.assertEqual(self.frames(75, iterations=500), self.frames(75))


if __name__ == '__main__':
    unittest.main()
