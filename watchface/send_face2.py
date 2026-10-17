"""Send the corrected LS11 dial. No OTA, no SiFli 0200."""

import asyncio
import binascii
import os
import sys
import time
from pathlib import Path

from bleak import BleakClient, BleakScanner
from winrt.windows.devices.enumeration import DevicePairingProtectionLevel

ADDR = os.environ.get("HAYLOU_WATCH_ADDRESS", "").strip().upper()
WRITE = "000002ff-3c17-d293-8e48-14fe2e4da212"
INDICATE = "000003ff-3c17-d293-8e48-14fe2e4da212"
CONFIRM = "000004ff-3c17-d293-8e48-14fe2e4da212"
NOTIFY = "000005ff-3c17-d293-8e48-14fe2e4da212"
LINK_ACK = bytes.fromhex("66770000000000")
HERE = Path(__file__).resolve().parent
FILE = HERE / "out" / "my_face.bin"
LOG_FILE = HERE / "send_face2.log"
CHUNK = 4096
PIC = 253
if len(sys.argv) >= 3:
    FILE = Path(sys.argv[1])
    PIC = int(sys.argv[2])
SLICE = 495
GAP = 0.012
TZ = bytes.fromhex("e802fdffffffffffffffff01")


class Tee:
    """Keep the editor console output and a durable diagnostic log."""
    def __init__(self, stream, mirror):
        self.stream = stream
        self.mirror = mirror

    def write(self, text):
        self.stream.write(text)
        self.mirror.write(text)
        self.mirror.flush()

    def flush(self):
        self.stream.flush()
        self.mirror.flush()


_log_handle = LOG_FILE.open("a", encoding="utf-8", buffering=1)
sys.stdout = Tee(sys.stdout, _log_handle)
sys.stderr = Tee(sys.stderr, _log_handle)
print("\n===", "local validation" if "--check" in sys.argv else "upload", time.strftime("%Y-%m-%d %H:%M:%S"), "file", FILE, "id", PIC, "===", flush=True)


def varint(n):
    n &= 0xFFFFFFFF
    out = bytearray()
    while True:
        b = n & 0x7F
        n >>= 7
        if n:
            out.append(b | 0x80)
        else:
            out.append(b)
            return bytes(out)


def proto_len(blob):
    return varint(len(blob)) + blob


def seconds():
    return varint(int(time.time()))


def frame(payload):
    marker = 3
    crc = binascii.crc_hqx(payload, 0) & 0xFFFF
    hdr = bytearray(9)
    hdr[0] = 0x7E
    hdr[1] = 0x6D
    hdr[2] = marker & 0xFF
    hdr[3] = len(payload) & 0xFF
    hdr[4] = (len(payload) >> 8) & 0xFF
    hdr[5] = crc & 0xFF
    hdr[6] = (crc >> 8) & 0xFF
    total = sum(hdr[:7]) & 0xFFFF
    hdr[7] = total & 0xFF
    hdr[8] = (total >> 8) & 0xFF
    return bytes(hdr) + payload


def read_varint(data, i):
    n = 0
    shift = 0
    while i < len(data) and shift <= 35:
        b = data[i]
        i += 1
        n |= (b & 0x7F) << shift
        if not (b & 0x80):
            return n, i
        shift += 7
    return None, i


def parse(data):
    fields = {}
    i = 0
    while i < len(data):
        key, i2 = read_varint(data, i)
        if key is None:
            break
        i = i2
        fn, wt = key >> 3, key & 7
        if wt == 0:
            val, i = read_varint(data, i)
            if val is None:
                break
            fields.setdefault(fn, []).append(("v", val))
        elif wt == 2:
            ln, i = read_varint(data, i)
            if ln is None or i + ln > len(data):
                break
            fields.setdefault(fn, []).append(("b", bytes(data[i : i + ln])))
            i += ln
        else:
            break
    return fields


def cmd_of(fields):
    if 1 in fields and fields[1][0][0] == "v":
        return fields[1][0][1]
    return None


