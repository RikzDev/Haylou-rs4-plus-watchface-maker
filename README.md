# Haylou RS4 Plus Watchface Maker

A Windows watchface editor and reverse-engineering project for the **Haylou RS4 Plus (LS11)**.

Import a **TBWF watchface made for the standard Haylou RS4**, edit its images, text, fonts and animations, and build a native `.bin` watchface for the RS4 Plus. The editor converts supported elements into a new LS11 package; the watch does not load TBWF files directly.

**Repository:** [RikzDev/Haylou-rs4-plus-watchface-maker](https://github.com/RikzDev/Haylou-rs4-plus-watchface-maker)

> **Development status:** This is an experimental project. The editor, builder and Bluetooth uploader are included; install the dependencies and configure your watch address before uploading. Earlier experimental modules caused fallback screens and, in one case, a crash requiring a factory reset. Local validation helps catch known problems, but does not establish compatibility with every firmware or watchface.

## Contents

- [Features](#features)
- [Compatibility](#compatibility)
- [Installation](#installation)
- [Getting started](#getting-started)
- [Text, fonts and live data](#text-fonts-and-live-data)
- [Images and animations](#images-and-animations)
- [Sending a watchface to the watch](#sending-a-watchface-to-the-watch)
- [Command-line tools](#command-line-tools)
- [Technical information: how we built it](#technical-information-how-we-built-it)
- [Testing](#testing)
- [Troubleshooting](#troubleshooting)
- [Project structure](#project-structure)
- [References and credits](#references-and-credits)

## Features

- Import standard RS4 `.tbwf` designs, including supported images, embedded fonts and frame sequences.
- Edit a **368 × 448** watch preview with click selection, drag-and-drop movement and eight resize handles.
- Edit fixed images separately from animations and the background.
- Preview a sequence in a loop, inspect one frame, or use its data-driven behavior.
- Align individual frames with independent offsets.
- Reuse imported TTF/OTF fonts or load an external font for new text.
- Import numbers stored as separate PNG characters instead of a font.
- Adjust character size, color and spacing; PNG characters preserve their original artwork and colors.
- Display time, date, battery, steps, heart rate and weather temperature using firmware data.
- Save an editable project with its image and font assets, then generate a native LS11 `.bin` package.
- Validate package checksums, resource links and known module requirements before upload.
- With the uploader configured, generate an unsaved binary before sending and try another known custom ID when a slot is occupied.

The editor UI and tool output are in English. The Output panel uses white for information, yellow for warnings and red for errors. A blue upload note explains the expected first pairing failure and automatic retry.

## Compatibility

| Item | Support |
| --- | --- |
| Target watch | Haylou RS4 Plus, identified as LS11 in the investigated device |
| Output resolution | 368 × 448 pixels |
| Imported design format | Hello Haylou TBWF, including designs for the standard RS4 |
| Output format | SiFli `dial` container with an ARM module, resources and thumbnail |
| Desktop platform | Windows; the editor uses WPF and upload uses Windows Bluetooth APIs |
| Arbitrary watch models or firmware | Compatibility has not been established |

### Importing TBWF files from the standard RS4

**Yes: standard RS4 TBWF designs can be imported and adapted for the RS4 Plus.** The importer reads `TiBoWaFe.json`, the dial artwork and supported resource entries, then rebuilds the watchface for LS11.

This is a format conversion, so the original TBWF is not sent to the watch. Different proportions, unsupported widgets and resource naming conventions can require manual adjustments. The background is fitted to the RS4 Plus screen; check every element after import, especially designs with a different aspect ratio. A circular design does not automatically become a correctly arranged rectangular design.

## Installation

Run the commands in **PowerShell**, from the repository root unless stated otherwise.

### 1. Install the prerequisites

| Dependency | Purpose |
| --- | --- |
| Windows with a working Bluetooth adapter | Editor platform and optional PC upload |
| [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) | Build and run the WPF editor |
| [Python 3.13](https://www.python.org/downloads/windows/) | Image processing, package generation and diagnostics |
| Git, or a downloaded source archive | Obtain the project |
| [xPack GNU Arm Embedded GCC 12.3.1-1.2](https://github.com/xpack-dev-tools/arm-none-eabi-gcc-xpack/releases/tag/v12.3.1-1.2) | Compile the watch-side module |
| `watchface/tools/ezip.exe` | Convert images into the SiFli eZip format |

Python 3.13 and .NET 9 are the development versions used by this project. Other versions have not been systematically tested.

### 2. Download the source

```powershell
git clone https://github.com/RikzDev/Haylou-rs4-plus-watchface-maker.git
Set-Location Haylou-rs4-plus-watchface-maker
```

If using a source archive, extract it and open PowerShell in that folder instead.

### 3. Set up Python

```powershell
py -3.13 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
```

For optional ARM emulation and development tests:

```powershell
.\.venv\Scripts\python.exe -m pip install -r requirements-dev.txt
```

Using the virtual environment's executable directly avoids requiring a PowerShell activation-policy change.

### 4. Set up the ARM compiler and image converter

Download and extract the Windows xPack compiler. Set `HAYLOU_ARM_GCC` to the actual compiler executable:

```powershell
$env:HAYLOU_ARM_GCC = 'C:\Toolchains\xpack-arm-none-eabi-gcc-12.3.1-1.2\bin\arm-none-eabi-gcc.exe'
& $env:HAYLOU_ARM_GCC --version
```

The path above is an example. Launch the editor from this PowerShell session so it inherits the variable. To use it in later sessions, configure `HAYLOU_ARM_GCC` as a Windows user environment variable.

Without an override, the builder looks for the tested installation at:

```text
%LOCALAPPDATA%\HaylouFaceEditor\tools\armgcc\xpack-arm-none-eabi-gcc-12.3.1-1.2\bin\arm-none-eabi-gcc.exe
```

It also accepts the older `%TEMP%\armgcc` location as a fallback, but temporary folders can be cleaned by Windows or other tools.

Check the image converter:

```powershell
Test-Path .\watchface\tools\ezip.exe
```

If it is missing from your checkout, obtain it from [OpenSiFli/SiFli-SDK](https://github.com/OpenSiFli/SiFli-SDK), under `tools/png2ezip/ezip.exe`, and place it in `watchface/tools/`. Keep its [third-party notice](watchface/tools/NOTICE.txt) with the binary.

### 5. Check automatic path discovery

The editor locates `watchface/` relative to its executable or the working directory. It uses the repository's `.venv\Scripts\python.exe` when available, otherwise `python` on `PATH`. You do not need to edit source paths for a normal checkout.

For a custom installation, optional environment overrides are available:

```powershell
$env:HAYLOU_PROJECT_ROOT = 'C:\Projects\Haylou-rs4-plus-watchface-maker'
$env:HAYLOU_PYTHON = 'C:\Projects\Haylou-rs4-plus-watchface-maker\.venv\Scripts\python.exe'
```

Replace these example paths with your own. The project-root override points to the folder containing `watchface/`, not to `watchface/` itself.

### 6. Configure PC upload, if needed

**Editing and binary generation do not require a Bluetooth connection.**

The uploader is included as [`watchface/send_face2.py`](watchface/send_face2.py). Set the address of your own watch before starting the editor:

```powershell
$env:HAYLOU_WATCH_ADDRESS = 'AA:BB:CC:DD:EE:FF'
```

The value above is a placeholder, not a device to connect to. Obtain your watch's Bluetooth address using a BLE scanner. The uploader requires a configured address and does not select an arbitrary RS4 by its advertised name.

Launch the editor from the same PowerShell session, or set this as a Windows user environment variable for later launches. Upload logs are written to `watchface/send_face2.log`. The recovery helper uses the same address and is not part of normal installation.

### 7. Build and launch

```powershell
dotnet build .\editor\HaylouFaceEditor.csproj
dotnet run --project .\editor\HaylouFaceEditor.csproj
```

After building, the executable is normally available at:

```text
editor\bin\Debug\net9.0-windows\HaylouFaceEditor.exe
```

## Getting started

1. Launch the editor and choose **Import TBWF**.
2. Select a standard RS4 `.tbwf` design.
3. Check the background, text, fixed images and frame sequences in the preview.
4. Click an element to select it. Drag to move it, or use its handles to resize it.
5. Assign each text element its intended data source and font.
6. Save an editable `.hface.json` project.
7. Choose **Generate .bin** and select an output file.
8. Review the build log before attempting an upload.

**Shift** preserves image proportions during resizing. Text and clock hands preserve their proportions automatically. **Escape** cancels the current drag or resize. A click by itself does not move an element.

Keep the project JSON and its companion assets together. Saving a project can create a background PNG and sibling `-fonts`, `-characters` and `-frames` folders. The `.bin` is the generated watch package; it does not replace the editable project.

## Text, fonts and live data

Select a text element and choose its data source:

| Data source | Behavior |
| --- | --- |
| Fixed text | Displays the text you enter |
| Hour, minute, second | Uses the watch's clock |
| Month, day, weekday | Uses the watch's date; the text weekday source is numeric |
| Steps | Uses the decoded activity information |
| Heart rate | Shows the available heart-rate reading, otherwise `--` |
| Battery | Shows the numeric battery percentage |
| Temperature / weather | Selects today's synchronized forecast; uses the watch's Celsius/Fahrenheit setting, otherwise `--` |
| Scaled activity value | Uses a decoded activity field divided by 1000; its full semantics are still under investigation |

The preview sample is a design aid. It does not replace the live value used on the watch.

### Choosing a font

Use the font selector to reuse an embedded TBWF font in a new text element. Use **Load font** to add a `.ttf` or `.otf` file. Imported PNG character sets are also listed and can be reused for elements whose required characters are available.

Change **Character spacing** to adjust the distance between characters: positive values separate them, negative values bring them closer. The preview and exporter use the same raster cells and spacing.

Fixed text is currently limited to **12 characters**. The glyph runtime does not implement general Unicode text shaping, so complex scripts and arbitrary Unicode text are not established as supported. PNG character maps require explicit single-character ASCII mappings.

The battery source displays the number only: add a separate fixed `%` text if desired. Temperature likewise provides the numeric value; unit artwork can be a separate element. Weather must first be synchronized to the watch by the phone app.

## Images and animations

### Fixed images

Add or select a fixed image to move and resize it independently. It uses the same positioning controls as a sequence, with its mode set to **Fixed image**. The dial background remains the fitted background layer.

### Built-in TBWF icons

Some TBWF files refer to Hello Haylou icons by name (for example `stepResource_ic_wf_step_shoe_print.png`) without storing the image inside the archive. Their website preview can show a heart, shoe or weather icon even though the TBWF contains no matching PNG.

The editor resolves these references from **your own Hello Haylou APK**. Place `Hello+Haylou.apk` in the project root, set `HAYLOU_HELLO_APK` to its full path, or click **Load TBWF icon library (APK)** and select the APK. Then import the TBWF again. The library is cached under `%LOCALAPPDATA%\HaylouFaceEditor\tbwf-icons`; the app does not need to be installed on Windows. No APK or original app artwork is distributed in this repository.

Vector icons and animation lists are converted into editable PNG frames with the imported position, size and solid color. Transparent fills, strokes and trimmed progress paths are supported. Invisible resources remain hidden, and embedded images take priority over library resources. Unsupported drawable types produce a warning instead of silently disappearing; Android layer-list effects are currently unsupported.

Battery resources use battery-driven selection. Weather and Bluetooth connection icon sets are imported with all their frames in **Fixed image** mode: automatic weather-condition and connection-state selection is not implemented yet. Use **Single frame** to inspect them or reorder the desired image to the first position for a fixed display. Imported PNGs are saved with the project, so reopening it and generating a `.bin` no longer require the APK.

### Frame sequences

Select a sequence to add, remove or reorder its frames. Choose a preview mode:

- **Live/data preview:** follows the sequence's configured mode using preview values.
- **Loop preview:** plays all frames so you can inspect the entire sequence.
- **Single frame:** displays one selected frame for alignment.

In single-frame mode, dragging adjusts **only that frame's offset**. Resizing changes the common dimensions of the sequence. Playback pauses during an active drag or resize.

Supported sequence modes include continuous loop, battery, weekday, day of month, month, second, minute, 12-hour clock, steps, heart rate and the scaled activity value. A battery sequence selects its first frame at 0% and its last frame at 100%, distributing intermediate values across the sequence. Other data-driven modes use their implemented index mapping; check that your frame order matches the intended values.

The current builder permits up to **64 frames per sequence**, with frame dimensions no larger than **368 × 448**, and an interval from **200 to 10,000 ms**. These limits do not guarantee that every large combination fits the watch's available resources.

## Sending a watchface to the watch

This workflow requires the uploader configuration described in [Installation](#installation).

1. Generate the face and check the build log.
2. Turn off Bluetooth on the phone temporarily so it releases the watch connection.
3. Keep the watch near the PC and choose **Send to watch**.
4. If the binary has not been generated or has become outdated, the editor asks for an output path and builds it before sending.
5. Accept a pairing request on the watch if one appears.
6. Wait for the installation result, then check the actual watch display.
7. Re-enable phone Bluetooth after the PC disconnects.

The uploader starts with ID **253**. If that slot is occupied, it can try the other known custom IDs **251, 252 and 260** while preserving installed faces. These IDs were used in this project's experiments; they are not a universal reserved range. If every candidate is occupied, the uploader stops. It does not delete a working face automatically.

The first Windows pairing attempt may fail and disconnect, followed by a successful encryption-only reconnection. A completed chunk transfer is not by itself proof of installation: the uploader also waits for the watch to report the intended dial ID.

## Command-line tools

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

## Technical information: how we built it

The project grew from comparing official downloads, inspecting the phone applications and measuring the actual Bluetooth transfer. The aim was to reproduce the watch's existing installation and rendering interfaces.

```mermaid
flowchart LR
    A[Standard RS4 TBWF] --> B[WPF importer and editor]
    B --> C[Layout and assets]
    C --> D[Shared glyph rasterization]
    C --> E[ARM module compilation]
    C --> F[Image and animation resources]
    D --> F
    E --> G[LS11 dial package]
    F --> G
    G --> H[Local validation]
    H --> I[Haylou BLE upload]
    I --> J[RS4 Plus]
```

### 1. Obtain real watchface files

We examined the official app's assets and downloaded watchfaces, then monitored the Android Haylou Fun cache to retrieve native packages as they appeared. This provided actual files accepted by the watch, including faces containing activity widgets, image sequences and weather.

The cache-monitoring helper is [`watchface/monitor_haylou_cache.ps1`](watchface/monitor_haylou_cache.ps1). It uses ADB and watches the app's external cache, saving newly observed dial files into `incoming/`. Android permissions and storage restrictions vary; ADB/cache access is a research tool, not a requirement for normal editing. It uses the repository's Python environment, with an optional `HAYLOU_PYTHON` override.

### 2. Separate photo faces from executable watchfaces

The first important finding was that a photo face could replace the background but could not move the firmware's built-in clock. A fully editable face required the executable package format used by official dials.

We identified a container with the four-byte magic **`dial`**, version **`0x0701`** in the investigated files, an ID, a file-entry table and CRC-16 checksums. A typical live package contains:

| Member | Purpose |
| --- | --- |
| `dial_<id>.dsc` | Descriptor identifying the package members |
| `dial_<id>.so` | ARM code that creates and updates the face |
| `dial_<id>_res.so` | Image descriptors and pixel resources |
| `dial_<id>_tn.bin` | Thumbnail, typically 260 × 317 |

[`rs4face.py`](watchface/rs4face.py) implements container inspection, packing and checksum verification. The container checksums and Bluetooth framing checksums are separate concerns.

### 3. Reconstruct the watch-side module interface

Official modules revealed an ARM Thumb, Cortex-M33 hard-float target and firmware-provided LVGL/RT-Thread interfaces. We studied exported entry points, imported symbols, lifecycle callbacks, resource descriptors and relocations.

The custom module registers its initialization, pause, resume and deinitialization callbacks with `app_clock_register`. It uses the firmware's clock and activity APIs, creates LVGL image objects and updates their sources as values change. The build uses a data-only firmware import table to avoid unsupported compiler-generated PLT relocations.

### 4. Reproduce the resource layout

Images are converted to RGB565 eZip resources with the SiFli converter. Resource descriptors point at pixels in `.ROM3_IMG.*` sections of the resource module. We also reconstructed the built-in resource-manager setup and the required language-pack export; valid-looking pixels alone were insufficient for reliable module initialization.

Clock hands use uncompressed RGB565 with alpha because rotating compressed eZip images caused failures during development. Their dimensions, position and pivot are exported together.

### 5. Implement text, live values and animations

Official animated faces showed both animation-player calls and ordinary image swapping. Battery indicators could be data-selected images rather than a time-based animation. The editor's runtime uses frame-source changes and separate index rules for loops and data-driven sequences.

Custom fonts are rasterized into image glyphs. This avoids the investigated firmware's restrictions on system-font sizing and preserves the requested appearance. Separate digit PNGs use the same live glyph-selection path. Preview generation and package generation now share rasterization, cell dimensions and numeric padding, correcting discrepancies from the earlier native WPF text preview.

For temperature, official faces 77 and 100 revealed `apph_cfg_weather_info_get`, seven forecast records with a 48-byte stride, signed Celsius at offset `+8`, and a UTC timestamp at `+12`. The module selects today's local date and converts to Fahrenheit when the firmware's unit setting requests it. It caches the result by date and hour and uses a conservative 384-byte heap destination. The full proprietary output structure remains incompletely decoded. See the [weather analysis](watchface/weather_face77_analysis.md).

### 6. Reconstruct the Bluetooth upload

Phone Bluetooth HCI captures and app analysis established the Haylou transfer sequence. The working implementation announces the watchface with command **34**, sends command **32** data in **4096-byte chunks**, and divides framed writes into the observed **495-byte ATT slices** when the negotiated link permits them.

A serial confirmation exchange follows each chunk response. Command **82** reports the installed/active dial; it is not treated as a verified face-selection command. Timing, acknowledgment handling and pairing behavior mattered as much as the file bytes. Reusing acknowledgments or confusing the transfer chunk size with the ATT slice size led to repeated synchronization failures.

Custom-ID changes update the container, descriptor paths, ELF identities and checksums together. ID **254** belongs to the investigated firmware's photo-face path and is excluded from the live-module slot pool.

### 7. Investigate fallback and runtime crashes

Some early packages transferred successfully but fell back or crashed afterward. We investigated missing resource exports, initialization requirements, unsupported relocations, occupied IDs and firmware output-buffer sizes.

One crash investigation found that the activity getter could write beyond our original 16-byte structure. Comparing an official caller and using ARM emulation demonstrated how a larger write could overwrite saved registers. The module now reserves 64 bytes for activity output and 8 bytes for heart-rate/SpO2 output, with bounds suitable for the decoded fields and conservative extra space. Preflight rejects known older editor module revisions.

### 8. Distinguish implementation from device validation

A simple rebuilt face with a custom font was confirmed working on the watch during development. More complex combinations, the shared raster preview and the temperature implementation have local build, integrity and mocked-runtime checks; those checks do not run the real watch image decoder or prove stability on every firmware.

The [reverse-engineering notes](docs/reverse-engineering.md) summarize the investigation and the verified transfer settings. Raw phone captures, app binaries and development-session logs are kept outside this repository.

## Testing

Run the suite from the repository root:

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s watchface -p 'test_*.py' -v
```

ARM compilation checks require the compiler. Weather runtime checks additionally require Unicorn; unavailable dependencies cause the relevant tests to be skipped. The latest development verification ran **18 tests**, including:

- Required resource and language-pack exports, plus rejection of known older modules.
- Package retargeting and occupied-slot handling without automatic deletion.
- PNG character mappings, artwork preservation and resized hand pixels.
- Preview/export agreement on glyph pixels, spacing and coordinates.
- Weather date selection, signed values, Fahrenheit conversion, cache refresh, missing data and allocation boundaries with mocked firmware calls.

To exercise a generated module without Bluetooth:

```powershell
.\.venv\Scripts\python.exe .\watchface\emulate_module.py .\watchface\out\my_face.bin `
    --today-size 64 --heart-size 8 --iterations 500 --initialize
```

These are simulated firmware services with explicit output sizes. They help detect memory and resource-access errors, but do not reproduce the complete firmware.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| The editor cannot build or open | Confirm the .NET 9 SDK and rebuild the WPF project |
| Missing Python, compiler or image-converter error | Check `.venv`, optional `HAYLOU_PYTHON`/`HAYLOU_PROJECT_ROOT` overrides, `HAYLOU_ARM_GCC` and `watchface/tools/ezip.exe` |
| Upload script not found | Keep `watchface/send_face2.py` beside the builder and its helper modules |
| Watch address is not configured | Set `HAYLOU_WATCH_ADDRESS` before launching the editor; validation with `--check` does not need it |
| Watch not found or connection drops | Release the phone's Bluetooth connection, check the adapter and configured watch address, and keep the watch nearby |
| Pairing error followed by reconnection | The investigated Windows setup sometimes needs a second encryption-only attempt; inspect the final result |
| Occupied dial ID | Let the uploader try its known alternatives; if all are occupied, remove a face through the watch/app before trying again |
| Transfer completes but the wrong face remains | Check the final reported dial ID; chunk acknowledgments alone do not confirm installation |
| TBWF elements are missing or misplaced | Review visibility, supported sources, resource mappings, frame offsets and source aspect ratio |
| No `%` or temperature unit symbol | Add a separate text/image element; these sources provide numeric values |
| Temperature shows `--` | Synchronize weather from the phone and verify the watch date; no forecast for today is treated as unavailable |
| Preview succeeds but the watch falls back or crashes | Keep the generated binary and log, note the firmware and imported design, and investigate before retrying the same package |

The **Recovery** action is a factory reset. It removes watchfaces, pairing and settings, and is intended for recovery rather than routine installation.

## Project structure

```text
editor/                       Windows WPF application
  MainWindow.xaml             Main interface
  MainWindow.xaml.cs          Import, project persistence, build and send actions
  CanvasEditing.cs            Selection, movement and resize gestures
  FontEditing.cs              Imported/external font selection
  GlyphPreview.cs             Asynchronous raster preview
  ProjectPaths.cs             Repository and Python path discovery
watchface/
  build_live.py               Native live-watchface builder and validation
  preview_layout.py           Preview using the exporter's glyph rasters
  rs4face.py                  Dial-container and photo-face tools
  upload_slots.py             Occupied custom-ID handling
  send_face2.py               Bluetooth uploader and preflight entry point
  emulate_module.py           Optional ARM/firmware-mock diagnostics
  module/clock.c              Watch-side rendering and live data
  module/imports.c            Firmware import table
  tools/                      eZip converter and third-party notice
  test_*.py                   Regression tests
  monitor_haylou_cache.ps1     Optional Android cache capture helper
  weather_face77_analysis.md  Official weather-face analysis
  factory_reset.py            Development recovery helper
  out/                        Generated local artifacts
docs/reverse-engineering.md    Research notes without raw device/session data
requirements.txt              Runtime Python dependencies
requirements-dev.txt          Optional emulation dependency
```

`incoming/`, generated binaries, phone captures and third-party designs are local research/output artifacts and are excluded from version control.

## References and credits

- **[XorTroll/ls02-re](https://github.com/XorTroll/ls02-re)** — Reference for the documentation's separation of reverse-engineering notes, reusable implementation and desktop tooling. Its [technical notes](https://github.com/XorTroll/ls02-re/blob/main/RE.md) investigate the **Haylou Smart Watch 2 / LS02**. This project targets **RS4 Plus / LS11**, so LS02 command layouts are not assumed to apply here. This README uses the project as an organizational and research reference, without copying its protocol implementation.
- **[OpenSiFli/SiFli-SDK](https://github.com/OpenSiFli/SiFli-SDK)** — Source of the unchanged eZip converter used for image resources; see [NOTICE.txt](watchface/tools/NOTICE.txt).
- **[xPack GNU Arm Embedded GCC](https://github.com/xpack-dev-tools/arm-none-eabi-gcc-xpack)** — Compiler distribution used for the watch-side ARM module.
- **Hello Haylou and community watchface creators** — TBWF designs and resource conventions used to investigate and test import behavior. Original designs and fonts remain the work of their respective authors.

This is an independent community project, with no affiliation with or endorsement by Haylou.
