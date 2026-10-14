"""Check resource linking with real ARM modules before packaging a face."""

import tempfile
import unittest
from pathlib import Path

from build_live import (
    GCC,
    assert_module_resources,
    assert_rom_images,
    compile_so,
    write_layout,
    write_res,
    validate_upload,
    retarget_live_package,
)
from rs4face import pack_dial, parse_dial
from upload_slots import resolve_collision, CUSTOM_SLOTS


@unittest.skipUnless(GCC.is_file(), "ARM GCC is required")
class ResourceLinkTests(unittest.TestCase):
    @staticmethod
    def compile_face(work, missing_language=False):
        write_layout(
            work / "face_layout.h", "dial_253", "test",
            [{"x": 0, "y": 0, "color": "#FFFFFF", "source": "hour"}], [],
        )
        # One uncompressed RGB565 + alpha pixel in a ROM3 section.
        write_res(work / "res.c", [("vscode_bg", b"\xff\xff\xff", 5 | (1 << 10) | (1 << 21))])
        if missing_language:
            source = work / "res.c"
            source.write_text("\n".join(
                line for line in source.read_text().splitlines()
                if "lv_i18n_lang_pack[]" not in line
            ))
        return compile_so(work, "dial_253")

    def test_resource_manager_language_pack_is_linked(self):
        with tempfile.TemporaryDirectory() as folder:
            code, resource = self.compile_face(Path(folder))
            assert_rom_images(resource, 1)
            assert_module_resources(code, resource, ["vscode_bg"])

    def test_missing_language_pack_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            code, resource = self.compile_face(Path(folder), missing_language=True)
            with self.assertRaisesRegex(RuntimeError, "lv_i18n_lang_pack"):
                assert_module_resources(code, resource, ["vscode_bg"])
            package = pack_dial([
                ("/ex/installer_wf/dial_253/dial_253.so", code),
                ("/ex/installer_wf/dial_253/dial_253_res.so", resource),
            ], 253, "test")
            with self.assertRaisesRegex(RuntimeError, "lv_i18n_lang_pack"):
                validate_upload(package, 253)

    def test_missing_image_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            code, resource = self.compile_face(Path(folder))
            with self.assertRaisesRegex(RuntimeError, "missing_frame"):
                assert_module_resources(code, resource, ["vscode_bg", "missing_frame"])

    def test_upload_rejects_editor_without_buffer_fix(self):
        with tempfile.TemporaryDirectory() as folder:
            code, resource = self.compile_face(Path(folder))
            files = [
                ("/ex/installer_wf/dial_253/dial_253.so", code),
                ("/ex/installer_wf/dial_253/dial_253_res.so", resource),
            ]
            validate_upload(pack_dial(files, 253, "test"), 253)
            # Simulate a pre-fix module without altering relocation offsets.
            old_code = code.replace(b"haylou_editor_abi_version", b"haylou_editor_old_version")
            self.assertNotEqual(old_code, code)
            with self.assertRaisesRegex(RuntimeError, "buffers de dados insuficientes"):
                validate_upload(pack_dial([(files[0][0], old_code), files[1]], 253, "test"), 253)


@unittest.skipUnless(GCC.is_file(), "ARM GCC is required")
class SlotTests(unittest.IsolatedAsyncioTestCase):
    @classmethod
    def setUpClass(cls):
        with tempfile.TemporaryDirectory() as folder:
            code, resource = ResourceLinkTests.compile_face(Path(folder))
            cls.package = pack_dial([
                ("/ex/installer_wf/dial_253/dial_253.dsc", b"dial_253\0dial_253.so\0dial_253_res.so\0"),
                ("/ex/installer_wf/dial_253/dial_253.so", code),
                ("/ex/installer_wf/dial_253/dial_253_res.so", resource),
                ("/ex/installer_wf/dial_253/dial_253_tn.bin", b"thumbnail pixels"),
            ], 253, "test")

    def test_retarget_preserves_resources_and_registration(self):
        moved = retarget_live_package(self.package, 251)
        package = parse_dial(moved)
        self.assertEqual(package["id"], 251)
        self.assertTrue(package["entry_crc_ok"])
        self.assertTrue(all(item["crc_ok"] for item in package["files"]))
        self.assertTrue(all("dial_251/" in item["path"] for item in package["files"]))
        code = package["files"][1]["data"]
        self.assertIn(b"dial_251\0", code)
        self.assertNotIn(b"dial_253\0", code)
        self.assertEqual(package["files"][3]["data"], b"thumbnail pixels")
        self.assertEqual(retarget_live_package(moved, 253), self.package)

    async def test_occupied_slot_uses_valid_alternative(self):
        attempted, reported = [], []

        async def announce(data, ident):
            validate_upload(data, ident)
            self.assertEqual(parse_dial(data)["id"], ident)
            attempted.append(ident)
            return 9 if ident == 251 else 0

        def report(previous, ident, data):
            reported.append((previous, ident))

        err, data, ident = await resolve_collision(self.package, 253, 9, announce, report)
        self.assertEqual((err, ident), (0, 252))
        self.assertEqual(attempted, [251, 252])
        self.assertEqual(reported, [(253, 251), (251, 252)])

    async def test_all_slots_occupied_stops_without_repeating(self):
        attempted = []

        async def announce(data, ident):
            attempted.append(ident)
            return 9

        with self.assertRaisesRegex(RuntimeError, "slots customizados conhecidos"):
            await resolve_collision(self.package, 253, 9, announce, lambda *_: None)
        self.assertEqual(attempted, list(CUSTOM_SLOTS[1:]))

    async def test_unoccupied_slot_keeps_original_file(self):
        async def announce(*_):
            self.fail("No additional start should be sent")

        err, data, ident = await resolve_collision(self.package, 253, 0, announce, lambda *_: None)
        self.assertEqual((err, data, ident), (0, self.package, 253))


if __name__ == "__main__":
    unittest.main()
