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
                  malloc_failure=False, trace=None):
    from unicorn import Uc, UC_ARCH_ARM, UC_MODE_THUMB, UC_HOOK_CODE
    from unicorn.arm_const import (
        UC_ARM_REG_R0, UC_ARM_REG_R1, UC_ARM_REG_R2, UC_ARM_REG_R4,
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
    source_names = {address: name for name, address in exports.items() if name.startswith("g") and "c" in name}
    weather_calls, free_calls, time_calls = 0, 0, 0
    malloc_sizes = []
    heap = face + 0x8000
    heap_size = 0

    def callback(cpu, address, size, _):
        nonlocal next_object, image_updates, weather_calls, free_calls, time_calls, heap_size
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
            result = 1
        elif name == "apph_todayinfo_target_info_get":
            payload = struct.pack("<4I", 0, 23000, 0, 1234) + b"\xa5" * max(0, today_size - 16)
            cpu.mem_write(arg0, payload[:today_size])
        elif name == "apph_hrspo2_info_get":
            cpu.mem_write(arg0, (bytes([1, 72, 99, 0]) + b"\x5a" * heart_size)[:heart_size])
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
        elif name == "lv_img_set_src":
            header, data_size, pixels = struct.unpack("<III", cpu.mem_read(arg1, 12))
            if not ((header >> 10) & 2047) or not ((header >> 21) & 2047):
                raise RuntimeError("image descriptor has invalid dimensions")
            cpu.mem_read(pixels, data_size)
            image_updates += 1
            object_sources[arg0] = source_names.get(arg1, "")
        elif name in ("lv_obj_set_pos", "lv_page_glue_obj", "lv_obj_set_parent_event",
                      "lv_label_set_text", "lv_ext_set_local_font", "lv_img_set_pivot",
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
    for _ in range(iterations):
        call("tick")
    if trace is not None:
        trace.update(weather_calls=weather_calls, malloc_sizes=malloc_sizes, free_calls=free_calls,
                     glyph_text="".join(chr(int(name.split("c", 1)[1])) for name in object_sources.values() if name))
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
