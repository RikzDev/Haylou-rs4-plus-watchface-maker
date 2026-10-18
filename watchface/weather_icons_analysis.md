# Live weather image selection

The implementation uses the existing LS11 weather getter and ordinary LVGL image descriptors. No new proprietary drawing function is needed. Numeric temperature remains a separate text source.

## Evidence from official dial 77

The local `out/official-icon-analysis/dial_77_disassembly.txt` and matching code/resource ELF relocations show a condition byte at forecast-record offset `+1`, signed Celsius at `+8`, a UTC timestamp at `+12` and a 48-byte record stride. The existing weather-date selection finds today among seven records.

At code offset `0x1392`, the original hour is captured before 12-hour conversion. The selector at `0x1a56` subtracts six; an unsigned comparison against 12 selects daytime hours 6 through 18 inclusive. The condition jump table at `0x1a94` handles codes 1 through 12; other values select `clock_red_tick_no_data`.

Resource relocations resolve the branches to the following images. The editor's frame indices 1 through 12 intentionally match the native condition byte. Index 0 is fallback; indices 13 through 15 are night variants.

| Frame | Native condition | Artwork |
| --- | --- | --- |
| 0 | Missing or outside 1-12 | No data / unknown |
| 1 | 1 | Clear sky / day |
| 2 | 2 | Partly cloudy / day |
| 3 | 3 | Overcast |
| 4 | 4 | Showers / day |
| 5 | 5 | Thunderstorm |
| 6 | 6 | Rain and snow |
| 7 | 7 | Light rain |
| 8 | 8 | Heavy rain |
| 9 | 9 | Snow |
| 10 | 10 | Sandstorm |
| 11 | 11 | Haze |
| 12 | 12 | Wind |
| 13 | 1, night | Clear sky / night |
| 14 | 2, night | Partly cloudy / night |
| 15 | 4, night | Showers / night |

The images bundled in the editor come from its existing Hello Haylou drawable library, reordered to this native mapping. Official dial artwork is not copied into the source. Arbitrary TBWF weather arrays may have 18 entries and a different order, so those imports remain fixed-image sets. Use the Weather icons manager to create a correctly ordered live element.

## Runtime and storage

`weather` uses animation source number 15, independent of the text-source numbering. The weather getter and local-time getter are imported when either weather images or temperature text are present. Both features share one cached forecast fetch per date/hour. A 384-byte heap allocation retains the existing conservative getter boundary. Missing records, invalid codes and failed allocation select frame 0.

Each weather element exports 16 PNG image descriptors through the existing animation resource builder. The manager preserves element geometry, layer order, tint and per-frame offsets when replacing the palette. PNG transparency and the default artwork's original colors are preserved. Custom input is decoded before replacing project state.

Projects store their palette style and relative custom PNG paths alongside normal exported frames. User defaults are optional, local settings; saved projects keep their own palette. Preferences use a unique asset directory and an atomic settings-file replacement.

## Validation

`test_weather_icons_runtime.py` compiles and executes the generated ARM module in Unicorn with mocked firmware calls. It covers all 256 condition bytes by day and night, hours 0/5/6/12/18/19/23 in both clock formats, fallback, seventh-record selection, a full 384-byte getter write, allocation failure, 500 cached updates, shared temperature/icon fetching and Fahrenheit conversion. Invalid frame counts are rejected before conversion. These are local runtime tests; they do not substitute for a physical watch upload.
