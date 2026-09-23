[CmdletBinding()]
param(
  [ValidateSet("Auto", "Cpu", "Cuda", "Rocm")]
  [string]$TorchBackend = "Auto",
  [string]$TorchIndexUrl,
  [string]$RuntimeRoot,
  [string]$SourceRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$pythonVersion = "3.11.9"
$pythonInstallerUrl = "https://www.python.org/ftp/python/$pythonVersion/python-$pythonVersion-amd64.exe"
$minimumFreeBytes = 12GB

function Get-FileSha256([string]$Path) {
  $stream = [IO.File]::OpenRead($Path)
  $sha = [Security.Cryptography.SHA256]::Create()
  try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace("-", "") }
  finally { $sha.Dispose(); $stream.Dispose() }
}

function Get-TextSha256([string]$Text) {
  $sha = [Security.Cryptography.SHA256]::Create()
  try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace("-", "") }
  finally { $sha.Dispose() }
}

function Resolve-SourceRoot {
  if ($SourceRoot) { return [IO.Path]::GetFullPath($SourceRoot) }
  $scriptRoot = Split-Path -Parent $MyInvocation.ScriptName
  $published = Join-Path $scriptRoot "compat"
  if (Test-Path -LiteralPath (Join-Path $published "MANIFEST.sha256")) { return $published }
  return [IO.Path]::GetFullPath((Join-Path $scriptRoot "..\third_party\compat"))
}

