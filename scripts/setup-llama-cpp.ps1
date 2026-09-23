[CmdletBinding()]
param(
  [string]$ModelPath,
  [switch]$SkipRuntimeDownload
)

$ErrorActionPreference = 'Stop'
$runtimeDirectory = Join-Path $env:LOCALAPPDATA 'DictateAnywhere\llama.cpp'
$modelDirectory = Join-Path $env:LOCALAPPDATA 'DictateAnywhere\models\llama-cpp'
$targetModelPath = Join-Path $modelDirectory 'gemma-3-4b-it-Q4_K_M.gguf'
$targetModelSha256 = '882e8d2db44dc554fb0ea5077cb7e4bc49e7342a1f0da57901c0802ea21a0863'

New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $modelDirectory -Force | Out-Null

if (-not $SkipRuntimeDownload) {
  $assetName = 'llama-b10823-bin-win-cpu-x64.zip'
  $assetUrl = 'https://github.com/ggml-org/llama.cpp/releases/download/b10823/llama-b10823-bin-win-cpu-x64.zip'
  $assetSha256 = 'fa3d7a84302fddfc669e6187aeffc85503a1172372eb86905f0529a83d99e1dc'

  $temporaryDirectory = New-Item -ItemType Directory -Path ([System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), [Guid]::NewGuid().ToString('N')))
  $stagedRuntime = "$runtimeDirectory.staged-$([Guid]::NewGuid().ToString('N'))"
  $backupRuntime = "$runtimeDirectory.backup-$([Guid]::NewGuid().ToString('N'))"
  try {
    $archivePath = Join-Path $temporaryDirectory.FullName $assetName
    Invoke-WebRequest -Uri $assetUrl -OutFile $archivePath
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $assetSha256) { throw 'The downloaded llama.cpp runtime failed its integrity check.' }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
      $servers = @($archive.Entries | Where-Object { $_.Name -ieq 'llama-server.exe' })
      if ($servers.Count -ne 1) { throw 'The downloaded llama.cpp release must contain exactly one llama-server.exe.' }
      $sourceDirectory = [IO.Path]::GetDirectoryName($servers[0].FullName).Replace('\', '/')
      $runtimeEntries = @($archive.Entries | Where-Object {
        $_.Name -and [IO.Path]::GetDirectoryName($_.FullName).Replace('\', '/') -eq $sourceDirectory
      })
      $duplicates = @($runtimeEntries | Group-Object { $_.Name.ToLowerInvariant() } | Where-Object Count -gt 1)
      if ($duplicates.Count -gt 0) { throw 'The downloaded llama.cpp release contains duplicate runtime filenames.' }
      foreach ($entry in $runtimeEntries) {
        $normalized = $entry.FullName.Replace('\', '/')
        $expected = if ($sourceDirectory) { "$sourceDirectory/$($entry.Name)" } else { $entry.Name }
        if ($normalized -ne $expected -or $normalized.StartsWith('/') -or @($normalized.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0) {
          throw 'The downloaded llama.cpp release contains an unsafe runtime path.'
        }
        New-Item -ItemType Directory -Path $stagedRuntime -Force | Out-Null
        $destination = Join-Path $stagedRuntime $entry.Name
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
      }
    }
    finally {
      $archive.Dispose()
    }
    if (-not (Test-Path -LiteralPath (Join-Path $stagedRuntime 'llama-server.exe') -PathType Leaf)) {
      throw 'The staged llama.cpp runtime is incomplete.'
    }
    Set-Content -LiteralPath (Join-Path $stagedRuntime '.notype-runtime-provenance') -Value "b10823`n$assetSha256" -Encoding Ascii
    Move-Item -LiteralPath $runtimeDirectory -Destination $backupRuntime
    try {
      Move-Item -LiteralPath $stagedRuntime -Destination $runtimeDirectory
      Remove-Item -LiteralPath $backupRuntime -Force -Recurse
    }
    catch {
      if (Test-Path -LiteralPath $runtimeDirectory) { Remove-Item -LiteralPath $runtimeDirectory -Force -Recurse }
      Move-Item -LiteralPath $backupRuntime -Destination $runtimeDirectory
      throw
    }
  }
  finally {
    Remove-Item -LiteralPath $temporaryDirectory.FullName -Force -Recurse -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stagedRuntime -Force -Recurse -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $backupRuntime -Force -Recurse -ErrorAction SilentlyContinue
  }
}

if (-not [string]::IsNullOrWhiteSpace($ModelPath)) {
  if (-not (Test-Path -LiteralPath $ModelPath -PathType Leaf)) {
    throw "GGUF model was not found: $ModelPath"
  }
  $sourceModelHash = (Get-FileHash -LiteralPath $ModelPath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($sourceModelHash -ne $targetModelSha256) {
    throw 'The supplied GGUF does not match the supported immutable Gemma model.'
  }
  $stagedModelPath = "$targetModelPath.staged-$([Guid]::NewGuid().ToString('N'))"
  try {
    Copy-Item -LiteralPath $ModelPath -Destination $stagedModelPath
    $stagedModelHash = (Get-FileHash -LiteralPath $stagedModelPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($stagedModelHash -ne $targetModelSha256) { throw 'The staged GGUF failed its integrity check.' }
    Move-Item -LiteralPath $stagedModelPath -Destination $targetModelPath -Force
    Set-Content -LiteralPath "$targetModelPath.sha256" -Value $targetModelSha256 -Encoding Ascii
  }
  finally {
    Remove-Item -LiteralPath $stagedModelPath -Force -ErrorAction SilentlyContinue
  }
}

Write-Host "llama.cpp runtime: $(Join-Path $runtimeDirectory 'llama-server.exe')"
Write-Host "GGUF model path: $targetModelPath"
if (-not (Test-Path -LiteralPath $targetModelPath -PathType Leaf)) {
  Write-Host 'Runtime installed. Download a compatible Q4_K_M GGUF model, then run this script again with -ModelPath.'
}
