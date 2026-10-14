param(
    [int]$IntervalSeconds = 3
)

$ErrorActionPreference = 'Stop'
$adb = (Get-Command adb -ErrorAction Stop).Source
$remoteCache = '/sdcard/Android/data/com.yingsheng.hayloufun/cache'
$projectRoot = Split-Path -Parent $PSScriptRoot
$incoming = Join-Path $projectRoot 'incoming'
$log = Join-Path $PSScriptRoot 'haylou-cache-monitor.log'
$knownFile = Join-Path $PSScriptRoot 'haylou-cache-monitor-known.json'
$python = Join-Path $projectRoot '.venv\Scripts\python.exe'
if ($env:HAYLOU_PYTHON) {
    $python = $env:HAYLOU_PYTHON
} elseif (-not (Test-Path -LiteralPath $python)) {
    $python = (Get-Command python -ErrorAction Stop).Source
}
$inspector = Join-Path $PSScriptRoot 'rs4face.py'

New-Item -ItemType Directory -Force -Path $incoming | Out-Null

function Get-RemoteFaces {
    $lines = & $adb shell 'for f in /sdcard/Android/data/com.yingsheng.hayloufun/cache/*; do [ -f "$f" ] && echo "$(basename "$f") $(stat -c%s "$f")"; done'
    foreach ($line in $lines) {
        if ($line -match '^([0-9a-f]{32}\.0)\s+(\d+)$') {
            [pscustomobject]@{ Name = $Matches[1]; Size = [long]$Matches[2] }
        }
    }
}

function Log([string]$message) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $message"
    Add-Content -LiteralPath $log -Value $line
}

$known = @{}
if (Test-Path $knownFile) {
    try {
        (Get-Content -LiteralPath $knownFile -Raw | ConvertFrom-Json).PSObject.Properties |
            ForEach-Object { $known[$_.Name] = [long]$_.Value }
    } catch {
        Log "estado anterior ignorado: $($_.Exception.Message)"
    }
}

if ($known.Count -eq 0) {
    foreach ($item in Get-RemoteFaces) {
        $known[$item.Name] = $item.Size
    }
    $known | ConvertTo-Json | Set-Content -LiteralPath $knownFile -Encoding utf8
    Log "monitor iniciado; $($known.Count) arquivos existentes marcados como conhecidos"
} else {
    Log "monitor retomado; $($known.Count) arquivos conhecidos"
}

$pending = @{}
while ($true) {
    try {
        foreach ($item in Get-RemoteFaces) {
            if ($known.ContainsKey($item.Name)) {
                continue
            }
            $signature = "$($item.Name):$($item.Size)"
            $observations = 0
            if ($pending.ContainsKey($signature)) {
                $observations = [int]$pending[$signature]
            }
            $pending[$signature] = 1 + $observations
            if ($pending[$signature] -lt 2) {
                continue
            }
            $destination = Join-Path $incoming $item.Name
            Log "nova face detectada: $($item.Name) ($($item.Size) bytes)"
            & $adb pull "$remoteCache/$($item.Name)" $destination | Out-Null
            if ((Test-Path $destination) -and (Get-Item $destination).Length -eq $item.Size) {
                $known[$item.Name] = $item.Size
                $known | ConvertTo-Json | Set-Content -LiteralPath $knownFile -Encoding utf8
                Log "capturada em $destination"
                & $python -X utf8 $inspector info $destination 2>&1 |
                    ForEach-Object { Add-Content -LiteralPath $log -Value "  $_" }
            } else {
                Log "captura incompleta; vou tentar novamente: $($item.Name)"
            }
        }
    } catch {
        Log "erro no monitor: $($_.Exception.Message)"
    }
    Start-Sleep -Seconds $IntervalSeconds
}
