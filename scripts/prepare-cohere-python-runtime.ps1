[CmdletBinding()]
param([string]$RuntimeRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if (-not [Environment]::Is64BitProcess -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
  throw 'Automatic local model runtime preparation requires Windows x64.'
}
if (-not $RuntimeRoot) { $RuntimeRoot = Join-Path $env:LOCALAPPDATA 'DictateAnywhere\local-model-runtime' }
$RuntimeRoot = [IO.Path]::GetFullPath($RuntimeRoot)
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ((Test-Path -LiteralPath (Join-Path $repositoryRoot '.git')) -and
    $RuntimeRoot.StartsWith($repositoryRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -and
    -not $RuntimeRoot.StartsWith((Join-Path $repositoryRoot 'artifacts\'), [StringComparison]::OrdinalIgnoreCase)) {
  throw 'Private runtime files must stay under ignored artifacts/ or outside the checkout.'
}
$requirementsPath = Join-Path $PSScriptRoot 'local-models\requirements-lock.txt'
if (-not (Test-Path -LiteralPath $requirementsPath)) { throw 'The bundled local-model requirements lock is missing. Repair the installation.' }
New-Item -ItemType Directory -Path $RuntimeRoot -Force | Out-Null
$verify = @'
import importlib.metadata as m, pathlib, re, sys
expected = re.findall(r'^([A-Za-z0-9._-]+)==([^\s]+)', pathlib.Path(sys.argv[1]).read_text(encoding='utf-8-sig'), re.M)
try:
    assert sys.version_info[:2] == (3, 11)
    assert all(m.version(name) == version for name, version in expected)
    import torch, transformers, psutil, numpy, sentencepiece
    assert hasattr(transformers, 'CohereAsrForConditionalGeneration')
except Exception:
    raise SystemExit(1)
'@

$lockRoot = Join-Path $env:LOCALAPPDATA 'DictateAnywhere\cohere-auto\0.2.4'
New-Item -ItemType Directory -Path $lockRoot -Force | Out-Null
$leases = [Collections.Generic.List[IO.FileStream]]::new()
function Ensure-NativeCrt {
  if (-not ('CohereCrtProbe' -as [type])) {
  Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CohereCrtProbe {
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr LoadLibraryW(string path);
  [DllImport("kernel32.dll")] public static extern bool FreeLibrary(IntPtr handle);
}
'@
  }
  $usable = $true
  foreach ($name in @('msvcp140.dll','vcruntime140.dll','vcruntime140_1.dll')) {
    $handle = [CohereCrtProbe]::LoadLibraryW((Join-Path ([Environment]::SystemDirectory) $name))
    if ($handle -eq [IntPtr]::Zero) { $usable = $false }
    else { [void][CohereCrtProbe]::FreeLibrary($handle) }
  }
  if ($usable) { return }
  Write-Output 'Preparing Microsoft native runtime; installation may require administrator rights'
  $installer = Join-Path $RuntimeRoot 'vc_redist-14.51.36247-x64.exe'
  $digest = '843068991daaa1f73ad9f6239bce4d0f6a07a51f18c37ea2a867e9beca71295c'
  if (-not (Test-Path -LiteralPath $installer) -or (Get-FileHash -LiteralPath $installer).Hash -ne $digest) {
    $partial = $installer + '.partial'
    Invoke-WebRequest -Uri 'https://download.visualstudio.microsoft.com/download/pr/ebdab8e5-1d7b-4d9f-a11b-cbb1720c3b12/843068991DAAA1F73AD9F6239BCE4D0F6A07A51F18C37EA2A867E9BECA71295C/VC_redist.x64.exe' -OutFile $partial -UseBasicParsing
    if ((Get-FileHash -LiteralPath $partial).Hash -ne $digest) { throw 'Microsoft native runtime checksum mismatch. Retry preparation.' }
    Move-Item -LiteralPath $partial -Destination $installer -Force
  }
  $signature = Get-AuthenticodeSignature -LiteralPath $installer
  if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|, )O=Microsoft Corporation(,|$)') {
    throw 'Microsoft native runtime publisher verification failed.'
  }
  # Never auto-elevate or leave an elevated child beyond the cancellable preparation owner.
  $start = [Diagnostics.ProcessStartInfo]::new($installer)
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.Arguments = '/install /quiet /norestart'
  try {
    $installation = [Diagnostics.Process]::Start($start)
    try {
      $installation.WaitForExit()
      if ($installation.ExitCode -eq 3010) { throw 'Microsoft native runtime installed. Restart Windows, then retry preparation.' }
      if ($installation.ExitCode -notin @(0,1638)) { throw 'Install the verified Microsoft Visual C++ x64 runtime with administrator rights, then retry preparation.' }
    } finally { $installation.Dispose() }
  } catch [ComponentModel.Win32Exception] {
    throw 'Windows requires administrator rights for Microsoft Visual C++ runtime setup. Run preparation as an administrator, then retry in Koncus Nai.'
  }
}
try {
  foreach ($name in @('preparation.lock', 'inference.lock')) {
    $lease = [IO.File]::Open((Join-Path $lockRoot $name), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::ReadWrite)
    try { $lease.Lock(0, 1) }
    catch { $lease.Dispose(); throw 'Dictation runtime is busy. Finish dictation or close other Koncus Nai instances, then retry preparation.' }
    $leases.Add($lease)
  }
  Ensure-NativeCrt
  $targetVenv = Join-Path $RuntimeRoot '.venv'
  $backupVenv = Join-Path $RuntimeRoot '.cohere-previous'
  if (-not (Test-Path -LiteralPath $targetVenv) -and (Test-Path -LiteralPath $backupVenv)) {
    Move-Item -LiteralPath $backupVenv -Destination $targetVenv
  }
  $python = Join-Path $targetVenv 'Scripts\python.exe'
  Write-Output 'Verifying pinned local Python model dependencies'
  if (Test-Path -LiteralPath $python) {
    & $python -c $verify $requirementsPath
    if ($LASTEXITCODE -eq 0) { Write-Output 'Local model Python runtime ready'; return }
  }
  if ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($RuntimeRoot)).AvailableFreeSpace -lt 10GB) {
    throw 'Local model runtime setup requires at least 10 GiB free storage.'
  }
  # Installation/cancellation never modifies the working environment. Reuse a partial candidate on retry.
  $venv = Join-Path $RuntimeRoot '.cohere-candidate'
  $python = Join-Path $venv 'Scripts\python.exe'
  if (-not (Test-Path -LiteralPath $python)) {
    $basePython = Join-Path $RuntimeRoot 'python-base\python.exe'
    if (-not (Test-Path -LiteralPath $basePython)) {
      # Single quotes survive Windows PowerShell 5.1's native argument transport.
      $probe = "import sys,struct; sys.exit(1) if sys.version_info[:2] != (3,11) or struct.calcsize('P') != 8 else print(sys.executable)"
      $probeErrorPreference = $ErrorActionPreference
      try {
        # An absent launcher version is an expected probe miss, not a setup failure.
        $ErrorActionPreference = 'Continue'
        if (Get-Command py -ErrorAction SilentlyContinue) {
          $candidate = & py -3.11 -c $probe 2>$null
          if ($LASTEXITCODE -eq 0 -and $candidate -and (Test-Path -LiteralPath ([string]$candidate))) { $basePython = [string]$candidate }
        }
        if (-not (Test-Path -LiteralPath $basePython) -and (Get-Command python -ErrorAction SilentlyContinue)) {
          $candidate = & python -c $probe 2>$null
          if ($LASTEXITCODE -eq 0 -and $candidate -and (Test-Path -LiteralPath ([string]$candidate))) { $basePython = [string]$candidate }
        }
      } finally { $ErrorActionPreference = $probeErrorPreference }
    }
    if (-not (Test-Path -LiteralPath $basePython)) {
      $installer = Join-Path $RuntimeRoot 'python-3.11.9-amd64.exe'
      $expected = '5ee42c4eee1e6b4464bb23722f90b45303f79442df63083f05322f1785f5fdde'
      Write-Output 'Downloading/verifying pinned Python 3.11.9 for this Windows user'
      if (-not (Test-Path -LiteralPath $installer) -or (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $expected) {
        $partial = $installer + '.partial'
        Invoke-WebRequest -Uri 'https://www.python.org/ftp/python/3.11.9/python-3.11.9-amd64.exe' -OutFile $partial -UseBasicParsing
        if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $expected) { throw 'Python installer checksum mismatch. Retry preparation.' }
        Move-Item -LiteralPath $partial -Destination $installer -Force
      }
      $signature = Get-AuthenticodeSignature -LiteralPath $installer
      if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|, )O=Python Software Foundation(,|$)') {
        throw 'Python installer publisher verification failed. Retry after repairing Windows certificate trust.'
      }
      $baseRoot = Split-Path -Parent $basePython
      & $installer /quiet InstallAllUsers=0 TargetDir=$baseRoot Include_pip=1 Include_launcher=0 Include_test=0 Include_doc=0 Include_tcltk=0 Shortcuts=0 AssociateFiles=0 PrependPath=0
      if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $basePython)) { throw 'Per-user Python installation failed. Retry preparation.' }
    }
    Write-Output 'Creating the isolated local model environment'
    & $basePython -m venv $venv
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create the local Python environment. Retry preparation.' }
  }
  # Verify every pinned distribution before promoting the candidate, not just a stamp.
  & $python -c $verify $requirementsPath
  if ($LASTEXITCODE -ne 0) {
    Write-Output 'Installing verified wheels (no compiler or source builds)'
    & $python -m pip install --require-hashes --no-deps --only-binary=:all: --no-build-isolation -r $requirementsPath
    if ($LASTEXITCODE -ne 0) { throw 'Pinned model dependency installation failed. Check network/storage and retry preparation.' }
    & $python -c $verify $requirementsPath
    if ($LASTEXITCODE -ne 0) { throw 'Python model dependency validation failed. Repair the local runtime and retry.' }
  }
  if (Test-Path -LiteralPath $backupVenv) {
    $backupFull = [IO.Path]::GetFullPath($backupVenv)
    if (-not $backupFull.StartsWith($RuntimeRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
      throw 'Runtime backup escaped its preparation directory.'
    }
    if (((Get-Item -LiteralPath $backupFull -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
      throw 'Runtime backup contains an unsupported reparse point.'
    }
    Remove-Item -LiteralPath $backupFull -Recurse -Force
  }
  if (Test-Path -LiteralPath $targetVenv) { Move-Item -LiteralPath $targetVenv -Destination $backupVenv }
  try { Move-Item -LiteralPath $venv -Destination $targetVenv }
  catch {
    if (-not (Test-Path -LiteralPath $targetVenv) -and (Test-Path -LiteralPath $backupVenv)) {
      Move-Item -LiteralPath $backupVenv -Destination $targetVenv
    }
    throw
  }
  Write-Output 'Local model Python runtime ready'
}
catch {
  Write-Output ('Preparation failed: ' + $_.Exception.Message)
  exit 1
}
finally {
  foreach ($lease in $leases) { $lease.Dispose() }
}
