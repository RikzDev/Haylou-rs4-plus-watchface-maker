"""Verify mixed layers in both generated order and initialized ARM objects."""
import importlib.util
import tempfile
import unittest
from pathlib import Path

from build_live import GCC, compile_so, draw_order, write_layout, write_res
from emulate_module import exercise_tick
from rs4face import pack_dial


class LayerOrderTests(unittest.TestCase):
    def test_defaults_and_equal_values_follow_editor_order(self):
        widgets = [{"source": "text"}, {"font": "font.ttf"}, {"source": "text"}]
        glyphs = {"lines": [{"widget_index": 1}]}
        self.assertEqual(draw_order(widgets, [{"kind": "minute"}, {"kind": "hour"}], glyphs, [{}, {}]),
                         [(5, 0), (5, 1), (2, 0), (1, 0), (6, 0), (4, 0), (6, 1)])
        for item in widgets:
            item['z_index'] = 0
        self.assertEqual(draw_order(widgets, [{"kind": "hour", "z_index": 0}], glyphs, [{"z_index": 0}]),
                         [(5, 0), (1, 0), (6, 0), (4, 0), (6, 1)])

    def test_signed_layers_and_invalid_values(self):
        self.assertEqual(draw_order([{"z_index": -(2**31)}], [], {}, [{"z_index": 2**31 - 1}]), [(6, 0), (5, 0)])
        for value in (True, 1.5, "10", 2**31, -(2**31) - 1):
            with self.subTest(value=value), self.assertRaisesRegex(ValueError, 'Z-index'):
                draw_order([{'z_index': value}], [], {}, [])


@unittest.skipUnless(GCC.is_file() and importlib.util.find_spec('unicorn'), 'ARM GCC and optional Unicorn required')
class LayerRuntimeTests(unittest.TestCase):
    def test_background_only_and_animation_only(self):
        for animated in (False, True):
            with self.subTest(animated=animated), tempfile.TemporaryDirectory() as folder:
                work = Path(folder)
                animations = [dict(x=20, y=40, interval=400, source='loop',
                                   symbols=['anim_0_0', 'anim_0_1'],
                                   offsets=[dict(x=0, y=0), dict(x=3, y=5)])] if animated else []
                write_layout(work / 'face_layout.h', 'dial_253', 'blank', [], [], animations=animations)
                symbols = ['vscode_bg'] + (['anim_0_0', 'anim_0_1'] if animated else [])
                write_res(work / 'res.c', [(name, b'\xff\xff\xff', 5 | (1 << 10) | (1 << 21))
                                           for name in symbols])
                code, resource = compile_so(work, 'dial_253')
                package = work / 'blank.bin'
                package.write_bytes(pack_dial([
                    ('/ex/installer_wf/dial_253/dial_253.so', code),
                    ('/ex/installer_wf/dial_253/dial_253_res.so', resource)], 253, 'blank'))
                trace = {}
                exercise_tick(package, today_size=64, heart_size=8, initialize=True, iterations=20, trace=trace)
                self.assertEqual([obj['type'] for obj in trace['draw_objects']],
                                 ['image', 'image'] if animated else ['image'])
                self.assertEqual(trace['draw_objects'][0]['source'], 'vscode_bg')
                if animated:
                    self.assertEqual(trace['draw_objects'][1]['position'], (20, 40))

    def test_native_creation_order_and_glyph_grouping(self):
        with tempfile.TemporaryDirectory() as folder:
            work = Path(folder)
            # Text 0 is a firmware label; text 1 has two raster glyph slots.
            widgets = [dict(x=30, y=30, color='#FFFFFF', source='text', text='test', z_index=30),
                       dict(x=20, y=20, font='dummy.ttf', source='hour', z_index=-10)]
            glyphs = dict(lines=[dict(widget_index=1, x=20, y=20, pitch=5, slots=2,
                                     source=1, set=0, slot=0, text='')], slots=2,
                          externs='extern img_dsc_t g0c32;', lookup='return &g0c32;')
            hands = [dict(kind='hour', posx=40, posy=40, pivotx=0, pivoty=0, z_index=5)]
            animations = [dict(x=10, y=10, interval=400, source='static', symbols=['anim_0_0'],
                               offsets=[dict(x=0, y=0)], z_index=50)]
            write_layout(work / 'face_layout.h', 'dial_253', 'layers', widgets, hands, glyphs, animations)
            images = [(name, b'\xff\xff\xff', 5 | (1 << 10) | (1 << 21))
                      for name in ['vscode_bg', 'g0c32', 'hour_hand', 'anim_0_0']]
            write_res(work / 'res.c', images)
            code, resource = compile_so(work, 'dial_253')
            package = work / 'layers.bin'
            package.write_bytes(pack_dial([
                ('/ex/installer_wf/dial_253/dial_253.so', code),
                ('/ex/installer_wf/dial_253/dial_253_res.so', resource)], 253, 'layers'))
            trace = {}
            exercise_tick(package, today_size=64, heart_size=8, initialize=True, iterations=20, trace=trace)
            self.assertEqual([obj['position'] for obj in trace['draw_objects']],
                             [(0, 0), (20, 20), (25, 20), (40, 40), (30, 30), (10, 10)])
            self.assertEqual([obj['type'] for obj in trace['draw_objects']],
                             ['image', 'image', 'image', 'image', 'label', 'image'])
            self.assertEqual(trace['draw_objects'][0]['source'], 'vscode_bg')
            self.assertEqual(trace['draw_objects'][-1]['source'], 'anim_0_0')


if __name__ == '__main__':
    unittest.main()
