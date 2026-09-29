[CmdletBinding()]
param(
  [Parameter(Mandatory)][int]$AppProcessId,
  [Parameter(Mandatory)][string]$OutputPath,
  [ValidateRange(1, 600)][int]$DurationSeconds = 60
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\') + '\'
$output = [IO.Path]::GetFullPath($OutputPath)
if ($output.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase) -and
    -not $output.StartsWith((Join-Path $repo 'artifacts\'), [StringComparison]::OrdinalIgnoreCase)) {
  throw 'Probe results must stay under ignored artifacts/ or outside Git.'
}
$app = Get-Process -Id $AppProcessId
if ($app.ProcessName -ne 'DictateAnywhere.App') { throw 'Probe target is not Koncus Nai.' }
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DictationUiLatencyProbe {
  [DllImport("user32.dll", SetLastError=true)]
  public static extern IntPtr SendMessageTimeout(IntPtr handle, uint msg, UIntPtr wparam,
    IntPtr lparam, uint flags, uint timeout, out UIntPtr result);
}
'@
$rows = [Collections.Generic.List[object]]::new()
$clock = [Diagnostics.Stopwatch]::StartNew()
while ($clock.Elapsed.TotalSeconds -lt $DurationSeconds) {
  $app.Refresh()
  if ($app.HasExited) { break }
  $handle = $app.MainWindowHandle
  if ($handle -ne [IntPtr]::Zero) {
    $tick = [Diagnostics.Stopwatch]::StartNew()
    [UIntPtr]$result = [UIntPtr]::Zero
    # WM_NULL is a no-op processed by the window's UI thread. ABORTIFHUNG with
    # a 100 ms bound measures message-pump liveness, not animation frame rate.
    $responded = [DictationUiLatencyProbe]::SendMessageTimeout($handle, 0, [UIntPtr]::Zero, [IntPtr]::Zero, 2, 100, [ref]$result) -ne [IntPtr]::Zero
    $tick.Stop()
    $rows.Add([pscustomobject]@{ timestampUtc=[datetimeoffset]::UtcNow; elapsedMs=$tick.Elapsed.TotalMilliseconds; responded=$responded })
  }
  Start-Sleep -Milliseconds 100
}
New-Item -ItemType Directory (Split-Path $output -Parent) -Force | Out-Null
@{ metric='WM_NULL round-trip to application UI thread; not frame latency'; samples=$rows } |
  ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $output -Encoding utf8
Write-Output "UI probe recorded $($rows.Count) samples."
