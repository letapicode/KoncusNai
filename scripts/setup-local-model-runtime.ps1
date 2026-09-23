[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-PythonLauncher {
  if (Get-Command py -ErrorAction SilentlyContinue) {
    return [PSCustomObject]@{
      Command = "py"
      Arguments = @("-3.11")
    }
  }

  if (Get-Command python -ErrorAction SilentlyContinue) {
    return [PSCustomObject]@{
      Command = "python"
      Arguments = @()
    }
  }

  throw "Python 3.11 or newer is required to provision the local model runtime."
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$requirementsPath = Join-Path $repoRoot "scripts\local-models\requirements-lock.txt"
if (-not (Test-Path $requirementsPath)) {
  throw "Local-model requirements file not found at $requirementsPath."
}

$runtimeRoot = Join-Path $env:LOCALAPPDATA "DictateAnywhere\local-model-runtime"
$venvPath = Join-Path $runtimeRoot ".venv"
$pythonPath = Join-Path $venvPath "Scripts\python.exe"

New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null

if (-not (Test-Path $pythonPath)) {
  $launcher = Resolve-PythonLauncher
  $pythonCommand = $launcher.Command
  $pythonArguments = $launcher.Arguments

  & $pythonCommand @pythonArguments -m venv $venvPath
  if ($LASTEXITCODE -ne 0) {
    throw "Failed to create the DictateAnywhere local-model virtual environment."
  }
}

& $pythonPath -m pip install --require-hashes --no-deps --no-build-isolation -r $requirementsPath
if ($LASTEXITCODE -ne 0) {
  throw "Failed to install the DictateAnywhere local-model runtime requirements."
}

Write-Host ""
Write-Host "Local model runtime ready." -ForegroundColor Green
Write-Host "Python: $pythonPath"
Write-Host "The app will auto-detect this runtime on startup."
