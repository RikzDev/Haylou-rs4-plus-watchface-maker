# Editor guide

[Project overview](../README.md) | [Documentation index](README.md)

## Workspace

The default editor uses an IDE-style workspace with the existing warm dark palette. The visual plan is in [Studio workspace design](design/studio-workspace.md).

- Use **Project Explorer** on the left for layers, image/sequence assets and package settings.
- Select an element in the explorer or canvas to open its **Inspector** on the right. Text, image/sequence and background properties have separate tabs.
- Resize the panes with the vertical and horizontal splitters. **View > Reset panel layout** restores the defaults.
- Use **Fit**, the zoom buttons or **Ctrl+mouse wheel** over the artboard; zoom does not change exported coordinates.
- The bottom **Timeline** provides a track picker, frame filmstrip, ruler/playhead, transport and frame offsets. Click a frame or drag on the ruler to pause and inspect one frame. Drag a frame thumbnail to reorder it, preserving its original artwork and offsets. Use the scale slider to change the filmstrip width; long sequences scroll horizontally and follow a newly selected or playing frame.
- **Live data** follows the existing watch-value mapping. **Play loop** inspects artwork as a timed sequence. Data-driven frames are labeled as states; loop inspection does not change their exported data source.
- **Space** plays/pauses and **Left/Right**, **Home/End** select frames when focus is outside a text field or other value editor. **Ctrl+O**, **Ctrl+I**, **Ctrl+S** and **Ctrl+Shift+B** open, import, save and build.
- **Output** is a separate bottom tab with the existing severity colors. Build and upload operations display their output and disable editing while running.

Existing projects and the native watchface format remain compatible.

## Getting started

1. Launch the editor. It starts with an empty 368x448 black canvas and no elements.
2. Add text, images, sequences or built-in icons, or choose **Import TBWF** to start from a standard RS4 design.
3. Check the background, text, fixed images and frame sequences in the preview.
4. Click an element to select it. Drag to move it, or use its handles to resize it.
5. Assign each text element its intended data source and font.
6. Save an editable `.hface.json` project.
7. Choose **Build .bin** and select an output file.
8. Review the build log before attempting an upload.

**Shift** preserves image proportions during resizing. Text and clock hands preserve their proportions automatically. **Escape** cancels the current drag or resize. A click by itself does not move an element.

Importing a TBWF is optional. You can save and reopen a project created from scratch, generate a `.bin` and use **Send to watch** directly. Faces containing only images or animations, and even an empty background, are supported. You can also remove the final text element.

Keep the project JSON and its companion assets together. Saving a project can create a background PNG and sibling `-fonts`, `-characters` and `-frames` folders. The `.bin` is the generated watch package; it does not replace the editable project.

## Importing standard RS4 TBWF designs


**Yes: standard RS4 TBWF designs can be imported and adapted for the RS4 Plus.** The importer reads `TiBoWaFe.json`, the dial artwork and supported resource entries, then rebuilds the watchface for LS11.

This is a format conversion, so the original TBWF is not sent to the watch. Different proportions, unsupported widgets and resource naming conventions can require manual adjustments. The background is fitted to the RS4 Plus screen; check every element after import, especially designs with a different aspect ratio. A circular design does not automatically become a correctly arranged rectangular design.

## Background image

Use the **Background** tab in the Inspector to change the canvas independently of text, images and animations:

- **Change image...** loads a PNG, JPEG or BMP. **Remove image** keeps the fill color.
- **Fill color** accepts `#RRGGBB` and appears around the image and through transparent pixels. Click **Pick...** for the hex color picker. Text color uses the same picker; opacity is available for image/sequence tint.
- **Image fit** offers **Fill screen (crop)**, **Fit whole image**, **Stretch to screen** and **Original size**.
- **Zoom** scales the image from 10% to 400%. **X offset** and **Y offset** move it relative to its centered position.
- **Reset placement** restores centered, 100% fill-screen placement.

The preview updates immediately. The background remains below all elements. Saving preserves the original image in a companion `-background.png` file along with the fit, color, zoom and position, so you can adjust the crop again after reopening. The generated watch package uses the same rendered 368x448 background as the editor. Existing projects and TBWF backgrounds can also be edited.

## Text, fonts and live data

Select a text element and choose its data source:

