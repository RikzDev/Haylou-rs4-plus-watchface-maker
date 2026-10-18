# Command-line tools

[Project overview](../README.md) | [Documentation index](README.md)

Use the virtual environment's Python executable in the following examples.

### Inspect or unpack a native package

```powershell
.\.venv\Scripts\python.exe .\watchface\rs4face.py info .\watchface\out\my_face.bin
.\.venv\Scripts\python.exe .\watchface\rs4face.py unpack .\watchface\out\my_face.bin .\unpacked --png
```

### Build a live watchface from a layout

The editor generates a layout internally. Developers can supply their own JSON containing `widgets` and optional `hands` and `animations`:

```powershell
.\.venv\Scripts\python.exe .\watchface\build_live.py `
    --png .\background.png `
    --layout .\layout.json `
    --id 253 `
    --name my_face `
    --title "My Face" `
    --output .\watchface\out\my_face.bin
```

The background must be 368 × 448. The package name must contain 1–15 UTF-8 bytes. Exported raster text is limited to a total of 64 glyph slots per face.

### Validate without connecting

The uploader's `--check` option performs package validation without a Bluetooth connection or configured watch address:

```powershell
.\.venv\Scripts\python.exe .\watchface\send_face2.py .\watchface\out\my_face.bin 253 --check
```

To send from the command line, configure `HAYLOU_WATCH_ADDRESS`, release the phone's Bluetooth connection, and run the same command without `--check`.

### Photo-only conversion

`rs4face.py create` and `rs4face.py tbwf` belong to the simpler **photo-face** path. They replace the background while the firmware draws its own fixed clock. Use the editor or `build_live.py` for editable live elements and animations.