function Assert-NoReparsePath([string]$Root, [string]$Path) {
  $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
  $cursor = [IO.Path]::GetFullPath($Path)
  if (-not [string]::Equals($cursor, $resolvedRoot, [StringComparison]::OrdinalIgnoreCase) -and
      -not $cursor.StartsWith($resolvedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "An Indic compatibility path escaped its approved directory."
  }
  while ($true) {
    if (Test-Path -LiteralPath $cursor) {
      $item = Get-Item -LiteralPath $cursor -Force
      if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "An Indic compatibility path contains an unsupported reparse point."
      }
    }
    if ([string]::Equals($cursor, $resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) { break }
    $cursor = Split-Path -Parent $cursor
    if ([string]::IsNullOrWhiteSpace($cursor)) { throw "An Indic compatibility path is invalid." }
  }
}

function Test-CompatManifest([string]$Root) {
  $manifest = Join-Path $Root "MANIFEST.sha256"
  if (-not (Test-Path -LiteralPath $manifest)) { throw "The Indic compatibility-source manifest is missing." }
  Assert-NoReparsePath -Root $Root -Path $manifest
  foreach ($line in Get-Content -LiteralPath $manifest) {
    if ($line -notmatch '^([0-9a-f]{64})  ([A-Za-z0-9._/-]+)$') { throw "The Indic compatibility-source manifest is malformed." }
    $relative = $Matches[2].Replace('/', [IO.Path]::DirectorySeparatorChar)
    if ($relative -match '(^|[\\/])(build|[^\\/]+\.egg-info)([\\/]|$)') { throw "The Indic compatibility-source manifest includes generated build metadata." }
    $candidate = [IO.Path]::GetFullPath((Join-Path $Root $relative))
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    if (-not $candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
      throw "An Indic compatibility source is missing or outside its approved directory."
    }
    Assert-NoReparsePath -Root $Root -Path $candidate
    if ((Get-FileSha256 $candidate) -ne $Matches[1].ToUpperInvariant()) { throw "An Indic compatibility source failed integrity verification." }
  }
}

function Copy-VerifiedCompatSources([string]$SourceRoot, [string]$DestinationRoot) {
  $manifest = Join-Path $SourceRoot 'MANIFEST.sha256'
  $sourceBase = [IO.Path]::GetFullPath($SourceRoot).TrimEnd('\') + '\'
  $destinationBase = [IO.Path]::GetFullPath($DestinationRoot).TrimEnd('\') + '\'
  New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null
  foreach ($line in Get-Content -LiteralPath $manifest) {
    if ($line -notmatch '^([0-9a-f]{64})  ([A-Za-z0-9._/-]+)$') { throw "The Indic compatibility-source manifest is malformed." }
    $expectedHash = $Matches[1].ToUpperInvariant()
    $relative = $Matches[2].Replace('/', [IO.Path]::DirectorySeparatorChar)
    if ($relative -match '(^|[\\/])(build|[^\\/]+\.egg-info)([\\/]|$)') { throw "The Indic compatibility-source manifest includes generated build metadata." }
    $source = [IO.Path]::GetFullPath((Join-Path $SourceRoot $relative))
    $destination = [IO.Path]::GetFullPath((Join-Path $DestinationRoot $relative))
    if (-not $source.StartsWith($sourceBase, [StringComparison]::OrdinalIgnoreCase) -or
        -not $destination.StartsWith($destinationBase, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $source -PathType Leaf)) {
      throw "An Indic compatibility source is missing or outside its approved directory."
    }
    Assert-NoReparsePath -Root $SourceRoot -Path $source
    $destinationDirectory = Split-Path -Parent $destination
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
    if ((Get-FileSha256 $destination) -ne $expectedHash) {
      throw "An Indic compatibility source failed staged integrity verification."
    }
  }
}

function Install-ParlerPython([string]$Root) {
  $baseRoot = Join-Path $Root "python-base"
  $basePython = Join-Path $baseRoot "python.exe"
  if (Test-Path -LiteralPath $basePython) { return $basePython }
  $downloadRoot = Join-Path $Root "downloads"
  $installerPath = Join-Path $downloadRoot "python-$pythonVersion-amd64.exe"
  New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
  try {
    Invoke-WebRequest -Uri $pythonInstallerUrl -OutFile $installerPath -UseBasicParsing
    $signature = Get-AuthenticodeSignature -LiteralPath $installerPath
    $versionInfo = (Get-Item -LiteralPath $installerPath).VersionInfo
    if ($signature.Status -ne "Valid" -or $signature.SignerCertificate.Subject -notmatch "(^|, )O=Python Software Foundation(,|$)" -or $versionInfo.ProductVersion -notlike "3.11.9*") {
      throw "The downloaded Python runtime failed publisher or version verification."
    }
    & $installerPath /quiet InstallAllUsers=0 TargetDir=$baseRoot Include_pip=1 Include_launcher=0 Include_test=0 Include_doc=0 Include_tcltk=0 Shortcuts=0 AssociateFiles=0 PrependPath=0
    if ($LASTEXITCODE -ne 0) { throw "Automatic Python installation failed." }
  } finally { Remove-Item -LiteralPath $installerPath -Force -ErrorAction SilentlyContinue }
  if (-not (Test-Path -LiteralPath $basePython)) { throw "Python installation did not produce a usable executable." }
  return $basePython
}

function Resolve-Python([string]$Root) {
  $managed = Join-Path $Root "python-base\python.exe"
  if (Test-Path -LiteralPath $managed) { return [PSCustomObject]@{ Command=$managed; Arguments=@() } }
  if (Get-Command py -ErrorAction SilentlyContinue) {
    & py -3.11 -c "import sys" *> $null
    if ($LASTEXITCODE -eq 0) { return [PSCustomObject]@{ Command="py"; Arguments=@("-3.11") } }
  }
  if (Get-Command python -ErrorAction SilentlyContinue) {
    & python -c "import sys; raise SystemExit(0 if sys.version_info[:2] == (3, 11) else 1)" *> $null
    if ($LASTEXITCODE -eq 0) { return [PSCustomObject]@{ Command="python"; Arguments=@() } }
  }
  return [PSCustomObject]@{ Command=(Install-ParlerPython $Root); Arguments=@() }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $RuntimeRoot) { $RuntimeRoot = Join-Path $env:LOCALAPPDATA "DictateAnywhere\indic-parler-runtime" }
$RuntimeRoot = [IO.Path]::GetFullPath($RuntimeRoot)
$compatRoot = Resolve-SourceRoot
$requirementsPath = Join-Path $scriptRoot "local-models\requirements-indic-parler-lock.txt"
$workerPath = Join-Path $scriptRoot "local-models\indic_parler_tts_worker.py"
$torchLockPath = Join-Path $scriptRoot "local-models\requirements-indic-parler-torch-cpu-lock.txt"
if ($TorchBackend -eq "Cuda") {
  if ($TorchIndexUrl) { throw "Custom package indexes are not allowed for this pinned runtime." }
  $torchLockPath = Join-Path $scriptRoot "local-models\requirements-indic-parler-torch-cuda-lock.txt"
} elseif ($TorchBackend -eq "Rocm") { throw "ROCm is not available in the verified Windows runtime." }
foreach ($required in @($requirementsPath,$workerPath,$torchLockPath)) { if (-not (Test-Path -LiteralPath $required)) { throw "A bundled Indic runtime component is missing." } }
Test-CompatManifest $compatRoot

New-Item -ItemType Directory -Path $RuntimeRoot -Force | Out-Null
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($RuntimeRoot))
if ($drive.AvailableFreeSpace -lt $minimumFreeBytes) { throw "Indic voice setup needs at least 12 GB of free disk space." }

$mutex = [Threading.Mutex]::new($false, "Local\KoncusNai.IndicParlerProvisioning")
$lockTaken = $false
try {
  $lockTaken = $mutex.WaitOne([TimeSpan]::FromMinutes(45))
  if (-not $lockTaken) { throw "Another Indic voice setup is still running." }
  Get-ChildItem -LiteralPath $RuntimeRoot -Directory -Filter '.staging-*' -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTimeUtc -lt [DateTime]::UtcNow.AddDays(-1) } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

  $stagingRoot = Join-Path $RuntimeRoot ('.staging-' + [Guid]::NewGuid().ToString('N'))
  $stagingVenv = Join-Path $stagingRoot '.venv'
  New-Item -ItemType Directory -Path $stagingRoot | Out-Null
  $stagedCompatRoot = Join-Path $stagingRoot 'compat-source'
  Copy-VerifiedCompatSources -SourceRoot $compatRoot -DestinationRoot $stagedCompatRoot
  $launcher = Resolve-Python $RuntimeRoot
  & $launcher.Command @($launcher.Arguments) -m venv $stagingVenv
  if ($LASTEXITCODE -ne 0) { throw "Failed to create the candidate Indic voice runtime." }
  $candidatePython = Join-Path $stagingVenv 'Scripts\python.exe'
  & $candidatePython -m pip install --require-hashes --no-deps -r $torchLockPath
  if ($LASTEXITCODE -ne 0) { throw "Failed to install the selected PyTorch runtime." }
  & $candidatePython -m pip install --require-hashes --no-deps --no-build-isolation -r $requirementsPath
  if ($LASTEXITCODE -ne 0) { throw "Failed to install pinned Indic voice dependencies." }
  & $candidatePython -m pip install --no-deps --no-build-isolation (Join-Path $stagedCompatRoot 'audiotools') (Join-Path $stagedCompatRoot 'descript-audio-codec') (Join-Path $stagedCompatRoot 'parler-tts')
  if ($LASTEXITCODE -ne 0) { throw "Failed to install the verified Indic compatibility packages." }
  & $candidatePython -m pip check
  if ($LASTEXITCODE -ne 0) { throw "The candidate Indic voice dependency graph is inconsistent." }
  & $candidatePython -c "import torch, parler_tts, transformers, accelerate, safetensors, soundfile; assert transformers.__version__ == '5.17.0'; assert parler_tts.__version__ == '0.2.2+koncus1'"
  if ($LASTEXITCODE -ne 0) { throw "The candidate Indic voice runtime failed its health check." }

  $stampMaterial = @((Get-FileSha256 $requirementsPath),(Get-FileSha256 $torchLockPath),(Get-FileSha256 $workerPath),(Get-FileSha256 (Join-Path $compatRoot 'MANIFEST.sha256'))) -join "`n"
  $stampHash = Get-TextSha256 $stampMaterial
  $currentVenv = Join-Path $RuntimeRoot '.venv'
  $previousVenv = Join-Path $RuntimeRoot '.venv.previous'
  if (Test-Path -LiteralPath $previousVenv) { Remove-Item -LiteralPath $previousVenv -Recurse -Force }
  $movedCurrent = $false
  try {
    if (Test-Path -LiteralPath $currentVenv) { Move-Item -LiteralPath $currentVenv -Destination $previousVenv; $movedCurrent = $true }
    Move-Item -LiteralPath $stagingVenv -Destination $currentVenv
    $stampHash | Set-Content -LiteralPath (Join-Path $RuntimeRoot '.requirements.sha256') -NoNewline
  } catch {
    if (-not (Test-Path -LiteralPath $currentVenv) -and $movedCurrent -and (Test-Path -LiteralPath $previousVenv)) { Move-Item -LiteralPath $previousVenv -Destination $currentVenv -ErrorAction SilentlyContinue }
    throw
  } finally { if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue } }
  Write-Host "Indic Parler-TTS runtime is ready." -ForegroundColor Green
} finally {
  if (Get-Variable stagingRoot -ErrorAction SilentlyContinue) {
    if (Test-Path -LiteralPath $stagingRoot) {
      Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
  }
  if ($lockTaken) { $mutex.ReleaseMutex() }
  $mutex.Dispose()
}
