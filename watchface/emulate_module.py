"""Exercise an ARM tick with mocked firmware writes; never uses Bluetooth.

Requires optional unicorn. Firmware output sizes are explicit test inputs,
not measurements of the device's proprietary implementations.
"""

import argparse
import struct
from datetime import datetime, timezone
from pathlib import Path

from build_live import elf_sections
from rs4face import parse_dial


def symbols(blob, dynamic_only=False):
    sections = elf_sections(blob)
    result = {}
    indexed = {}
    for index, (kind, start, size, link, stride) in enumerate(sections):
        if kind not in ((11,) if dynamic_only else (2, 11)):
            continue
        _, names_start, names_size, _, _ = sections[link]
        names = blob[names_start:names_start + names_size]
        table = []
        for offset in range(start, start + size, stride):
            name_offset, value, length, info, _, section = struct.unpack_from("<IIIBBH", blob, offset)
            end = names.find(b"\0", name_offset)
            name = names[name_offset:end].decode("ascii")
            entry = (name, value, length, info, section)
            table.append(entry)
            if name and section:
                result[name] = entry
        indexed[index] = table
    return result, indexed


def exercise_tick(package_path, today_size=28, heart_size=4, iterations=3, initialize=False,
                  *, weather_records=None, weather_size=340, unit_type=1, time_sequence=None,
                  malloc_failure=False, trace=None, today_values=None, time_format=1,
                  time_format_sequence=None, record_ticks=False, heart_bpm=72, heart_flags=1):
    from unicorn import Uc, UC_ARCH_ARM, UC_MODE_THUMB, UC_HOOK_CODE
    from unicorn.arm_const import (
        UC_ARM_REG_R0, UC_ARM_REG_R1, UC_ARM_REG_R2, UC_ARM_REG_R3, UC_ARM_REG_R4,
        UC_ARM_REG_R5, UC_ARM_REG_R6, UC_ARM_REG_R7, UC_ARM_REG_R8,
        UC_ARM_REG_R9, UC_ARM_REG_R10, UC_ARM_REG_R11, UC_ARM_REG_SP,
        UC_ARM_REG_LR, UC_ARM_REG_PC,
    )

    package = parse_dial(Path(package_path).read_bytes())
    code = next(item["data"] for item in package["files"]
                if item["path"].endswith(".so") and not item["path"].endswith("_res.so"))
    resource = next(item["data"] for item in package["files"] if item["path"].endswith("_res.so"))
    cpu = Uc(UC_ARCH_ARM, UC_MODE_THUMB)
    code_base, resource_base = 0x1000000, 0x2000000
    face, sync, stubs, exit_address, stack = 0x3000000, 0x3002000, 0x4000000, 0x5000000, 0x601F000
    for blob, base in ((code, code_base), (resource, resource_base)):
        section_offset = int.from_bytes(blob[32:36], "little")
        section_stride = int.from_bytes(blob[46:48], "little")
        headers = [struct.unpack_from("<10I", blob, section_offset + index * section_stride)
                   for index in range(len(elf_sections(blob)))]
        end = max(header[3] + header[5] for header in headers if header[2] & 2)
        cpu.mem_map(base, (end + 4095) & ~4095)
    cpu.mem_map(face, 0x10000)
    cpu.mem_map(stubs, 0x10000)
    cpu.mem_map(exit_address, 0x1000)
    cpu.mem_map(0x6000000, 0x20000)
    cpu.mem_write(sync + 0x29, bytes([80]))
    exports = {}
    modules = [(code, code_base), (resource, resource_base)]
    tables = []
    for blob, base in modules:
        section_offset = int.from_bytes(blob[32:36], "little")
        section_stride = int.from_bytes(blob[46:48], "little")
        for index, (_, start, size, _, _) in enumerate(elf_sections(blob)):
            header = struct.unpack_from("<10I", blob, section_offset + index * section_stride)
            if header[2] & 2 and header[1] != 8:
                cpu.mem_write(base + header[3], blob[start:start + size])
        defined, indexed = symbols(blob)
        tables.append((defined, indexed))
        for name, entry in defined.items():
            if entry[3] >> 4:  # global/weak
                exports[name] = base + entry[1]
    callbacks = {}
    for (blob, base), (_, indexed) in zip(modules, tables):
        for kind, start, size, link, stride in elf_sections(blob):
            if kind != 9:
                continue
            for offset in range(start, start + size, stride):
                target, info = struct.unpack_from("<II", blob, offset)
                relocation = info & 255
                addend = int.from_bytes(cpu.mem_read(base + target, 4), "little")
                if relocation == 23:
                    value = base + addend
                else:
                    name, symbol_value, _, _, section = indexed[link][info >> 8]
                    if section:
                        value = base + symbol_value
                    elif name in exports:
                        value = exports[name]
                    else:
                        value = stubs + len(callbacks) * 4 + 1
                        callbacks[value & ~1] = name
                        exports[name] = value
                        cpu.mem_write(value & ~1, b"\x70\x47")
                    if relocation == 2:
                        value += addend
                cpu.mem_write(base + target, struct.pack("<I", value))

    next_object = face + 0x4000
    image_updates = 0
    object_sources = {}
    draw_objects = []
    object_positions = {}
    object_image_names = {}
    object_hidden = {}
    label_texts = {}
    image_names = {address: name for name, address in exports.items()
                   if name in ("vscode_bg", "hour_hand", "minute_hand", "second_hand")
                   or name.startswith("anim_") or (name.startswith("g") and "c" in name)}
    source_names = {address: name for name, address in exports.items() if name.startswith("g") and "c" in name}
    weather_calls, free_calls, time_calls = 0, 0, 0
    format_calls = 0
    malloc_sizes = []
    heap = face + 0x8000
    heap_size = 0

    def callback(cpu, address, size, _):
        nonlocal next_object, image_updates, weather_calls, free_calls, time_calls, heap_size, format_calls
        if address == exit_address:
            cpu.emu_stop()
            return
        name = callbacks.get(address)
        if name is None:
            return
        arg0, arg1 = cpu.reg_read(UC_ARM_REG_R0), cpu.reg_read(UC_ARM_REG_R1)
        result = 0
        if name == "dlmodule_get_user_data":
            result = face
        elif name == "get_local_time":
            now = (time_sequence or [(2026, 10, 14, 12, 34, 56, 3)])[min(time_calls, len(time_sequence or [0]) - 1)]
            cpu.mem_write(arg0, struct.pack("<H6B", *now))
            time_calls += 1
        elif name == "apph_cfg_time_format_get":
            result = (time_format_sequence or [time_format])[min(format_calls, len(time_format_sequence or [0]) - 1)]
            format_calls += 1
        elif name == "apph_todayinfo_target_info_get":
            values = {"raw0": 0, "calories": 23000, "distance": 0, "steps": 1234, **(today_values or {})}
            payload = struct.pack("<4I", *(values[key] for key in ("raw0", "calories", "distance", "steps"))) + b"\xa5" * max(0, today_size - 16)
            cpu.mem_write(arg0, payload[:today_size])
        elif name == "apph_hrspo2_info_get":
            cpu.mem_write(arg0, (bytes([heart_flags, heart_bpm, 99, 0]) + b"\x5a" * heart_size)[:heart_size])
        elif name == "get_sync_data":
            result = sync
        elif name == "rt_malloc":
            malloc_sizes.append(arg0)
            if not malloc_failure:
                heap_size = arg0
                cpu.mem_write(heap + heap_size, b"\xda" * 16)
                result = heap
        elif name == "rt_free":
            if arg0 != heap or bytes(cpu.mem_read(heap + heap_size, 16)) != b"\xda" * 16:
                raise RuntimeError("weather getter overwrote the allocation boundary")
            free_calls += 1
        elif name == "apph_cfg_weather_info_get":
            if arg1 != 0 or arg0 != heap:
                raise RuntimeError("weather getter uses wrong destination/selector")
            payload = bytearray(weather_size)
            for index, record in enumerate(weather_records or []):
                if index >= 7:
                    break
                payload[index * 48 + 1] = record.get("condition", 0)
                struct.pack_into("<h", payload, index * 48 + 8, record["temperature"])
                struct.pack_into("<I", payload, index * 48 + 12, record["timestamp"])
            cpu.mem_write(arg0, bytes(payload))
            weather_calls += 1
        elif name == "get_local_time_from_utc0timestamps":
            date = datetime.fromtimestamp(arg1, timezone.utc)
            cpu.mem_write(arg0, struct.pack("<H6B", date.year, date.month, date.day,
                                          date.hour, date.minute, date.second, date.isoweekday()))
        elif name == "apph_cfg_unit_type_get":
            result = unit_type
        elif name in ("lv_img_create", "lv_label_create", "lv_task_create"):
            result = next_object
            next_object += 32
            if name != "lv_task_create":
                draw_objects.append((result, "image" if name == "lv_img_create" else "label"))
        elif name == "lv_img_set_src":
            header, data_size, pixels = struct.unpack("<III", cpu.mem_read(arg1, 12))
            if not ((header >> 10) & 2047) or not ((header >> 21) & 2047):
                raise RuntimeError("image descriptor has invalid dimensions")
            cpu.mem_read(pixels, data_size)
            image_updates += 1
            object_sources[arg0] = source_names.get(arg1, "")
            object_image_names[arg0] = image_names.get(arg1, "")
        elif name == "lv_obj_set_pos":
            signed = lambda value: value if value < 0x80000000 else value - 0x100000000
            object_positions[arg0] = (signed(arg1), signed(cpu.reg_read(UC_ARM_REG_R2)))
        elif name == "lv_obj_set_hidden":
            object_hidden[arg0] = bool(arg1)
        elif name == "lv_label_set_text":
            label_texts[arg0] = read_string(arg1)
        elif name == "sprintf":
            fmt = read_string(arg1)
            signed = lambda value: value if value < 0x80000000 else value - 0x100000000
            args = [cpu.reg_read(UC_ARM_REG_R2), cpu.reg_read(UC_ARM_REG_R3)]
            count = fmt.count('%')
            if count > 2:
                raise RuntimeError("sprintf mock supports at most two values")
            import re
            conversions = re.findall(r"%[0-9]*([du])", fmt)
            args = [signed(value) if conversion == 'd' else value for value, conversion in zip(args, conversions)]
            text = fmt.replace('u', 'd') % tuple(args[:count]) if count else fmt
            cpu.mem_write(arg0, text.encode('ascii') + b'\0')
            result = len(text)
        elif name in ("lv_page_glue_obj", "lv_obj_set_parent_event",
                      "lv_ext_set_local_font", "lv_img_set_pivot",
                      "lv_img_set_angle", "lv_img_set_antialias"):
            pass
        elif name == "memset":
            cpu.mem_write(arg0, bytes([arg1 & 255]) * cpu.reg_read(UC_ARM_REG_R2))
            result = arg0
        else:
            raise RuntimeError(f"unexpected firmware call in tick: {name}")
        cpu.reg_write(UC_ARM_REG_R0, result)
        cpu.reg_write(UC_ARM_REG_PC, cpu.reg_read(UC_ARM_REG_LR))

    cpu.hook_add(UC_HOOK_CODE, callback)
    def read_string(address):
        data = bytearray()
        for index in range(64):
            value = bytes(cpu.mem_read(address + index, 1))
            if value == b'\0':
                return data.decode('ascii')
            data.extend(value)
        raise RuntimeError("Unterminated string")

    def glyph_text():
        return "".join(chr(int(name.split("c", 1)[1])) for name in object_sources.values() if name)
    registers = [UC_ARM_REG_R4, UC_ARM_REG_R5, UC_ARM_REG_R6, UC_ARM_REG_R7,
                 UC_ARM_REG_R8, UC_ARM_REG_R9, UC_ARM_REG_R10, UC_ARM_REG_R11]
    expected = [0x11000000 + index for index in range(len(registers))]
    def call(function):
        for register, value in zip(registers, expected):
            cpu.reg_write(register, value)
        cpu.reg_write(UC_ARM_REG_SP, stack)
        cpu.reg_write(UC_ARM_REG_LR, exit_address | 1)
        cpu.reg_write(UC_ARM_REG_R0, 0)
        cpu.emu_start((tables[0][0][function][1] + code_base) | 1, exit_address, count=100000)
        if cpu.reg_read(UC_ARM_REG_PC) != exit_address:
            raise RuntimeError(f"{function} did not return within the instruction limit")
        actual = [cpu.reg_read(register) for register in registers]
        if actual != expected or cpu.reg_read(UC_ARM_REG_SP) != stack:
            changed = [f"r{index + 4}=0x{value:08x}" for index, value in enumerate(actual) if value != expected[index]]
            raise RuntimeError(f"{function} corrupted saved registers: " + ", ".join(changed))
    if initialize:
        call("face_init")
    ticks = []
    for _ in range(iterations):
        call("tick")
        if record_ticks:
            ticks.append(dict(glyph_text=glyph_text(), labels=list(label_texts.values()),
                              hidden=list(object_hidden.values()),
                              images=[object_image_names.get(obj, '') for obj, _ in draw_objects]))
    if trace is not None:
        trace.update(weather_calls=weather_calls, malloc_sizes=malloc_sizes, free_calls=free_calls,
                     draw_objects=[dict(type=kind, source=object_image_names.get(obj, ""),
                                        position=object_positions.get(obj), hidden=object_hidden.get(obj, False)) for obj, kind in draw_objects],
                     glyph_text=glyph_text(), label_texts=list(label_texts.values()), ticks=ticks)
    detail = f", {image_updates} validated image-source updates" if initialize else ""
    return f"tick preserved stack/registers in {iterations} iterations (today={today_size}, heart={heart_size}{detail})"


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("package", type=Path)
    parser.add_argument("--today-size", type=int, default=28)
    parser.add_argument("--heart-size", type=int, default=4)
    parser.add_argument("--iterations", type=int, default=3)
    parser.add_argument("--initialize", action="store_true")
    args = parser.parse_args()
    print(exercise_tick(args.package, args.today_size, args.heart_size, args.iterations, args.initialize))
