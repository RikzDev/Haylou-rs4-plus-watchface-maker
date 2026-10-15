# Activity values and AM/PM on LS11

## Official activity reference

Local package `incoming/1179e6401dde19cc735c1f023b0c2cc2.0` contains dial **54**. Its thumbnail labels the three activity values as steps, kcal and km. The Thumb update routine at `0x1040` calls `apph_todayinfo_target_info_get` with its destination at `sp+64` (`0x105a–0x105e`).

| Offset from the getter destination | Interpretation established by dial 54 | Display |
| --- | --- | --- |
| `+0x04` | Milli-kcal, bit 31 excluded | Integer kcal, divided by 1000 |
| `+0x08` | Distance in units of 1/100000 km, bit 31 excluded | Two decimals, km or mi |
| `+0x0c` | Steps, bit 31 excluded | Integer, capped at 99999 |

At `0x1218–0x1258`, the module reads `sp+68`, divides by 1000 and formats `%d`. Raw calorie values above 99999999 are replaced with 99999. At `0x125a–0x128a`, it reads `sp+76` and caps steps at 99999. At `0x1290–0x12e4`, it reads the unit preference and `sp+72`; unit value 2 selects miles using the factor 6214/10000. Other values use kilometers. The integer/fraction split divides by 100000 and 1000, and formats `%d.%02d`.

For example, raw distance 750000 is 7.50 km or 4.66 mi. The editor's implementation splits the imperial calculation into bounded 32-bit terms, preserving the conversion without an overflowing multiplication or an unresolved 64-bit division helper. `scaled` remains a legacy source; newly imported `calorie` and `distance` elements use their named sources. Saved elements with key `distance` and legacy source `scaled` are migrated on open.

## AM/PM reference

The requested **Schedule to Keep** calendar face is official **dial 100**, already captured as `dial_100.bin`, `incoming/c8a3ea89f54020f3cf1e7d492e1c2309.0` and its duplicate `18f4476c_c8a3ea89f54020f3cf1e7d492e1c2309.0`. Identification is confirmed by its resource ELF: `en_us_lang_translation` contains `key_clk_project_life` with value `Schedule to Keep` at byte offset 32. The same table provides `key_clk_pm` = `PM` at offset 64 and `key_clk_am` = `AM` at offset 160.

Its update routine at `0x142c` obtains the original hour and time-format preference. At `0x1452` it checks format 2 (12-hour mode). The AM/PM text comes from `lv_ext_str_get`, using those translation offsets, then `strcpy`; two spaces are appended before the month/year text. At `0x167e` the 24-hour branch writes an empty prefix. These markers are ordinary localized text, not dedicated AM/PM image assets.

A focused Unicorn run of the original routine through `0x14a4`, with mocked firmware calls and the confirmed English translations, verifies its branch behavior. The original calendar has an edge-case defect: `0x1466` compares the hour with 12 and `0x1474` branches to AM for hours **less than or equal to 12**. Thus noon still selects AM, and midnight remains hour 0. The editor intentionally keeps standard 12-hour behavior: midnight is 12 AM and noon is 12 PM, already covered by the generated-module tests. Local evidence is saved in `out/activity-analysis/calendar_ampm_trace.json`.

The captured official **dial 272** provides a second reference and contains separate AM and PM resources. Its update routine checks `apph_cfg_time_format_get` against 2 at `0x1fee–0x1ffe`, then compares the original hour with 11 at `0x2000–0x2006`. It selects white AM/black PM before noon and black AM/white PM from noon. In 24-hour mode both markers use black artwork. The Schedule to Keep identification comes from dial 100's translation table above; dial 272 is a separate AM/PM reference.

The editor derives AM/PM before converting the displayed hour. The `ampm` text source is blank in 24-hour mode. An AM/PM image sequence requires exactly two frames, AM then PM, and is hidden in 24-hour mode using `lv_obj_set_hidden`, an import also present in dial 272. The preview demonstrates the period; the watch decides whether to show it from its time-format setting.

## Validation and limits

`test_activity_runtime.py` compiles real ARM modules and exercises raster glyphs, firmware labels and image selection through Unicorn. Cases include marker bits, zero/large activity values, km/mi conversion, complete 64-byte getter writes, midnight/noon, 23:00, switching 12/24-hour mode, independent frame offsets, and preserved stack/registers. Tests also check that no unresolved `__aeabi_*` helper is introduced. PNG digit fonts receive a generated decimal point when they have no period asset; AM/PM text requires A, P and M glyphs.

A native WPF check verifies dropdown selection, TBWF source mapping, project save/reopen and actual `.bin` generation. The resulting package passes uploader preflight and ARM execution with both unit settings. These are local validation results with explicit firmware mocks, not a new on-watch test or a measurement of activity sensor accuracy. No official artwork or binary is distributed with these changes.

## Configurable progress frames (2026-10-15)

Heart-rate, steps, calories and whole-distance image sequences now use clamped progress selection rather than `value % frame_count`. Their default inclusive ranges are 0..200 BPM, 0..10000 steps, 0..1000 whole kcal and 0..10 whole km/mi. Battery keeps its existing fixed 0..100 range. Time/calendar selectors remain cyclic.

For a reading strictly between the endpoints, the frame is `(value - minimum) * count / (maximum - minimum + 1)`. Readings at/below minimum select frame 0; at/above maximum select the last frame. Missing heart-rate readings select frame 0. Bounds are validated before image conversion and when generating the C layout. Heart rate permits 0..255; activity ranges permit 0..99999 with minimum strictly below maximum. Clamping before multiplication bounds the product to 99999 * 64 and avoids overflow even for large firmware activity readings.

The sequence descriptor contains `min_value` and `max_value` alongside its source and frame table. No firmware imports, output buffer sizes or ABI requirements change. Distance still converts to the configured display unit before selecting a whole-unit progress frame. Saved project ranges are optional for compatibility: a missing range uses that mode's defaults. Choosing another mode in the editor resets the range to its defaults. The C# live preview shares the inclusive-bucket calculation and uses documented sample values.

`test_heart_animation_runtime.py` covers every BPM from 0 through 255, 1/5/6/64-frame sequences, nonzero bounds, unavailable readings, independent frame offsets, and 500 stable ticks. `test_activity_animation_runtime.py` covers default/custom bounds, goal crossings, km/mi, metadata flags, large values, overflow limits, offsets and repeated ticks using actual compiled ARM modules. These are local mocked-firmware checks; no device upload was performed for this change.
