# Official LS11 activity and weather icons

## Scope

Inspected 14 distinct native dial packages from the local official-face collection: IDs 13, 50, 66, 77, 79, 89, 99, 100, 272, 273, 284, 291, 299 and 307. Duplicate downloaded files were deduplicated by SHA-256. No package was uploaded and no editor behavior was changed.

The extracted ELF inventory, decoded example images and Thumb disassembly are under `out/official-icon-analysis/`. These are local research artifacts.

## Findings

The inspected modules import general LVGL image creation/source functions, notably `lv_img_create` and `lv_img_set_src`. Their import inventory does not expose a specialized firmware function for drawing a steps, heart-rate or temperature icon.

Named resources such as `clock_thread_digit_heart` are image-descriptor objects, not drawing functions: in the resource ELF they have symbol type `STT_OBJECT`, length 12 bytes, and a descriptor pointing to compressed pixel data. The matching code ELF imports the descriptor for use with the general rendering interface.

| Face | Resource | Dimensions | Decoded artwork |
| --- | --- | --- | --- |
| 50 | `clock_thread_digit_heart` | 32 × 32 | White heart on a pink/red circle |
| 50 | `clock_thread_digit_steps` | 32 × 32 | White shoe on a green circle |
| 299 | `clock_no_signal_step` | 33 × 32 | Shoe |
| 272 | `clock_obsidian_step_0` … `_10` | 96 × 76 | Shoe with different progress-ring states |
| 272 | `clock_obsidian_hr_0` … `_6` | 96 × 76 | Heart with different ring states |
| 77 | `clock_red_tick_sunny` | 32 × 32 | Sun |
| 77 | `clock_red_tick_cloudy_day` | 32 × 32 | Cloud and sun |
| 77 | `clock_red_tick_no_data` | 32 × 32 | Cloud/question-mark fallback |

The examples use image format 2: eZip with alpha. Twelve selected resources, including two complete backgrounds, were reconstructed from descriptors and ELF relocations and successfully decoded.

Some icons are part of the background instead of independent resources. In face 77, the heart, battery/lightning, flame and location-pin artwork are already present in `clock_red_tick_bg`. The separate weather resources are selected dynamically.

## Weather versus numeric temperature

Face 77 includes 16 weather-condition images: sunny/day/night, cloudy variants, rain/shower variants, thunderstorm, snow, sleet, haze, wind, sandstorm and no data. Face 100 has a similar weather resource family.

The weather getter supplies data; it does not itself draw an icon. The official code selects the matching forecast date, reads its condition code and assigns a weather image. Numeric temperature is formatted and displayed separately. The condition byte is at offset +1 in the 48-byte forecast record; the signed Celsius value is at +8.

A dedicated built-in thermometer-drawing API was not found in this collection. This result does not prove that no other firmware module or uncollected dial uses one.

## Implications for the editor

- Steps and heart icons can be ordinary fixed PNG images next to their live numeric text. The current editor's fixed-image support already provides positioning and resizing for this approach.
- Progress-ring artwork can use multiple image states with an appropriate selection rule. Resource names alone do not establish each face's full index mapping.
- An automatic weather icon needs a weather-condition image selector and a validated condition-to-artwork mapping, including no-data and applicable day/night variants. The editor now provides a separate `weather` image source using the [validated 16-slot mapping](weather_icons_analysis.md); numeric `temperature` remains independent.
- Original or appropriately licensed icon artwork can use the existing resource builder and `place_still`/image-source path. A new specialized firmware drawing import is not necessary for the demonstrated approach.

The decoded examples remain local; original official artwork was not added to the published source repository.
