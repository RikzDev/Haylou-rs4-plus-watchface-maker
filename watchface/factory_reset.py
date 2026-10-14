"""Send the Haylou Fun factory reset (cmd 27, factory mode 0) in the boot window."""

import asyncio
import sys
import time

import send_face2 as s
from bleak import BleakClient, BleakScanner

SERVICE = "000001ff-3c17-d293-8e48-14fe2e4da212"


def reset_body():
    inner = b"\x08\x00"
    body = b"\x08" + s.varint(27) + b"\x10\x00" + b"\x18" + s.seconds()
    body += s.varint((28 << 3) | 2) + s.proto_len(inner) + s.TZ
    return body


async def blast(device):
    client = BleakClient(device, timeout=2.0, services=[SERVICE])
    await client.connect()
    print("CONNECTED", flush=True)
    try:
        framed = s.frame(reset_body())
        sl = min(495, max(20, int(getattr(client, "mtu_size", 23) or 23) - 3))
        for _ in range(3):
            for offset in range(0, len(framed), sl):
                await client.write_gatt_char(s.WRITE, framed[offset : offset + sl], response=False)
            await asyncio.sleep(0.05)
        print("Reset sent", len(framed), "connected", client.is_connected, flush=True)
        await asyncio.sleep(1.0)
        return True
    finally:
        try:
            await client.disconnect()
        except Exception:
            pass


async def main():
    if not s.ADDR:
        raise RuntimeError("Set HAYLOU_WATCH_ADDRESS before recovery.")
    print("[WARNING] Factory reset; scanning continuously", flush=True)
    print("Restart the watch now. Keep phone Bluetooth off.", flush=True)
    loop = asyncio.get_running_loop()
    seen = asyncio.Queue()

    def on_detect(device, _adv):
        if (device.address or "").upper() == s.ADDR:
            loop.call_soon_threadsafe(seen.put_nowait, device)

    scanner = BleakScanner(detection_callback=on_detect)
    await scanner.start()
    print("Listening", flush=True)
    deadline = time.time() + 180
    try:
        while time.time() < deadline:
            try:
                device = await asyncio.wait_for(seen.get(), timeout=max(0.2, deadline - time.time()))
            except asyncio.TimeoutError:
                break
            while not seen.empty():
                device = seen.get_nowait()
            print("Watch detected", flush=True)
            await scanner.stop()
            try:
                if await blast(device):
                    print("Reset command sent", flush=True)
                    return
            except Exception as exc:
                print("[WARNING] Connection attempt failed", type(exc).__name__, exc, flush=True)
            await scanner.start()
    finally:
        try:
            await scanner.stop()
        except Exception:
            pass
    print("[ERROR] Reset was not sent", flush=True)
    sys.exit(1)


if __name__ == "__main__":
    asyncio.run(main())
