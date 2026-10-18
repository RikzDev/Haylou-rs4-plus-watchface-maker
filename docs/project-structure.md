# Project structure

[Project overview](../README.md) | [Documentation index](README.md)

```text
install.ps1                   Per-user dependency and Release installer
tests/install.Tests.ps1        Isolated installer regression checks
editor/                       Windows WPF application
  MainWindow.xaml             Main interface
  MainWindow.xaml.cs          Import, project persistence, build and send actions
  StudioWorkspace.cs           Explorer, artboard, inspector and animation timeline
  WeatherIcons.cs              Live weather palettes and personal defaults
  WeatherIconsWindow.xaml      Weather condition image manager
  ColorPickerWindow.xaml       Shared HEX/HSV color picker
  ColorPicking.cs              Text, background and image color actions
  CanvasEditing.cs            Selection, movement and resize gestures
  FontEditing.cs              Imported/external font selection
  PngFonts.cs                 PNG font registration, editing and shared mappings
  PngFontWindow.xaml          PNG character mapping editor
  BackgroundEditing.cs        Background image placement, color and persistence
  Layers.cs                   Element z-index and layer selection
  IconLibrary.cs              Built-in icon gallery and sequence creation
  ImageColor.cs               Reversible image and sequence tinting
  Assets/TbwfIcons/           Bundled drawable definitions and attribution
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
  activity_time_analysis.md   Activity fields, AM/PM and progress frame rules
  official_icon_analysis.md   Official image-resource and icon findings
  factory_reset.py            Development recovery helper
  out/                        Generated local artifacts
docs/                         Installation, editing, upload and developer guides
  images/                     Editor screenshots
  design/                     Studio layout plan and wireframe
  reverse-engineering.md      Research notes without raw device/session data
requirements.txt              Runtime Python dependencies
requirements-dev.txt          Optional emulation dependency
```

`incoming/`, generated binaries, phone captures and third-party designs are local research/output artifacts and are excluded from version control.
