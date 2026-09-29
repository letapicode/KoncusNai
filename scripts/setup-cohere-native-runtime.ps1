[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$ModelDirectory,
  [string]$RuntimeDirectory = (Join-Path $env:LOCALAPPDATA 'DictateAnywhere\cohere-native\0.2.4'),
  [string]$PythonPath = (Join-Path $env:LOCALAPPDATA 'DictateAnywhere\local-model-runtime\.venv\Scripts\python.exe'),
  [ValidateSet('cpu', 'vulkan')][string]$Backend = 'cpu',
  [ValidateRange(1, 64)][int]$Threads = 12,
  [string]$CMakePath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$commit = '4807edaf210d0d7e8a6f7fb2a44b65966a2797f0'
$archiveHash = '09705f54218817c065602ada8fd0f4d13b3f7fbb9d94929eeb3789c6c2b1f34a'
$modelRoot = (Resolve-Path -LiteralPath $ModelDirectory).Path
$runtimeRoot = [IO.Path]::GetFullPath($RuntimeDirectory)
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\') + '\'
if ($runtimeRoot.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase) -and
    -not $runtimeRoot.StartsWith((Join-Path $repoRoot 'artifacts\'), [StringComparison]::OrdinalIgnoreCase)) {
  throw 'Native binaries, model weights and conversion logs must stay under artifacts/ or outside Git.'
}
if (-not (Test-Path -LiteralPath $PythonPath)) { throw 'Install the existing local model runtime first.' }
if ($Threads -gt [Environment]::ProcessorCount) { throw 'Thread count exceeds the logical processor count.' }
New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
$source = Join-Path $runtimeRoot 'source'
if (-not (Test-Path -LiteralPath $source)) {
  & git clone --depth 1 --branch v0.2.4 https://github.com/handy-computer/transcribe.cpp.git $source
  if ($LASTEXITCODE -ne 0) { throw 'Pinned native source download failed.' }
}
$actualCommit = & git -c "safe.directory=$source" -C $source rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $commit) { throw 'Native source does not match the pinned commit.' }
# Refuse local source modifications: matching headers and binaries are an ABI requirement.
$changes = & git -c "safe.directory=$source" -C $source status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Pinned native source was modified.' }
$archive = Join-Path $runtimeRoot 'native.tar.gz'
if (-not (Test-Path -LiteralPath $archive)) {
  Invoke-WebRequest 'https://github.com/handy-computer/transcribe.cpp/releases/download/v0.2.4/transcribe-native-0.2.4-windows-x86_64-cpu-vulkan.tar.gz' -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() -ne $archiveHash) { throw 'Native release archive checksum mismatch.' }
$nativeRoot = Join-Path $runtimeRoot 'native'
New-Item -ItemType Directory -Path $nativeRoot -Force | Out-Null
& tar -xzf $archive -C $nativeRoot
if ($LASTEXITCODE -ne 0) { throw 'Native archive extraction failed.' }
$native = Join-Path $nativeRoot 'transcribe-native-windows-x86_64-cpu-vulkan'
$contract = Get-Content -LiteralPath (Join-Path $native 'contract.json') -Raw | ConvertFrom-Json
if ($contract.version -ne '0.2.4' -or $contract.header_hash -ne '7df72bf9e667b8c2') { throw 'Native ABI contract mismatch.' }
$modelLicense = Join-Path $runtimeRoot 'Cohere-Apache-2.0-LICENSE.txt'
if (-not (Test-Path -LiteralPath $modelLicense)) {
  Invoke-WebRequest 'https://www.apache.org/licenses/LICENSE-2.0.txt' -OutFile $modelLicense
}

$sourceHash = (Get-FileHash -LiteralPath (Join-Path $modelRoot 'model.safetensors')).Hash.ToLowerInvariant()
$bf16 = Join-Path $runtimeRoot 'cohere-BF16.gguf'
$q8 = Join-Path $runtimeRoot 'cohere-Q8_0.gguf'
$receiptPath = Join-Path $runtimeRoot 'conversion-receipt.json'
$reuse = $false
if ((Test-Path -LiteralPath $receiptPath) -and (Test-Path -LiteralPath $q8)) {
  $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
  $reuse = $receipt.source_sha256 -eq $sourceHash -and $receipt.commit -eq $commit -and
           $receipt.model_sha256 -eq (Get-FileHash -LiteralPath $q8).Hash.ToLowerInvariant()
}
# Verified existing conversions do not require build tooling or converter packages.
if (-not $reuse) {
if (-not $CMakePath) {
  $cmake = Get-Command cmake -ErrorAction SilentlyContinue
  $CMakePath = if ($cmake) { $cmake.Source } else {
    'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
  }
}
if (-not (Test-Path -LiteralPath $CMakePath)) { throw 'CMake and Visual Studio C++ Build Tools are required for the quantizer.' }
$build = Join-Path $runtimeRoot 'build'
& $CMakePath -S $source -B $build -DTRANSCRIBE_BUILD_TOOLS=ON -DTRANSCRIBE_USE_SYSTEM_BLAS=OFF
if ($LASTEXITCODE -ne 0) { throw 'Quantizer configuration failed.' }
& $CMakePath --build $build --config Release --target transcribe-quantize --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'Quantizer build failed.' }
$quantizer = Join-Path $build 'bin\Release\transcribe-quantize.exe'
if (-not (Test-Path -LiteralPath $quantizer)) { $quantizer = Join-Path $build 'bin\transcribe-quantize.exe' }

$conversion = Join-Path $runtimeRoot 'conversion-env'
$conversionPython = Join-Path $conversion 'Scripts\python.exe'
if (-not (Test-Path -LiteralPath $conversionPython)) {
  & $PythonPath -m venv $conversion
  if ($LASTEXITCODE -ne 0) { throw 'Conversion environment creation failed.' }
}
# Reuse the installed Torch/SentencePiece without changing the configured runtime.
# Prepend its site-packages so a machine-wide Torch cannot accidentally take precedence.
$installedSite = [IO.Path]::GetFullPath((Join-Path (Split-Path (Split-Path $PythonPath)) 'Lib\site-packages'))
$siteLiteral = $installedSite.Replace('\', '\\').Replace("'", "\'")
"import sys; sys.path.insert(0, '$siteLiteral')" | Set-Content -LiteralPath (Join-Path $conversion 'Lib\site-packages\installed-runtime.pth') -Encoding ascii
& $conversionPython -m pip install --no-deps gguf==0.18.0
if ($LASTEXITCODE -ne 0) { throw 'Pinned converter dependency installation failed.' }
  & $conversionPython -u (Join-Path $source 'scripts\convert-cohere.py') $modelRoot $bf16
  if ($LASTEXITCODE -ne 0) { throw 'Same-checkpoint conversion failed.' }
  & $quantizer $bf16 $q8 --quant Q8_0
  if ($LASTEXITCODE -ne 0) { throw 'Q8 quantization failed.' }
}
$q8Hash = (Get-FileHash -LiteralPath $q8).Hash.ToLowerInvariant()
@{source_sha256=$sourceHash; model_sha256=$q8Hash; commit=$commit} | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding utf8
$hashes = @{}
Get-ChildItem -LiteralPath $native -Filter '*.dll' | ForEach-Object {
  $hashes[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()
}
$bindingRoot = Join-Path $source 'bindings\python\src'
Get-ChildItem -LiteralPath (Join-Path $bindingRoot 'transcribe_cpp') -Filter '*.py' | ForEach-Object {
  $hashes[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()
}
$manifestPath = Join-Path $runtimeRoot 'native-manifest.json'
@{version='0.2.4'; commit=$commit; header_hash='7df72bf9e667b8c2'; precision='Q8_0';
  backend=$Backend; threads=$Threads; model_path=$q8; model_sha256=$q8Hash; source_sha256=$sourceHash;
  library_path=(Join-Path $native 'transcribe.dll'); bindings_path=$bindingRoot; runtime_hashes=$hashes
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Output "Native runtime prepared: $manifestPath"
Write-Output 'Opt in for one launch by setting DICTATEANYWHERE_COHERE_NATIVE_MANIFEST to that path. No application settings were changed.'
