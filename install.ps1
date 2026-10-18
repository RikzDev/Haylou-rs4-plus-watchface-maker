#requires -Version 5.1
<#
.SYNOPSIS
Install RS4 Studio and its dependencies for the current Windows user.
.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
.EXAMPLE
.\install.ps1 -InstallDirectory 'C:\Apps\RS4 Studio' -NoLaunch
#>
[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'HaylouFaceEditor'),
    [string]$SourceArchive,
    [switch]$NoShortcut,
    [switch]$NoLaunch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$Message) { Write-Host "[RS4 Studio] $Message" -ForegroundColor Cyan }
function Get-WindowsPowerShell { return (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') }

function Invoke-Checked([string]$File, [string[]]$Arguments) {
    & $File @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code $LASTEXITCODE." }
}

function Assert-ChildPath([string]$Parent, [string]$Child) {
    $prefix = [IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    $absolute = [IO.Path]::GetFullPath($Child)
    if (-not $absolute.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the installation directory: $absolute"
    }
}

function Save-Download([string]$Uri, [string]$Path, [string]$Sha256 = '') {
    Write-Step "Downloading $([Uri]$Uri | ForEach-Object Host)..."
    $previousProgress = $ProgressPreference
    try {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -UseBasicParsing -Uri $Uri -OutFile $Path -TimeoutSec 600
    }
    finally { $ProgressPreference = $previousProgress }
    if ($Sha256 -and (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Sha256) {
        throw "Download checksum mismatch: $Path"
    }
}

function Expand-CheckedArchive([string]$Archive, [string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.Replace('/', '\')
            Assert-ChildPath $Destination (Join-Path $Destination $name)
        }
    }
    finally { $zip.Dispose() }
    Expand-Archive -LiteralPath $Archive -DestinationPath $Destination -Force
}

function Test-Python313([string]$File) {
    if (-not $File -or -not (Test-Path -LiteralPath $File -PathType Leaf)) { return $false }
    try {
        $code = "import sys,struct; print(int(sys.version_info[:2]==(3,13) and struct.calcsize('P')==8))"
        $result = & $File -c $code 2>$null
        return ($LASTEXITCODE -eq 0 -and "$result".Trim() -eq '1')
    }
    catch { return $false }
}

function Get-Python313([string]$Base, [string]$Work) {
    $managed = Join-Path $Base 'tools\python313\python.exe'
    $candidates = @($managed, $env:HAYLOU_PYTHON,
        (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python313\python.exe'))
    $command = Get-Command python.exe -ErrorAction SilentlyContinue
    if ($command -and $command.Source -notlike '*\WindowsApps\*') { $candidates += $command.Source }
    $py = Get-Command py.exe -ErrorAction SilentlyContinue
    if ($py) {
        try {
            $resolved = & $py.Source -3.13 -c 'import sys; print(sys.executable)' 2>$null
            if ($LASTEXITCODE -eq 0) { $candidates += "$resolved".Trim() }
        }
        catch { Write-Verbose 'The Python launcher did not resolve Python 3.13.' }
    }
    foreach ($file in $candidates) {
        if (Test-Python313 $file) { Write-Step "Using Python: $file"; return $file }
    }
    $version = '3.13.15'
    $setup = Join-Path $Work 'python-setup.exe'
    Save-Download "https://www.python.org/ftp/python/$version/python-$version-amd64.exe" $setup 'edec09c4853aeae9ac36efb8c9f95b6b8e2fee65eee56d9767a8b7c69c574403'
    $target = Split-Path -Parent $managed
    Write-Step 'Installing a private Python 3.13 (no PATH or file association changes)...'
    $arguments = '/quiet InstallAllUsers=0 Include_launcher=0 InstallLauncherAllUsers=0 AssociateFiles=0 Shortcuts=0 PrependPath=0 Include_test=0 Include_doc=0 Include_tcltk=0 Include_pip=1 TargetDir="' + $target + '"'
    $process = Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -notin @(0, 3010) -or -not (Test-Python313 $managed)) {
        throw "Python installation failed (exit code $($process.ExitCode))."
    }
    return $managed
}

function Test-DotNet9([string]$File) {
    if (-not $File -or -not (Test-Path -LiteralPath $File -PathType Leaf)) { return $false }
    try {
        $sdks = @(& $File --list-sdks 2>$null)
        if ($LASTEXITCODE -ne 0 -or -not ($sdks -match '^9\.0\.\d+ \[')) { return $false }
        $runtimes = @(& $File --list-runtimes 2>$null)
        return ($LASTEXITCODE -eq 0 -and @($runtimes -match '^Microsoft.WindowsDesktop.App 9\.0\.').Count -gt 0)
    }
    catch { return $false }
}

function Get-DotNet9([string]$Base, [string]$Work) {
    $managed = Join-Path $Base 'tools\dotnet\dotnet.exe'
    $candidates = @($managed)
    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($command) { $candidates += $command.Source }
    if ($env:ProgramW6432) { $candidates += Join-Path $env:ProgramW6432 'dotnet\dotnet.exe' }
    foreach ($file in $candidates) {
        if (Test-DotNet9 $file) { Write-Step "Using .NET: $file"; return $file }
    }
    $script = Join-Path $Work 'dotnet-install.ps1'
    Save-Download 'https://dot.net/v1/dotnet-install.ps1' $script
    Write-Step 'Installing a private .NET 9 SDK and Windows Desktop runtime...'
    Invoke-Checked (Get-WindowsPowerShell) @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script,
        '-Channel', '9.0', '-Quality', 'GA', '-Architecture', 'x64', '-InstallDir', (Split-Path -Parent $managed), '-NoPath')
    if (-not (Test-DotNet9 $managed)) { throw 'The .NET 9 SDK / Windows Desktop runtime is unavailable after installation.' }
    return $managed
}

function Test-ArmGcc([string]$File) {
    if (-not $File -or -not (Test-Path -LiteralPath $File -PathType Leaf)) { return $false }
    try {
        $version = & $File --version 2>$null
        return ($LASTEXITCODE -eq 0 -and "$version" -match '12\.3\.1')
    }
    catch { return $false }
}

function Get-ArmGcc([string]$Base, [string]$Work) {
    $relative = 'tools\armgcc\xpack-arm-none-eabi-gcc-12.3.1-1.2\bin\arm-none-eabi-gcc.exe'
    $managed = Join-Path $Base $relative
    foreach ($file in @($managed, $env:HAYLOU_ARM_GCC, (Join-Path (Join-Path $env:LOCALAPPDATA 'HaylouFaceEditor') $relative))) {
        if (Test-ArmGcc $file) { Write-Step "Using ARM compiler: $file"; return $file }
    }
    $name = 'xpack-arm-none-eabi-gcc-12.3.1-1.2-win32-x64.zip'
    $url = 'https://github.com/xpack-dev-tools/arm-none-eabi-gcc-xpack/releases/download/v12.3.1-1.2/' + $name
    $archive = Join-Path $Work $name
    $checksum = Join-Path $Work 'arm.sha'
    Save-Download ($url + '.sha') $checksum
    $hash = ([IO.File]::ReadAllText($checksum).Trim() -split '\s+')[0]
    if ($hash -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid ARM compiler SHA-256 file.' }
    Save-Download $url $archive $hash
    $expanded = Join-Path $Work 'arm'
    Expand-CheckedArchive $archive $expanded
    $source = Join-Path $expanded 'xpack-arm-none-eabi-gcc-12.3.1-1.2'
    $target = Split-Path -Parent (Split-Path -Parent $managed)
    Assert-ChildPath $Base $target
    if (Test-Path -LiteralPath $target) { throw "Incomplete compiler directory: $target. Move it aside and rerun the installer." }
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Recurse
    if (-not (Test-ArmGcc $managed)) { throw 'ARM compiler validation failed.' }
    return $managed
}

function Install-Haylou {
    if (-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
        throw 'This installer supports Windows x64.'
    }
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $base = [IO.Path]::GetFullPath($InstallDirectory)
    if (-not [IO.Directory]::GetParent($base)) { throw 'Choose an installation folder, not a drive root.' }
    $existingSettings = Join-Path $base 'installation.json'
    if (Test-Path -LiteralPath $existingSettings) {
        $saved = Get-Content -LiteralPath $existingSettings -Raw | ConvertFrom-Json
        if (-not $saved.PSObject.Properties['ProjectRoot'] -or -not $saved.PSObject.Properties['SourceHash']) {
            throw 'Existing installation settings are not managed by RS4 Studio. Choose another folder.'
        }
        Assert-ChildPath $base $saved.ProjectRoot
        $savedMarker = Join-Path $saved.ProjectRoot '.haylou-managed.json'
        if (-not (Test-Path -LiteralPath $savedMarker) -or (Get-Content -LiteralPath $savedMarker -Raw | ConvertFrom-Json).SourceHash -ne $saved.SourceHash) {
            throw 'Existing installation settings do not match a managed RS4 Studio version. Choose another folder.'
        }
    }
    elseif (Test-Path -LiteralPath (Join-Path $base 'Start-Editor.ps1')) {
        throw 'An unmanaged Start-Editor.ps1 already exists. Choose another installation folder.'
    }
    New-Item -ItemType Directory -Path $base -Force | Out-Null
    $cache = Join-Path $base 'cache'
    $work = Join-Path $cache ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $transcript = $false
    try {
        Start-Transcript -Path (Join-Path $base 'install.log') -Append | Out-Null
        $transcript = $true
        Write-Step "Installing to $base"
        $archive = Join-Path $work 'source.zip'
        if ($SourceArchive) { Copy-Item -LiteralPath $SourceArchive -Destination $archive }
        else { Save-Download 'https://codeload.github.com/RikzDev/Haylou-rs4-plus-watchface-maker/zip/refs/heads/main' $archive }
        $sourceHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        $expanded = Join-Path $work 'source'
        Expand-CheckedArchive $archive $expanded
        $sourceRoots = @(Get-ChildItem -LiteralPath $expanded -Directory | Where-Object {
            Test-Path -LiteralPath (Join-Path $_.FullName 'editor\HaylouFaceEditor.csproj')
        })
        if ($sourceRoots.Count -ne 1) { throw 'The source archive does not contain one RS4 Studio project.' }
        $sourceRoot = $sourceRoots[0].FullName
        foreach ($required in @('requirements.txt', 'watchface\build_live.py', 'watchface\tools\ezip.exe')) {
            if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot $required) -PathType Leaf)) { throw "Source archive is missing $required." }
        }
        $app = Join-Path $base ('versions\' + $sourceHash.Substring(0, 16))
        Assert-ChildPath $base $app
        $marker = Join-Path $app '.haylou-managed.json'
        if (Test-Path -LiteralPath $app) {
            if (-not (Test-Path -LiteralPath $marker) -or (Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json).SourceHash -ne $sourceHash) {
                throw "The existing directory is not managed by this installer: $app"
            }
        }
        # Never terminate an open editor or replace files while it is editing.
        foreach ($process in @(Get-Process HaylouFaceEditor -ErrorAction SilentlyContinue)) {
            if ($process.Path -and $process.Path.StartsWith($app + '\', [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Save your project and close the installed editor before reinstalling.'
            }
        }
        foreach ($process in @(Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'")) {
            if ($process.CommandLine -and $process.CommandLine.IndexOf($app, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw 'Save your project and close the installed editor before reinstalling.'
            }
        }
        New-Item -ItemType Directory -Path $app -Force | Out-Null
        @{ SourceHash = $sourceHash } | ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding UTF8
        foreach ($item in Get-ChildItem -LiteralPath $sourceRoot -Force) {
            Copy-Item -LiteralPath $item.FullName -Destination $app -Recurse -Force
        }
        $python = Get-Python313 $base $work
        $dotnet = Get-DotNet9 $base $work
        $gcc = Get-ArmGcc $base $work
        $venv = Join-Path $app '.venv\Scripts\python.exe'
        if (-not (Test-Python313 $venv)) {
            Write-Step 'Creating the project Python environment...'
            Invoke-Checked $python @('-m', 'venv', (Join-Path $app '.venv'))
        }
        Write-Step 'Installing Python packages...'
        Invoke-Checked $venv @('-m', 'pip', 'install', '--disable-pip-version-check', '-r', (Join-Path $app 'requirements.txt'))
        $sdk = @(& $dotnet --list-sdks | Where-Object { $_ -match '^9\.0\.\d+ \[' } | ForEach-Object { [version](($_ -split ' ')[0]) } | Sort-Object)[-1].ToString()
        @{ sdk = @{ version = $sdk; rollForward = 'latestPatch' } } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $app 'global.json') -Encoding UTF8
        Write-Step 'Building the Release editor...'
        Push-Location -LiteralPath $app
        try { Invoke-Checked $dotnet @('build', 'editor\HaylouFaceEditor.csproj', '-c', 'Release', '--nologo') }
        finally { Pop-Location }
        $dll = Join-Path $app 'editor\bin\Release\net9.0-windows\HaylouFaceEditor.dll'
        if (-not (Test-Path -LiteralPath $dll)) { throw 'The Release editor was not produced.' }
        Invoke-Checked $venv @('-c', 'import PIL,bleak; from winrt.windows.devices.enumeration import DevicePairingProtectionLevel')
        $settings = @{ ProjectRoot = $app; Python = $venv; DotNet = $dotnet; ArmGcc = $gcc; Dll = $dll; SourceHash = $sourceHash }
        $settingsPath = Join-Path $base 'installation.json'
        $settings | ConvertTo-Json | Set-Content -LiteralPath ($settingsPath + '.tmp') -Encoding UTF8
        $launcher = Join-Path $base 'Start-Editor.ps1'
        @'
$ErrorActionPreference = 'Stop'
try {
    $settings = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installation.json') -Raw | ConvertFrom-Json
    $env:HAYLOU_PROJECT_ROOT = $settings.ProjectRoot
    $env:HAYLOU_PYTHON = $settings.Python
    $env:HAYLOU_ARM_GCC = $settings.ArmGcc
    $env:DOTNET_ROOT = Split-Path -Parent $settings.DotNet
    Start-Process -FilePath $settings.DotNet -ArgumentList ('"' + $settings.Dll + '"') -WorkingDirectory $settings.ProjectRoot -WindowStyle Hidden
}
catch {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show($_.Exception.Message, 'RS4 Studio launch failed') | Out-Null
}
'@ | Set-Content -LiteralPath $launcher -Encoding UTF8
        # Switch the launcher only after packages and the Release build succeed.
        Move-Item -LiteralPath ($settingsPath + '.tmp') -Destination $settingsPath -Force
        if (-not $NoShortcut) {
            $shell = New-Object -ComObject WScript.Shell
            $folders = @([Environment]::GetFolderPath('DesktopDirectory'), [Environment]::GetFolderPath('Programs'))
            foreach ($folder in $folders) {
                $link = $shell.CreateShortcut((Join-Path $folder 'RS4 Studio.lnk'))
                $link.TargetPath = Get-WindowsPowerShell
                $link.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $launcher + '"'
                $link.WorkingDirectory = $app
                $link.IconLocation = (Join-Path (Split-Path -Parent $dll) 'HaylouFaceEditor.exe') + ',0'
                $link.Save()
            }
        }
        Write-Host "Installed successfully. Launcher: $launcher" -ForegroundColor Green
        Write-Host 'For Bluetooth upload, configure HAYLOU_WATCH_ADDRESS as described in the installation guide.'
        if (-not $NoLaunch) {
            Start-Process -FilePath (Get-WindowsPowerShell) -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $launcher + '"') -WindowStyle Hidden
        }
    }
    finally {
        if ($transcript) { Stop-Transcript | Out-Null }
        # Only remove this invocation's own temporary files, never a version or project.
        Assert-ChildPath $cache $work
        if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
    }
}

if ($MyInvocation.InvocationName -ne '.') { Install-Haylou }
