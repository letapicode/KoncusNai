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
    Stop-Process -InputObject $process -Force -ErrorAction SilentlyContinue
  }
}

# A persisted PID cannot prove launch ownership. Ordinary cleanup leaves Ollama
# and all legacy marker contents untouched. Only the running app owns its handles.
if ($AllOllama) {
  foreach ($process in @(Get-Process -Name "ollama" -ErrorAction SilentlyContinue)) {
    Stop-ProcessTree -ProcessId $process.Id
  }
}

Write-Host "Stopped $($notypeProcesses.Count) Koncus Nai process(es)." -ForegroundColor Green
if ($AllOllama) {
  Write-Host "All local Ollama processes were explicitly stopped." -ForegroundColor Yellow
} else {
  Write-Host "Ollama was left untouched; forced app exit cannot guarantee owned-worker cleanup." -ForegroundColor Cyan
}
