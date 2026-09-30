[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$ModelDirectory,
  [string]$RuntimeDirectory,
  [string]$PythonPath = (Join-Path $env:LOCALAPPDATA 'DictateAnywhere\local-model-runtime\.venv\Scripts\python.exe'),
  [ValidateSet('automatic', 'cpu', 'vulkan')][string]$Backend = 'automatic',
  [ValidateRange(1, 64)][int]$Threads = [Math]::Max(1, [Math]::Min(4, [Environment]::ProcessorCount - 2)),
  [string]$DeviceKey,
  [switch]$Recalibrate
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'prepare-cohere-python-runtime.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Python/native prerequisite preparation failed. Resolve the displayed error and retry.' }
if (-not (Test-Path -LiteralPath $PythonPath)) { throw 'The selected Python runtime is missing. Correct PythonPath and retry.' }
if (-not $RuntimeDirectory) {
  $folder = if ($Backend -eq 'automatic') { 'cohere-auto' } else { 'cohere-native' }
  $RuntimeDirectory = Join-Path $env:LOCALAPPDATA "DictateAnywhere\$folder\0.2.4"
}
$runtimeRoot = [IO.Path]::GetFullPath($RuntimeDirectory)
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\') + '\'
if ($runtimeRoot.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase) -and
    -not $runtimeRoot.StartsWith((Join-Path $repoRoot 'artifacts\'), [StringComparison]::OrdinalIgnoreCase)) {
  throw 'Runtime manifests, binaries, recordings and weights must stay under artifacts/ or outside Git.'
}
$arguments = @('-u', (Join-Path $PSScriptRoot 'local-models\prepare_cohere_runtime.py'),
  '--model-dir', (Resolve-Path -LiteralPath $ModelDirectory).Path, '--runtime-dir', $runtimeRoot)
if ($Backend -ne 'automatic') {
  $arguments += @('--provision-only', '--backend', $Backend, '--threads', [string]$Threads)
  if ($DeviceKey) { $arguments += @('--device-key', $DeviceKey) }
}
if ($Recalibrate) { $arguments += '--recalibrate' }
& $PythonPath @arguments
if ($LASTEXITCODE -ne 0) { throw 'Runtime preparation failed. Retry after resolving the displayed error; the original provider remains available.' }
if ($Backend -ne 'automatic') {
  Write-Output "Manual override: set DICTATEANYWHERE_COHERE_NATIVE_MANIFEST to $(Join-Path $runtimeRoot 'native-manifest.json') for one launch."
}
