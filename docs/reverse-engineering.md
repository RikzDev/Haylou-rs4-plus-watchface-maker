# RS4 Plus / LS11 reverse-engineering notes

This document summarizes the observations used to implement the watchface builder and uploader. They concern the investigated RS4 Plus/LS11 firmware, rather than a universal protocol for every Haylou watch.

## Investigation sequence

1. Inspect the phone applications and their bundled watchface templates.
2. Retrieve official downloaded dial packages from the Android application cache.
3. Decode the native container, compare checksums and verify packing round trips.
4. Inspect ARM ELF imports, exports, image sections and lifecycle callbacks.
5. Build a minimal live face before adding activity data, fonts and frame sequences.
6. Compare actual phone-to-watch HCI traffic with the PC uploader.
7. Investigate installation fallback and runtime crashes independently of transfer success.
8. Add package checks, regression tests and mocked ARM runtime diagnostics.

The reference [XorTroll/ls02-re](https://github.com/XorTroll/ls02-re) documents a different model, LS02. Its organization is useful for separating protocol research from tools; its commands are not assumed to match LS11.

## Dial container

Investigated files begin with `dial`, contain version `0x0701`, and use a 64-byte base header followed by 64-byte file entries. Each entry specifies a stored checksum, payload offset, length and installation path. The file table has its own CRC-16, separate from each member's checksum.

Live faces usually contain a descriptor, an ARM code ELF, a resource ELF and a thumbnail. Their installed identities use paths such as `/ex/installer_wf/dial_253/`. Photo faces instead replace background resources under the firmware's fixed clock layout, using the investigated photo-face ID 254.

The local parser and packer are in `watchface/rs4face.py`. The live builder is in `watchface/build_live.py`.

## Module and image interfaces

- Target compiled by the project: Cortex-M33, Thumb, hard-float ARM ELF.
- Module lifecycle: initialization, pause, resume and deinitialization callbacks registered through `app_clock_register`.
- Rendering: firmware LVGL objects and functions, rather than a separately linked graphics library.
- Image resources: descriptors linked to `.ROM3_IMG.*` pixel sections in the resource ELF.
- Conversion: SiFli eZip converter, RGB565 with alpha where needed.
- Rotation: clock hands use uncompressed RGB565 plus alpha; rotating compressed resources caused faults during development.
- Resource initialization: built-in resource manager plus a nonempty language-pack descriptor.
- Supported relocation types validated by the builder: ABS32, GLOB_DAT and RELATIVE. Unexpected relocations are rejected.

The firmware's system-font sizing did not behave like the editor preview. The editor therefore exports raster glyphs and shares their generation with its preview. Explicit PNG character sets reuse that same live-value mechanism.

## Bluetooth transfer observed on the phone

| Channel | UUID | Role |
| --- | --- | --- |
| Write | `000002ff-3c17-d293-8e48-14fe2e4da212` | Framed Haylou commands |
| Indicate | `000003ff-3c17-d293-8e48-14fe2e4da212` | Link-level receipts |
| Confirm | `000004ff-3c17-d293-8e48-14fe2e4da212` | Receipt confirmation |
| Notify | `000005ff-3c17-d293-8e48-14fe2e4da212` | Command replies and dial information |

The investigated uploader uses Haylou protobuf commands inside frames beginning with `7e 6d`. It announces watchface data with command 34, data type 1, then sends command 32 chunks of 4096 bytes. The final chunk uses serial 65535. The phone capture used marker 3 and a zero announce-CRC field; package CRC checks remain mandatory locally.

For the observed negotiated link, framed writes were split into 495-byte ATT values. The code reduces that slice when the available ATT size is smaller. The command chunk and ATT slice are different layers.

The link receipt `66 77 00 00 00 00 00` is confirmed serially through the confirmation characteristic after the command response. Waiting for a command acknowledgment for each individual ATT slice, retaining duplicate-response state across chunks, or blindly retrying an uncertain chunk caused failures in earlier experiments.

Command 82 reports the dial ID and type after installation. It is not used as a verified selection command. The uploader requires the intended ID with type 1 before reporting a successful installation.

## Pairing and occupied IDs

The investigated Windows setup needs encryption-only pairing. An initial pairing failure can disconnect the link; reconnection and a second encryption-only attempt can succeed. The watch supports only one active connection, so the phone must release it during PC upload. The uploader attempts to remove its PC bond afterward so the phone can reconnect.

Error 9 indicates an occupied dial ID. Deletion requests were refused in the investigation, so the uploader preserves existing faces and tries its configured custom candidates: 253, 251, 252 and 260. All container paths, registered identities, ELF SONAMEs and checksums are updated together. This candidate list reflects the project experiments, not a vendor-reserved allocation.

Error 1 during chunk transfer was observed as a synchronization/CRC failure. A completed transfer still does not prove the installed module will initialize and remain stable.

## Runtime crashes and output buffers

Early custom-font code allocated only the decoded 16-byte activity prefix. An official caller left a larger destination area. Mocked firmware writes demonstrated that the old allocation could overwrite saved registers; this was a conditional reproduction, not a dump of the firmware getter's complete output.

The module now reserves 64 bytes for activity output and 8 bytes for heart-rate/SpO2 output. Fixed raster text has a 13-byte buffer for 12 characters plus a terminator, and bounded copying no longer advances past the source terminator. A module ABI marker lets preflight reject known older builds.

The activity field currently called `scaled` is divided by 1000. Its complete semantics have not been established, even though the importer associates an original distance widget with it.

## Weather

Official faces 77 and 100 exposed a seven-record forecast layout with a 48-byte stride. Signed Celsius is at record offset +8 and the UTC timestamp is at +12. Converting that timestamp to local time allows comparison against the current date. Unit type 2 selects Fahrenheit using signed integer conversion.

Both inspected official callers left 340 bytes for the weather getter. The editor module uses a zeroed 384-byte heap destination, frees it after decoding, and caches only the result and date/hour key. This is conservative storage, not a measurement of the getter's entire output structure. Missing timestamps or a missing matching date display `--`; a valid zero is displayed as zero.

See `watchface/weather_face77_analysis.md` for the original detailed analysis. Temperature is implemented and locally tested, but has not been confirmed on the watch through this project implementation.

## Validation boundaries

The project combines package/CRC checks, real ARM compilation checks, image-resource validation, and optional Unicorn execution with mocked firmware functions. Tests cover buffer preservation, character resources, preview/export agreement, ID collisions and weather selection/conversion.

A simple rebuilt custom-font face was confirmed working on the investigated watch. Larger animation combinations and recent editor changes still need device validation. Mocked ARM execution does not reproduce the real image decoder, memory allocator, radio timing or every firmware behavior.

Raw captures, downloaded application binaries, personal device identifiers and third-party watchface artwork are excluded from this source repository.