def err_of(fields):
    if 48 not in fields or fields[48][0][0] != "b":
        return None
    inner = parse(fields[48][0][1])
    if 1 in inner and inner[1][0][0] == "v":
        return inner[1][0][1]
    return None


def mtu_of(fields):
    if 32 not in fields or fields[32][0][0] != "b":
        return None
    inner = parse(fields[32][0][1])
    err = inner[1][0][1] if 1 in inner and inner[1][0][0] == "v" else None
    mtu = inner[2][0][1] if 2 in inner and inner[2][0][0] == "v" else None
    bind = inner[3][0][1] if 3 in inner and inner[3][0][0] == "v" else None
    return err, mtu, bind


def dials_of(fields):
    blobs = []
    if 77 in fields and fields[77][0][0] == "b":
        blobs.append(fields[77][0][1])
    if 87 in fields and fields[87][0][0] == "b":
        outer = parse(fields[87][0][1])
        for kind, blob in outer.get(1, []):
            if kind == "b":
                blobs.append(blob)
    found = []
    for blob in blobs:
        inner = parse(blob)
        item = {}
        if 1 in inner and inner[1][0][0] == "v":
            item["id"] = inner[1][0][1]
        if 2 in inner and inner[2][0][0] == "v":
            item["type"] = inner[2][0][1]
        if 3 in inner and inner[3][0][0] == "b":
            item["name"] = inner[3][0][1].split(b"\x00", 1)[0].decode("utf-8", "replace")
        if item:
            found.append(item)
    return found


def msg_query(cmd):
    return b"\x08" + varint(cmd) + b"\x10\x00" + b"\x18" + seconds() + TZ


def msg_start(length, crc, pic):
    inner = b"\x08" + varint(1) + b"\x10" + varint(length) + b"\x18" + varint(pic) + b"\x20" + varint(crc)
    body = b"\x08" + varint(34) + b"\x10\x00" + b"\x18" + seconds()
    body += varint((34 << 3) | 2) + proto_len(inner) + TZ
    return body


def msg_chunk(sn, data):
    inner = b"\x08" + varint(sn) + b"\x12" + proto_len(data)
    body = b"\x08" + varint(32) + b"\x10\x00" + b"\x18" + seconds()
    body += varint((33 << 3) | 2) + proto_len(inner) + TZ
    return body


def msg_select(dial_id, dial_type=0, name=b""):
    info = b"\x08" + varint(dial_id) + b"\x10" + varint(dial_type)
    if name:
        info += b"\x1a" + proto_len(name)
    body = b"\x08" + varint(82) + b"\x10\x00" + b"\x18" + seconds()
    body += varint((77 << 3) | 2) + proto_len(info) + TZ
    return body


def msg_delete(dial_id):
    info = b"\x08" + varint(dial_id) + b"\x10\x01" + b"\x1a" + proto_len(f"dial_{dial_id}".encode("ascii"))
    wrap = b"\x0a" + proto_len(info)
    body = b"\x08" + varint(94) + b"\x10\x00" + b"\x18" + seconds()
    body += varint((89 << 3) | 2) + proto_len(wrap) + TZ
    return body


async def connect():
    try:
        client = BleakClient(ADDR, timeout=20.0)
        await client.connect()
        return client
    except Exception as exc:
        print("[WARNING] Direct connection failed; scanning for the configured watch", exc, flush=True)
    found = await BleakScanner.discover(timeout=15.0)
    target = None
    for dev in found:
        name = (dev.name or "").upper()
        if (dev.address or "").upper() == ADDR:
            target = dev
            break
    if target is None:
        print("[ERROR] Watch not found", flush=True)
        for dev in found:
            print("scan", dev.address, dev.name, flush=True)
        return None
    client = BleakClient(target, timeout=20.0)
    await client.connect()
    return client


async def reconnect(client):
    # Pairing often drops the link itself. Avoid another Windows disconnect
    # request on a dead link, and release a live link before the short pause.
    if client.is_connected:
        try:
            await client.disconnect()
        except Exception as exc:
            print("[WARNING] Disconnect before reconnect failed", type(exc).__name__, exc, flush=True)
    await asyncio.sleep(0.5)
    return await connect()