| Data source | Behavior |
| --- | --- |
| Fixed text | Displays the text you enter |
| Hour, minute, second | Uses the watch's clock |
| Month, day, weekday | Uses the watch's date; the text weekday source is numeric |
| Year | Shows the full four-digit year from the watch, for example `2026` |
| Steps | Uses the decoded activity information |
| Heart rate | Shows the available heart-rate reading, otherwise `--` |
| Battery | Shows the numeric battery percentage |
| Temperature / weather | Selects today's synchronized forecast; uses the watch's Celsius/Fahrenheit setting, otherwise `--` |
| Calories (kcal) | Uses the decoded milli-kcal field, divided by 1000 |
| Distance (km/mi) | Shows two decimals and follows the watch's unit setting |
| Distance (whole km/mi) | Digits only, discarding decimals: `7.50` becomes `7`; follows the same unit setting |
| Distance unit (km/mi) | Shows `km` or `mi` for the current unit setting |
| AM/PM (12h mode) | Shows `AM` before noon and `PM` from noon; blank in 24-hour mode |

The old division-by-1000 option has been removed from the text and animation menus. Older projects using it load as **Calories (kcal)**; legacy distance elements load as **Distance (km/mi)**. The builder retains the old `scaled` key for compatibility with external layouts.

Distance uses its own decoded field rather than the old generic scaled value. TBWF `calorie` and `distance` elements now import with the correct sources. Older saved distance elements using the generic source are updated on open. See [activity and AM/PM analysis](../watchface/activity_time_analysis.md) for the official-dial evidence and validation scope.

The preview sample is a design aid. It does not replace the live value used on the watch.

### Text element names

Select a text element and edit **Element name** in the text inspector. The name is saved with the project and shown in Layers. It is independent of the rendered text, selected data source and font; use the text content field to change what the watch displays.

### Choosing a font

Use the font selector to reuse an embedded TBWF font in a new text element. Use **Load font** to add a `.ttf` or `.otf` file. Imported PNG character sets are also listed and can be reused for elements whose required characters are available.

To create an image font from separate PNGs:

1. Select a text element and click **Load PNG font...** in its font controls.
2. Select the character PNGs. The mapping window shows a thumbnail, filename and editable character for every image.
3. Check that each image maps to the correct character. Ten numbered images can be assigned to `0–9` automatically in numeric filename order; **Assign 0–9 in this order** can also apply that mapping. Duplicate or unsupported characters must be corrected before using the font.
4. Give the font a name and check the preview, then click **Use font**. Choose a numeric data source such as Hour, Minute, Battery or Steps, or enter fixed text containing the mapped characters.
5. Adjust size and character spacing in the text editor. The font appears in the font chooser and can be reused in other text elements.

**Save font...** creates a reusable `.hfont.json` file and a companion image folder. Keep them together. Open the `.hfont.json` through **Load PNG font...** to load that mapping directly. Saved projects also preserve loaded PNG fonts, including fonts that are not currently assigned to a text element. Original PNG colors and transparency are retained; this creates an image font rather than a TTF/OTF font.

### Editing a PNG font

1. Select a text element that uses the PNG font, then click **Edit PNG font...** below **Load PNG font...**.
2. Use **Add images** to include more letters, digits or punctuation. Check or change the character assigned to each image.
3. Select a row and choose **Replace image...** to change its artwork while keeping its character. **Remove selected** deletes character mappings from the font.
4. Edit the font name and preview text, then choose **Apply changes**. All text elements using that font update together, retaining their individual size, spacing, color, layer and position.

**Cancel** leaves the loaded font unchanged. **Save font...** exports a reusable `.hfont.json`; save the project to preserve the edited font with the face. Duplicate character assignments are rejected, and missing characters needed by existing text are reported in Output. Loaded font definitions support up to 95 printable ASCII characters (letters, digits and punctuation). The existing export limit remains 40 distinct requested characters per raster font set; unused catalogue characters are not packed.

Change **Character spacing** to adjust the distance between characters: positive values separate them, negative values bring them closer. The preview and exporter use the same raster cells and spacing.

Fixed text is currently limited to **12 characters**. The glyph runtime does not implement general Unicode text shaping, so complex scripts and arbitrary Unicode text are not established as supported. PNG character maps require explicit single-character ASCII mappings.

The battery source displays the number only: add a separate fixed `%` text if desired. Temperature likewise provides the numeric value; unit artwork can be a separate element. Weather must first be synchronized to the watch by the phone app.

