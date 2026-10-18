# Installation

[Project overview](../README.md) | [Documentation index](README.md)

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

If it is missing from your checkout, obtain it from [OpenSiFli/SiFli-SDK](https://github.com/OpenSiFli/SiFli-SDK), under `tools/png2ezip/ezip.exe`, and place it in `watchface/tools/`. Keep its [third-party notice](../watchface/tools/NOTICE.txt) with the binary.

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

The uploader is included as [`watchface/send_face2.py`](../watchface/send_face2.py). Set the address of your own watch before starting the editor:

```powershell
$env:HAYLOU_WATCH_ADDRESS = 'AA:BB:CC:DD:EE:FF'
```

The value above is a placeholder, not a device to connect to. Obtain your watch's Bluetooth address using a BLE scanner. The uploader requires a configured address and does not select an arbitrary RS4 by its advertised name.

Launch the editor from the same PowerShell session, or set this as a Windows user environment variable for later launches. Upload logs are written to `watchface/send_face2.log`. The recovery helper uses the same address and is not part of normal installation.

### 7. Build and launch

```powershell
dotnet build .\editor\HaylouFaceEditor.csproj -c Release
dotnet run --project .\editor\HaylouFaceEditor.csproj -c Release --no-build
```

After building, the executable is normally available at:

```text
editor\bin\Release\net9.0-windows\HaylouFaceEditor.exe
```