async def main():
    global PIC
    if "--check" not in sys.argv:
        import re
        if not re.fullmatch(r"(?:[0-9A-F]{2}:){5}[0-9A-F]{2}", ADDR):
            raise RuntimeError("Set HAYLOU_WATCH_ADDRESS to your watch's Bluetooth address before connecting.")
    data = FILE.read_bytes()
    if not os.environ.get("QUERY"):
        sys.path.insert(0, str(LOG_FILE.parent))
        from build_live import validate_upload
        validate_upload(data, PIC)
        print("Package validated before connection", FILE.name, len(data), "bytes", flush=True)
        if "--check" in sys.argv:
            return
    skip = int(os.environ.get("OFFSET", "0"))
    limit = int(os.environ.get("LIMIT", "0"))
    if skip:
        data = data[skip:]
        print("[WARNING] Diagnostic offset", skip, flush=True)
    if limit:
        data = data[:limit]
        print("[WARNING] Diagnostic truncation", limit, flush=True)
    crc = 0
    parts = [data[i : i + CHUNK] for i in range(0, len(data), CHUNK)]
    probe = frame(msg_query(82))
    print(
        f"File {len(data)} bytes, CRC 0x{crc:04x}, {len(parts)} chunks of {CHUNK}, query {len(probe)}, timezone {len(TZ)}",
        flush=True,
    )
    if len(probe) != 31 or len(TZ) != 12:
        print("[ERROR] Unexpected query frame length", flush=True)
        return
    queue = asyncio.Queue()
    acks = asyncio.Queue()

    loop = asyncio.get_running_loop()

    def on_notify(sender, blob):
        raw = bytes(blob)
        target = acks if raw == LINK_ACK else queue
        loop.call_soon_threadsafe(target.put_nowait, raw)

    client = await connect()
    if client is None:
        return
    print("CONNECTED", getattr(client, "mtu_size", "?"), flush=True)
    paired = False
    try:
        print("Pairing with encryption only. Accept confirmation on the watch if requested.", flush=True)
        await asyncio.wait_for(
            client.pair(protection_level=DevicePairingProtectionLevel.ENCRYPTION),
            30,
        )
        paired = True
        print("PAIRED", flush=True)
    except Exception as exc:
        print("[WARNING] Pairing attempt failed", type(exc).__name__, exc, flush=True)
    if not paired or not client.is_connected:
        print("[WARNING] Link dropped during pairing; reconnecting automatically", flush=True)
        client = await reconnect(client)
        if client is None:
            return
        print("RECONNECTED", getattr(client, "mtu_size", "?"), flush=True)
        try:
            await asyncio.wait_for(
                client.pair(protection_level=DevicePairingProtectionLevel.ENCRYPTION),
                30,
            )
            paired = True
            print("PAIRED", flush=True)
        except Exception as exc:
            print("[ERROR] Pairing retry failed", type(exc).__name__, exc, flush=True)
    try:
        await client.start_notify(INDICATE, on_notify)
        await client.start_notify(NOTIFY, on_notify)
    except Exception as exc:
        print("[WARNING] Initial notifications failed; reconnecting", type(exc).__name__, exc, flush=True)
        client = await reconnect(client)
        if client is None:
            return
        print("RECONNECTED", getattr(client, "mtu_size", "?"), flush=True)
        await client.start_notify(INDICATE, on_notify)
        await client.start_notify(NOTIFY, on_notify)
    await asyncio.sleep(1.0)

    def drain():
        dropped = 0
        while not queue.empty():
            queue.get_nowait()
            dropped += 1
        return dropped

    last = {}

    async def wait_cmd(expect, timeout, quiet=False):
        buf = bytearray()
        seen = set()
        deadline = time.time() + timeout
        while time.time() < deadline:
            left = deadline - time.time()
            try:
                piece = await asyncio.wait_for(queue.get(), timeout=max(0.05, left))
            except asyncio.TimeoutError:
                break
            if piece == LINK_ACK:
                continue
            buf += piece
            while True:
                start = buf.find(b"\x7e\x6d")
                if start < 0:
                    if len(buf) > 2:
                        del buf[:-2]
                    break
                if len(buf) < start + 9:
                    break
                length = buf[start + 3] | (buf[start + 4] << 8)
                total = start + 9 + length
                if len(buf) < total:
                    break
                packet = bytes(buf[start:total])
                del buf[:total]
                if packet in seen:
                    continue
                payload = packet[9:]
                got = binascii.crc_hqx(payload, 0) & 0xFFFF
                want = packet[5] | (packet[6] << 8)
                if got != want:
                    print("[WARNING] Received frame with an invalid CRC", packet[:12].hex(), flush=True)
                    continue
                seen.add(packet)
                fields = parse(payload)
                last["fields"] = fields
                cmd = cmd_of(fields)
                err = err_of(fields)
                dials = dials_of(fields)
                print(f"rx cmd {cmd} err {err} dial {dials}", flush=True)
                if cmd == expect:
                    return err, dials
        if not quiet:
            print(f"[WARNING] No response to command {expect}; connected {client.is_connected}", flush=True)
        return None, None

    async def revive():
        nonlocal client
        print("[WARNING] Reconnecting", flush=True)
        try:
            if client.is_connected:
                await client.disconnect()
        except Exception:
            pass
        for attempt in range(3):
            client = await connect()
            if client is None:
                return False
            uuids = {char.uuid.lower() for service in client.services for char in service.characteristics}
            if NOTIFY.lower() in uuids:
                break
            print("[WARNING] Incomplete GATT services", attempt, flush=True)
            try:
                await client.disconnect()
            except Exception:
                pass
            await asyncio.sleep(1.5)
        else:
            return False
        await asyncio.sleep(1.2)
        try:
            await client.start_notify(INDICATE, on_notify)
            await client.start_notify(NOTIFY, on_notify)
        except Exception as exc:
            print("[ERROR] Notifications failed", type(exc).__name__, exc, flush=True)
            return False
        await asyncio.sleep(0.8)
        print("RECONNECTED", getattr(client, "mtu_size", "?"), flush=True)
        return True

    async def send(payload, expect, timeout):
        if not client.is_connected and not await revive():
            return None, None
        drain()
        framed = frame(payload)
        print(f"TX {len(framed)} bytes, marker {framed[2]}, slice {SLICE}, expecting {expect}", flush=True)
        try:
            for offset in range(0, len(framed), SLICE):
                await client.write_gatt_char(WRITE, framed[offset : offset + SLICE], response=False)
                await asyncio.sleep(GAP)
        except Exception as exc:
            print("[ERROR] Write failed", type(exc).__name__, exc, flush=True)
            await revive()
            return None, None
        result = await wait_cmd(expect, timeout)
        await asyncio.sleep(0.02)
        await flush_confirms()
        return result

    async def flush_confirms():
        n = 0
        while True:
            try:
                pkt = acks.get_nowait()
            except asyncio.QueueEmpty:
                break
            try:
                await client.write_gatt_char(CONFIRM, pkt, response=True)
                n += 1
            except Exception as exc:
                print("[ERROR] Confirmation failed", type(exc).__name__, exc, flush=True)
                break
        if n:
            print(f"Confirmed {n} receipt(s)", flush=True)

    try:
        att = max(20, int(getattr(client, "mtu_size", 23) or 23) - 3)
        global SLICE
        if SLICE > att:
            SLICE = att
        print("Slice", SLICE, "att", att, flush=True)
        if os.environ.get("QUERY"):
            print("Query only", flush=True)
            _err, dials = await send(msg_query(82), 82, 8)
            print("Current dial", dials, flush=True)
            _err, _dials = await send(msg_query(2), 2, 8)
            print("Device", _err, flush=True)
            return
        print("Starting upload", flush=True)
        err, _dials = await send(msg_start(len(data), crc, PIC), 34, 8)
        if err is None and not client.is_connected:
            print("[WARNING] Link dropped after MTU negotiation; waiting for advertising", flush=True)
            await asyncio.sleep(6.0)
            back = False
            for attempt in range(4):
                if await revive():
                    back = True
                    break
                print("Reconnect attempt", attempt, flush=True)
                await asyncio.sleep(3.0)
            if not back:
                print("[ERROR] Reconnection failed", flush=True)
                return
            err, _dials = await send(msg_start(len(data), crc, PIC), 34, 8)
        if err == 9:
            from upload_slots import resolve_collision

            async def announce_slot(candidate, ident):
                error, _ = await send(msg_start(len(candidate), crc, ident), 34, 8)
                return error

            def report_slot(previous, ident, candidate):
                target = FILE.with_name(FILE.stem + f"_dial_{ident}.bin")
                if target.exists() and target.read_bytes() != candidate:
                    import hashlib
                    target = target.with_name(target.stem + "_" + hashlib.sha256(candidate).hexdigest()[:8] + ".bin")
                target.write_bytes(candidate)
                print(f"[WARNING] ID {previous} is occupied; installed face preserved. Trying ID {ident}.", flush=True)
                print("Retargeted binary", target, len(candidate), "bytes", flush=True)

            err, data, PIC = await resolve_collision(data, PIC, err, announce_slot, report_slot)
            parts = [data[i : i + CHUNK] for i in range(0, len(data), CHUNK)]
        if err == 14:
            print("[WARNING] Installer is busy; retrying", err, flush=True)
            await asyncio.sleep(1.5)
            err, _dials = await send(msg_start(len(data), crc, PIC), 34, 8)
        if err != 0:
            raise RuntimeError(f"Watch refused upload start: error {err}, ID {PIC}.")
        await asyncio.sleep(0.3)
        for index, part in enumerate(parts):
            if index:
                await asyncio.sleep(0.05)
            sn = 65535 if index == len(parts) - 1 else index
            print(f"Chunk {index}, serial {sn}, offset {index * CHUNK}", flush=True)
            err, _dials = await send(msg_chunk(sn, part), 32, 6)
            if err is None:
                err, _dials = await wait_cmd(32, 40, quiet=True)
                if err == 0:
                    print(f"[WARNING] Chunk {index} received a delayed acknowledgment", flush=True)
                await flush_confirms()
            accepted = err == 0
            if not accepted:
                print(f"[ERROR] Chunk {index} error {err}", flush=True)
            if not accepted:
                print(f"[ERROR] Transfer stopped at chunk {index}, offset {index * CHUNK}", flush=True)
                return
        print("Transfer complete", flush=True)
        _err, dials = await wait_cmd(82, 10, quiet=True)
        if not dials:
            _err, dials = await send(msg_query(82), 82, 8)
        if not any(d.get("id") == PIC for d in dials or []):
            await asyncio.sleep(5.0)
            _err, pushed = await wait_cmd(82, 8, quiet=True)
            if pushed:
                dials = pushed
            else:
                _err, dials = await send(msg_query(82), 82, 8)
        print("Final dial", dials, flush=True)
        if not any(d.get("id") == PIC and d.get("type") == 1 for d in dials or []):
            raise RuntimeError(
                f"Transfer complete, but face {PIC} did not activate on the watch. "
                f"Current dial: {dials}. Check the module and resources in the binary."
            )
        print("Face installed and active", PIC, flush=True)
        _err, listed = await send(msg_query(93), 93, 6)
        print("Final dial list", listed, flush=True)
    finally:
        if paired:
            try:
                await client.unpair()
                print("UNPAIRED", flush=True)
            except Exception as exc:
                print("[WARNING] Unpairing failed", type(exc).__name__, exc, flush=True)
        if client.is_connected:
            await client.disconnect()
        print("DISCONNECTED", flush=True)


if __name__ == "__main__":
    asyncio.run(main())
