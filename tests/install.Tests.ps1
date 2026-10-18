#requires -Version 5.1
# Run with: powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\install.Tests.ps1
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'install.ps1')

function Assert-True([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
function Assert-Throws([scriptblock]$Action, [string]$Pattern) {
    try { & $Action }
    catch {
        if ($_.Exception.Message -notmatch $Pattern) { throw }
        return
    }
    throw "Expected failure matching $Pattern"
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('haylou-installer-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
Assert-ChildPath $testRoot (Join-Path $testRoot 'child')
Assert-Throws { Assert-ChildPath $testRoot ($testRoot + '-sibling\file') } 'outside'
Assert-Throws { Assert-ChildPath $testRoot (Join-Path $testRoot '..\escape') } 'outside'

# Reject ZIP traversal before extraction creates any outside file.
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$badZip = Join-Path $testRoot 'bad.zip'
$zip = [IO.Compression.ZipFile]::Open($badZip, [IO.Compression.ZipArchiveMode]::Create)
$zip.CreateEntry('../escaped.txt') | Out-Null
$zip.Dispose()
Assert-Throws { Expand-CheckedArchive $badZip (Join-Path $testRoot 'bad-output') } 'outside'
Assert-True (-not (Test-Path (Join-Path $testRoot 'escaped.txt'))) 'Archive escaped its destination.'

# Exercise the complete coordinator without downloading or installing system tools.
$fixture = Join-Path $testRoot 'fixture'
$source = Join-Path $fixture 'RS4 Studio'
foreach ($folder in @('editor', 'watchface\tools')) { New-Item -ItemType Directory -Path (Join-Path $source $folder) -Force | Out-Null }
foreach ($file in @('editor\HaylouFaceEditor.csproj', 'watchface\build_live.py', 'watchface\tools\ezip.exe', 'requirements.txt')) {
    [IO.File]::WriteAllText((Join-Path $source $file), 'fixture')
}
$archive = Join-Path $testRoot 'fixture.zip'
Compress-Archive -LiteralPath $source -DestinationPath $archive
$fakeDotnet = Join-Path $testRoot 'fake-dotnet.ps1'
"Write-Output '9.0.313 [C:\fake\sdk]'" | Set-Content -LiteralPath $fakeDotnet
function Get-Python313 { return 'fake-python.exe' }
function Get-DotNet9 { return $fakeDotnet }
function Get-ArmGcc { return 'fake-arm-gcc.exe' }
function Invoke-Checked([string]$File, [string[]]$Arguments) {
    if ($Arguments[0] -eq 'build') {
        if ($script:FailBuild) { throw 'Simulated build failure' }
        $bin = Join-Path $PWD 'editor\bin\Release\net9.0-windows'
        New-Item -ItemType Directory -Path $bin -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $bin 'HaylouFaceEditor.dll'), 'release fixture')
    }
}
$InstallDirectory = Join-Path $testRoot 'Install with spaces'
$SourceArchive = $archive
$NoShortcut = $true
$NoLaunch = $true
$script:FailBuild = $false
Install-Haylou
$statePath = Join-Path $InstallDirectory 'installation.json'
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
Assert-True (Test-Path -LiteralPath $state.Dll) 'Release artifact missing.'
$project = Join-Path $state.ProjectRoot 'my project.hface.json'
[IO.File]::WriteAllText($project, 'do not overwrite')
Install-Haylou
Assert-True ([IO.File]::ReadAllText($project) -eq 'do not overwrite') 'Reinstall changed user data.'
Assert-True (@(Get-ChildItem (Join-Path $InstallDirectory 'versions') -Directory).Count -eq 1) 'Same archive created another version.'

# A failed update must leave the existing launcher target and projects intact.
$oldState = [IO.File]::ReadAllText($statePath)
[IO.File]::WriteAllText((Join-Path $source 'requirements.txt'), 'second fixture')
$archive2 = Join-Path $testRoot 'second.zip'
Compress-Archive -LiteralPath $source -DestinationPath $archive2
$SourceArchive = $archive2
$script:FailBuild = $true
Assert-Throws { Install-Haylou } 'Simulated build failure'
Assert-True ([IO.File]::ReadAllText($statePath) -eq $oldState) 'Failed build changed the active installation.'
Assert-True ([IO.File]::ReadAllText($project) -eq 'do not overwrite') 'Failed update changed user data.'
Assert-True (@(Get-ChildItem (Join-Path $InstallDirectory 'cache') -Directory).Count -eq 0) 'Temporary install files were not cleaned.'

# Refuse to overwrite a pre-existing directory which is not installer-managed.
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$InstallDirectory = Join-Path $testRoot 'Unmanaged'
$existing = Join-Path $InstallDirectory ('versions\' + $hash.Substring(0, 16))
New-Item -ItemType Directory -Path $existing -Force | Out-Null
$sentinel = Join-Path $existing 'keep.txt'
[IO.File]::WriteAllText($sentinel, 'original')
$SourceArchive = $archive
Assert-Throws { Install-Haylou } 'not managed'
Assert-True ([IO.File]::ReadAllText($sentinel) -eq 'original') 'Unmanaged directory changed.'
$InstallDirectory = Join-Path $testRoot 'Conflicting launcher'
New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
$existingLauncher = Join-Path $InstallDirectory 'Start-Editor.ps1'
[IO.File]::WriteAllText($existingLauncher, 'user script')
Assert-Throws { Install-Haylou } 'unmanaged Start-Editor'
Assert-True ([IO.File]::ReadAllText($existingLauncher) -eq 'user script') 'Unmanaged launcher was overwritten.'
$InstallDirectory = [IO.Path]::GetPathRoot($testRoot)
Assert-Throws { Install-Haylou } 'drive root'
Write-Host 'PASS: path boundaries, archive traversal, install/reinstall with spaces, project preservation, failed-update recovery, cleanup and unmanaged-directory/launcher protection.' -ForegroundColor Green
