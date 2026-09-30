[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratch = Join-Path $root ('artifacts\release-audit\payload-fixture-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
foreach ($name in @('DictateAnywhere.App.exe', 'DictateAnywhere.UiAccessHelper.exe')) {
  [IO.File]::WriteAllText((Join-Path $scratch $name), 'synthetic non-executable fixture')
}
$validator = Join-Path $PSScriptRoot 'validate-installer-payload.ps1'
& $validator -PayloadDirectory $scratch
foreach ($relative in @('local-models\__pycache__\worker.pyc', 'private.pfx', '.env', 'credentials.secrets.json', 'local-models\test_fixture.py', 'private.log')) {
  $path = Join-Path $scratch $relative
  New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($path)) -Force | Out-Null
  [IO.File]::WriteAllText($path, 'synthetic fixture only')
  $rejected = $false
  try { & $validator -PayloadDirectory $scratch }
  catch { $rejected = $_.Exception.Message -like '*forbidden in the installer payload*' }
  Remove-Item -LiteralPath $path
  if (-not $rejected) { throw "Payload validator accepted forbidden fixture: $relative" }
}
Write-Host 'Payload positive case and six contamination rejection cases passed.'
  [IO.File]::WriteAllText((Join-Path $scratch 'local-models/cohere_transcribe_worker.py'), 'synthetic worker')
  $bootstrapRejected = $false
  try { & $validator -PayloadDirectory $scratch }
  catch { $bootstrapRejected = $_.Exception.Message -like '*deployment helper is missing*' }
  if (-not $bootstrapRejected) { throw 'Missing Cohere Python bootstrap was accepted.' }
  [IO.File]::WriteAllText((Join-Path $scratch 'prepare-cohere-python-runtime.ps1'), 'synthetic bootstrap')
foreach ($helper in @('cohere_native_runtime.py', 'cohere_runtime_selection.py', 'cohere_quantize.py', 'prepare_cohere_runtime.py')) {
  $helperPath = Join-Path $scratch "local-models/$helper"
  $missingRejected = $false
  try { & $validator -PayloadDirectory $scratch }
  catch { $missingRejected = $_.Exception.Message -like '*deployment helper is missing*' }
  if (-not $missingRejected) { throw 'Incomplete Cohere helper deployment was accepted.' }
  [IO.File]::WriteAllText($helperPath, 'synthetic helper')
}
& $validator -PayloadDirectory $scratch
New-Item -ItemType Directory -Path (Join-Path $scratch 'local-models/fixtures') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $scratch 'local-models/fixtures/cohere-healthcheck-jfk.wav'), 'synthetic fixture')
$retiredFixtureRejected = $false
try { & $validator -PayloadDirectory $scratch }
catch { $retiredFixtureRejected = $_.Exception.Message -like '*audio fixtures must not be included*' }
if (-not $retiredFixtureRejected) { throw 'Retired Cohere fixture was accepted.' }
Write-Host 'Cohere worker readiness and retired-fixture checks passed.'
