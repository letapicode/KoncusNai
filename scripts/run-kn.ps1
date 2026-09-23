[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = [IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath ".."))
$projectPath = Join-Path $repoRoot "src\DictateAnywhere.App\DictateAnywhere.App.csproj"
$appPath = Join-Path $repoRoot "src\DictateAnywhere.App\bin\Release\net8.0-windows\DictateAnywhere.App.exe"
$buildMarkerPath = Join-Path $repoRoot "src\DictateAnywhere.App\bin\Release\net8.0-windows\DictateAnywhere.App.dll"
function Test-BuildRequired {
  if (-not (Test-Path -LiteralPath $appPath) -or -not (Test-Path -LiteralPath $buildMarkerPath)) {
    return $true
  }

  $appWriteTime = (Get-Item -LiteralPath $buildMarkerPath).LastWriteTimeUtc
  $rootBuildFiles = Get-ChildItem -LiteralPath $repoRoot -File -Include *.props,*.targets,*.json
  $sourceFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot "src"), (Join-Path $repoRoot "scripts") -Recurse -File -Include *.cs,*.xaml,*.csproj,*.props,*.targets,*.ico,*.png,*.svg,*.json,*.py,*.ps1,*.cmd,*.txt |
    Where-Object {
      $_.FullName -notmatch "[\\/](bin|obj)[\\/]" -and $_.Name -notlike "*_wpftmp.csproj"
    }
  return @(@($sourceFiles) + @($rootBuildFiles) | Where-Object { $_.LastWriteTimeUtc -gt $appWriteTime } | Select-Object -First 1).Count -gt 0
}

if (Test-BuildRequired) {
  Write-Host "Updating Koncus Nai..." -ForegroundColor Cyan
  $buildLog = New-TemporaryFile
  try {
    & dotnet build $projectPath -c Release --nologo -v:q *> $buildLog
    if ($LASTEXITCODE -ne 0) {
      Get-Content -LiteralPath $buildLog | Out-Host
      throw "Koncus Nai could not be built ($LASTEXITCODE)."
    }
  }
  finally {
    Remove-Item -LiteralPath $buildLog -Force -ErrorAction SilentlyContinue
  }
}

Start-Process -FilePath $appPath -WorkingDirectory (Split-Path -Parent $appPath)
