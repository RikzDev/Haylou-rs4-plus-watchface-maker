# Installation

[Project overview](../README.md) | [Documentation index](README.md)

## Automatic installation

On **Windows x64**, download and extract the project's source ZIP. Open **PowerShell** in that folder and run one command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

The [installer](../install.ps1) downloads the current `main` source archive, reuses compatible dependencies when available, otherwise installs private Python 3.13, .NET 9 SDK / Desktop runtime and xPack ARM GCC 12.3.1-1.2. It creates a project-specific Python environment, installs the runtime packages, builds only Release, and creates Desktop and Start menu shortcuts named **RS4 Studio**. It opens the editor when installation finishes.

Git and administrator privileges are not required. The installer does not change your global PATH or machine execution policy. Internet access is required; the first run downloads several hundred megabytes if the toolchains are missing. The source archive already includes the eZip converter and its notice. Python downloads are SHA-256 checked against the pinned release; the ARM archive is checked against its upstream checksum. Microsoft provides the [.NET installer script](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script); Python's Windows installation options are documented [here](https://docs.python.org/3.13/using/windows.html#installing-without-ui).

By default, installation uses `%LOCALAPPDATA%\HaylouFaceEditor`:

- `Start-Editor.ps1` launches the active editor with the correct dependency paths.
- `versions/<source hash>/` holds the source snapshot, its `.venv` and Release build.
- `tools/` holds dependencies installed by this script; compatible existing tools are reused.
- `installation.json` records the active version and tool paths; `install.log` records setup output.

Rerun the command to install the current `main` snapshot. Older versions and saved projects are retained. A failed build leaves the previous launcher target unchanged. Save your work and close the installed editor before reinstalling the same snapshot.

To choose another folder or finish without launching:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -InstallDirectory 'C:\Apps\RS4 Studio' -NoLaunch
```

Additional switches: `-NoShortcut` skips shortcuts; `-SourceArchive 'C:\Downloads\source.zip'` uses a local source archive containing one top-level project folder. Dependencies may still need downloads.

Bluetooth setup remains optional. To upload, configure `HAYLOU_WATCH_ADDRESS` as described in the manual instructions below. The installer does not connect to your watch.

## Manual installation

Run the commands below in **PowerShell**, from the repository root unless stated otherwise.

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