## Images and animations

### Layers and z-index

Use the **Layers** panel to choose any text, image, sequence or clock hand, including an element hidden behind another one. The list runs from front to back. Enter its **Z-index** to set the drawing priority: higher numbers appear in front; negative values are supported. **Bring to front** and **Send to back** place the selected element at either end of the stack.

The preview and exported ARM module use the same stable layer order. All characters of a text element and all frames of a sequence stay together in their element's layer. Equal indices follow the existing collection order; the later element appears in front. The background stays behind the editable elements.

Layer values are saved with the project. Older projects retain the previous preview defaults: images/sequences at 0, hands at 100 and text at 200.

### Fixed images

Add or select a fixed image to move and resize it independently. It uses the same positioning controls as a sequence, with its mode set to **Fixed image**. The dial background remains the fitted background layer.

### Built-in TBWF icons

Some TBWF files refer to Hello Haylou icons by name (for example `stepResource_ic_wf_step_shoe_print.png`) without storing the image inside the archive. Their website preview can show a heart, shoe or weather icon even though the TBWF contains no matching PNG.

The editor now includes the extracted icon definitions as embedded resources. **You do not need to load an APK or populate a cache.** Supported references are resolved automatically when importing a TBWF. Click **Icon library** to browse thumbnails, search by name, filter by category and add a fixed icon or a complete sequence to your design. The gallery currently contains 179 supported entries; two Android layer-list circle effects remain unsupported.

Vector icons and animation lists are converted into editable PNG frames with the imported position, size and solid color. Transparent fills, strokes and trimmed progress paths are supported. Invisible resources remain hidden, and images embedded in the TBWF take priority over library resources. Unsupported drawable types produce a warning instead of silently disappearing.

Select an icon or image in **Image / Sequence**, then use **Icon / image color**. Click **Pick...** to open the hex color picker: drag within the color field, adjust hue and opacity, or paste a hexadecimal value. It accepts `#RRGGBB` and `#AARRGGBB` (alpha first), with or without `#`. The current and new colors are shown side by side. **Apply color** commits the selection; **Cancel** leaves the element unchanged. You can also enter `#RRGGBB` or `#AARRGGBB` directly in the inspector. The chosen color applies to every frame, updates the preview and is written into the PNGs exported to the watch. Click **Original** to restore the source artwork. Original frames and the selected color are saved with the project, so you can change or restore the color after reopening it without accumulating opacity changes. This also works with older saved projects and ordinary imported images.

Battery resources use battery-driven selection. Raw TBWF/library weather and Bluetooth connection sets are imported with all their frames in **Fixed image** mode. Use **Weather icons** for live weather with the validated native condition order; automatic Bluetooth connection selection remains unsupported. Use **Single frame** to inspect them or reorder the desired image to the first position for a fixed display. Imported PNGs are saved with the project, so reopening it and generating a `.bin` no longer require the APK.

### Live weather icons and personal defaults

Click **Weather icons** in the toolbar or **Add > Weather icons**. Choose **Filled** or **Outline** artwork, then **Apply and add weather** to create a live weather element. The watch selects today's synchronized forecast and switches between day and night variants for clear sky, partly cloudy sky and showers. Day is 06:00-18:59; night is 19:00-05:59. Missing forecasts and unknown condition codes use **No data / unknown**.

Use **Replace...** beside any condition to load a PNG, JPG or BMP. Transparent PNGs are supported; images are fitted without stretching. **Reset** restores one icon, and **Reset all to built-in** restores the whole set. **Apply to project** updates existing weather elements while preserving placement, size, z-index, frame offsets and tint. Use **Original** in the image inspector if you want the replacement's original colors instead of an existing tint.

Check **Use these icons as my defaults for new projects** to store your set locally. Each saved project keeps its own settings and custom images in a companion `*-weather` folder. Copy that folder together with the project and its other asset folders. Preferences are stored under `%LOCALAPPDATA%/HaylouFaceEditor/weather-icons/`.

Weather has 16 fixed condition slots. Use **Single frame** or timeline scrubbing to inspect them; add, remove and reorder are disabled to preserve the firmware mapping. An arbitrary 18-frame TBWF weather sequence uses a different order and is not converted automatically. The live editor preview shows clear daytime weather; the watch uses its actual synchronized data. See the [native mapping and validation](../watchface/weather_icons_analysis.md).

