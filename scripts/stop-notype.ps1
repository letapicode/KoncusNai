[CmdletBinding()]
param(
  [switch]$AllOllama,
  [ValidateRange(0, 30)]
  [int]$GracePeriodSeconds = 2
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Stop-ProcessTree {
  param([Parameter(Mandatory)][int]$ProcessId)

  $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $ProcessId" -ErrorAction SilentlyContinue)
  foreach ($child in $children) {
    Stop-ProcessTree -ProcessId ([int]$child.ProcessId)
  }

  $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
  if ($null -ne $process) {
    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
  }
}

function Test-OwnedOllamaMarker {
  param(
    [Parameter(Mandatory)]$Marker,
    [Parameter(Mandatory)]$Process
  )

  $expectedName = [System.IO.Path]::GetFileName([string]$Marker.executablePath)
  if ($expectedName -notin @("ollama", "ollama.exe") -or $Process.ProcessName -ne "ollama") {
    return $false
  }

  $recordedStart = ([DateTime]$Marker.startTimeUtc).ToUniversalTime()
  $actualStart = $Process.StartTime.ToUniversalTime()
  return [Math]::Abs(($actualStart - $recordedStart).TotalSeconds) -lt 2
}

$notypeProcesses = @(Get-Process -Name "DictateAnywhere.App" -ErrorAction SilentlyContinue)
foreach ($process in $notypeProcesses) {
  if ($process.MainWindowHandle -ne 0) {
    [void]$process.CloseMainWindow()
  }
}

if ($GracePeriodSeconds -gt 0 -and $notypeProcesses.Count -gt 0) {
  Start-Sleep -Seconds $GracePeriodSeconds
}

foreach ($process in $notypeProcesses) {
  if ($null -ne (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) {
    Stop-ProcessTree -ProcessId $process.Id
  }
}

$markerPath = Join-Path $env:LOCALAPPDATA "DictateAnywhere\runtime\owned-ollama.pid"
if ($AllOllama) {
  foreach ($process in @(Get-Process -Name "ollama" -ErrorAction SilentlyContinue)) {
    Stop-ProcessTree -ProcessId $process.Id
  }
} elseif (Test-Path -LiteralPath $markerPath) {
  try {
    $marker = Get-Content -Raw -LiteralPath $markerPath | ConvertFrom-Json
    $owned = Get-Process -Id ([int]$marker.processId) -ErrorAction SilentlyContinue
    if ($null -ne $owned -and (Test-OwnedOllamaMarker -Marker $marker -Process $owned)) {
      Stop-ProcessTree -ProcessId $owned.Id
    }
  } finally {
    Remove-Item -LiteralPath $markerPath -Force -ErrorAction SilentlyContinue
  }
}

Write-Host "Stopped $($notypeProcesses.Count) Koncus Nai process(es)." -ForegroundColor Green
if ($AllOllama) {
  Write-Host "All local Ollama processes were explicitly stopped." -ForegroundColor Yellow
} else {
  Write-Host "Only a KoncusNai-owned Ollama process was eligible for shutdown." -ForegroundColor Cyan
}
