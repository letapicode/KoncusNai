[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-KokoroPythonLauncher {
  if (Get-Command py -ErrorAction SilentlyContinue) {
    return [PSCustomObject]@{ Command = "py"; Arguments = @("-3.11") }
  }
  if (Get-Command python -ErrorAction SilentlyContinue) {
    return [PSCustomObject]@{ Command = "python"; Arguments = @() }
  }
  throw "Python 3.11 or newer is required to provision the Kokoro runtime."
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$requirementsPath = Join-Path $repoRoot "local-models\requirements-kokoro-lock.txt"
if (-not (Test-Path -LiteralPath $requirementsPath)) {
  throw "Kokoro requirements file not found at $requirementsPath."
}

$runtimeRoot = Join-Path $env:LOCALAPPDATA "DictateAnywhere\kokoro-runtime"
$venvPath = Join-Path $runtimeRoot ".venv"
$pythonPath = Join-Path $venvPath "Scripts\python.exe"
$requirementsStampPath = Join-Path $runtimeRoot ".requirements.sha256"
New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null

if (-not (Test-Path -LiteralPath $pythonPath)) {
  $launcher = Resolve-KokoroPythonLauncher
  $pythonCommand = $launcher.Command
  $pythonArguments = $launcher.Arguments
  & $pythonCommand @pythonArguments -m venv $venvPath
  if ($LASTEXITCODE -ne 0) { throw "Failed to create the Kokoro virtual environment." }
}

& $pythonPath -m pip install --require-hashes --no-deps --no-build-isolation -r $requirementsPath
if ($LASTEXITCODE -ne 0) { throw "Failed to install Kokoro dependencies." }
(Get-FileHash -LiteralPath $requirementsPath -Algorithm SHA256).Hash | Set-Content -LiteralPath $requirementsStampPath -NoNewline

Write-Host "Kokoro runtime ready: $pythonPath" -ForegroundColor Green