### Progress sequences: heart rate, steps, calories and distance

Choose **Heart rate**, **Steps**, **Calories (whole kcal)** or **Distance (whole km/mi)** for images that become progressively more filled as the measured value increases. Set **Minimum** and **Maximum** to define the visual range. Arrange frames from least filled to most filled.

| Mode | Default range | Unit | Highest configurable maximum |
| --- | --- | --- | --- |
| Heart rate | 0-200 | BPM | 255 |
| Steps | 0-10000 | Steps | 99999 |
| Calories (whole kcal) | 0-1000 | Whole kcal | 99999 |
| Distance (whole km/mi) | 0-10 | Whole km or mi, following the watch's unit setting | 99999 |
| Battery | 0-100, fixed | Percent | 100, fixed |

Enter whole numbers with `0 <= minimum < maximum`. Frames divide this inclusive range into equal buckets. With five heart-rate frames and the default range, the buckets are **0-40**, **41-80**, **81-120**, **121-160** and **161-200 BPM**. With five steps frames and a 0-10000 range, they are **0-2000**, **2001-4000**, **4001-6000**, **6001-8000** and **8001-10000 steps**. Values below or above the configured range stay on the first or last frame, so reaching a goal never loops back to an empty bar. An unavailable heart-rate reading selects the first frame. A single frame remains fixed; sequences support up to 64 frames.

**Live data** previews this mapping at sample values: **72 BPM**, **1234 steps**, **300 kcal** and **7 whole km/mi**. **Play loop** and **Single frame** inspect the artwork independently of the live selector. Choosing another mode resets the range to that mode's defaults. Ranges are saved with the project and included in the generated binary. Older projects without saved ranges use the defaults above. Regenerate and upload the `.bin` to update an existing watchface. Use **Continuous loop** for a repeating animation independent of measured data. Time and calendar sequences keep their existing cyclic selection.

### Frame sequences

To build a sequence from separate library icons:

1. Open **Icon library**, then search for a family such as `progress circular`.
2. Use **Ctrl+click** to select individual icons, **Shift+click** for a range, or **Select all shown** for the filtered group. Selection is kept when changing filters; **Clear selection** resets it.
3. Click **Create sequence**. Selected icons are combined into one sequence, in library order; numbered states are sorted numerically, such as 0, 10, 20 through 100. Selecting an existing library animation includes all its frames.
4. In the side panel, choose **Continuous loop** for a timed animation, or a data-driven mode such as **Battery**, **Second** or **Weekday**. Set the interval for timed loops and use **Play loop** or **Single frame** to inspect the result.

To extend an existing sequence, select it first, click **Add icons as frames...**, choose icons and click **Add to selected sequence**. New frames are inserted after the selected frame, or at the end when no frame is selected. They fit inside the sequence dimensions without stretching the icon, inherit its selected color, and preserve its position, mode, interval and existing frame offsets. Use the frame reorder buttons to adjust the order afterward.

The gallery shows the number of resulting frames and enforces the 64-frame limit for both new and existing sequences. A combined sequence normally starts as a continuous loop; a selection consisting only of battery resources uses battery mode.

Select a sequence to add, remove or reorder its frames. Choose a preview mode:

- **Live/data preview:** follows the sequence's configured mode using preview values.
- **Loop preview:** plays all frames so you can inspect the entire sequence.
- **Single frame:** displays one selected frame for alignment.

In single-frame mode, dragging adjusts **only that frame's offset**. Resizing changes the common dimensions of the sequence. Playback pauses during an active drag or resize.

Supported sequence modes include continuous loop, battery, weekday, day of month, month, second, minute, 12-hour clock, steps, calories, distance, AM/PM, heart rate. A battery sequence selects its first frame at 0% and its last frame at 100%, distributing intermediate values across the sequence. Other data-driven modes use their implemented index mapping; check that your frame order matches the intended values.

**AM/PM** sequences require exactly two frames: AM first, PM second. They follow the watch's 12-hour setting and are hidden in 24-hour mode. AM/PM can also be added as ordinary dynamic text, using a TTF/OTF font or PNG characters for A, P and M.

The current builder permits up to **64 frames per sequence**, with frame dimensions no larger than **368 × 448**, and an interval from **200 to 10,000 ms**. These limits do not guarantee that every large combination fits the watch's available resources.
