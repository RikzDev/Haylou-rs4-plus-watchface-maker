# Testing and validation

[Project overview](../README.md) | [Documentation index](README.md)

Run the suite from the repository root:

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s watchface -p 'test_*.py' -v
```

ARM compilation checks require the compiler. Runtime checks additionally require Unicorn; unavailable dependencies cause the relevant tests to be skipped. The latest verification of the published source ran **50 tests**, including:

- Required resource and language-pack exports, plus rejection of known older modules.
- Package retargeting and occupied-slot handling without automatic deletion.
- PNG character mappings, artwork preservation and resized hand pixels.
- Preview/export agreement on glyph pixels, spacing, coordinates and element z-index.
- Year, calories, whole/fractional distance, km/mi, AM/PM and 12/24-hour transitions.
- Configurable progress sequences, unavailable heart-rate readings, goal crossings, large values and independent frame offsets.
- Weather date selection, signed values, Fahrenheit conversion, cache refresh, missing data and allocation boundaries with mocked firmware calls.
- All 256 weather condition bytes, native day/night mapping, 12/24-hour boundaries, fixed condition slots and shared temperature/icon fetching.

To exercise a generated module without Bluetooth:

```powershell
.\.venv\Scripts\python.exe .\watchface\emulate_module.py .\watchface\out\my_face.bin `
    --today-size 64 --heart-size 8 --iterations 500 --initialize
```

These are simulated firmware services with explicit output sizes. They help detect memory and resource-access errors, but do not reproduce the complete firmware.
