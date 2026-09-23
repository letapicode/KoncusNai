#requires -Version 7.0

[CmdletBinding()]
param(
  [ValidateSet("Matrix", "Replacement", "SelfTest")][string]$Mode = "Matrix",
  [string]$OutputDirectory = "artifacts/wp11-application-evidence",
  [string]$TargetPath = "artifacts/wp11-manual-fixtures/wp11-dictation-target.txt",
  [string]$FixtureDirectory = "artifacts/wp11-manual-fixtures",
  [ValidateRange(1, 10)][int]$SamplesPerBucket = 5,
  [ValidateRange(100, 2000)][int]$DurationEarlyToleranceMs = 250,
  [ValidateRange(100, 2000)][int]$DurationLateToleranceMs = 750,
  [ValidateRange(10, 120)][int]$StartupTimeoutSeconds = 45,
  [ValidateRange(10, 300)][int]$CompletionTimeoutSeconds = 120,
  [switch]$ConfirmKeyboardAutomation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$script:NativeMethodsLoaded = $false
$script:TargetProcess = $null
$script:OwnedAppProcess = $null
$script:RawSamples = [Collections.Generic.List[object]]::new()
$script:ProcessSamples = [Collections.Generic.List[object]]::new()

function Assert-Condition {
  param(
    [Parameter(Mandatory)][bool]$Condition,
    [Parameter(Mandatory)][string]$Message
  )

  if (-not $Condition) {
    throw $Message
  }
}

function Resolve-RepoRoot {
  Assert-Condition (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) "Unable to resolve the scripts directory."
  return [IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath ".."))
}

function Resolve-PathUnderRoot {
  param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][string]$Label
  )

  $resolved = if ([IO.Path]::IsPathRooted($Path)) {
    [IO.Path]::GetFullPath($Path)
  }
  else {
    [IO.Path]::GetFullPath((Join-Path -Path $Root -ChildPath $Path))
  }
  $normalizedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
  Assert-Condition $resolved.StartsWith($normalizedRoot, [StringComparison]::OrdinalIgnoreCase) "$Label must remain under $normalizedRoot"
  return $resolved
}

function Ensure-NativeMethods {
  if ($script:NativeMethodsLoaded) {
    return
  }

  Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class Wp11NativeMethods
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint sourceThreadId, uint targetThreadId, bool attach);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();
}
"@
  Add-Type -AssemblyName System.Windows.Forms
  $script:NativeMethodsLoaded = $true
}

function Get-WindowTitle {
  param([Parameter(Mandatory)][IntPtr]$Handle)

  $builder = [Text.StringBuilder]::new(1024)
  $null = [Wp11NativeMethods]::GetWindowText($Handle, $builder, $builder.Capacity)
  return $builder.ToString()
}

function Get-ForegroundProcessId {
  $handle = [Wp11NativeMethods]::GetForegroundWindow()
  [uint32]$processId = 0
  $null = [Wp11NativeMethods]::GetWindowThreadProcessId($handle, [ref]$processId)
  return [int]$processId
}

function Assert-TargetFocused {
  param(
    [Parameter(Mandatory)][Diagnostics.Process]$Process,
    [Parameter(Mandatory)][string]$ExpectedFileName
  )

  $Process.Refresh()
  Assert-Condition (-not $Process.HasExited) "The scratch-target Notepad process exited."
  Assert-Condition ($Process.MainWindowHandle -ne [IntPtr]::Zero) "The scratch-target Notepad window is unavailable."
  $shell = New-Object -ComObject WScript.Shell
  $null = $shell.AppActivate($Process.Id)
  $foreground = [Wp11NativeMethods]::GetForegroundWindow()
  [uint32]$foregroundProcessId = 0
  [uint32]$foregroundThreadId = [Wp11NativeMethods]::GetWindowThreadProcessId($foreground, [ref]$foregroundProcessId)
  [uint32]$currentThreadId = [Wp11NativeMethods]::GetCurrentThreadId()
  $attached = $foregroundThreadId -ne 0 -and $foregroundThreadId -ne $currentThreadId -and
    [Wp11NativeMethods]::AttachThreadInput($currentThreadId, $foregroundThreadId, $true)
  try {
    $null = [Wp11NativeMethods]::ShowWindow($Process.MainWindowHandle, 9)
    $null = [Wp11NativeMethods]::BringWindowToTop($Process.MainWindowHandle)
    $null = [Wp11NativeMethods]::SetForegroundWindow($Process.MainWindowHandle)
  }
  finally {
    if ($attached) {
      $null = [Wp11NativeMethods]::AttachThreadInput($currentThreadId, $foregroundThreadId, $false)
    }
  }
  $deadline = [datetimeoffset]::UtcNow.AddSeconds(3)
  do {
    Start-Sleep -Milliseconds 100
    $foregroundId = Get-ForegroundProcessId
    $title = Get-WindowTitle ([Wp11NativeMethods]::GetForegroundWindow())
    if ($foregroundId -eq $Process.Id -and $title.Contains($ExpectedFileName, [StringComparison]::OrdinalIgnoreCase)) {
      return
    }
  } while ([datetimeoffset]::UtcNow -lt $deadline)
  throw "Keyboard automation stopped because the verified scratch-target window did not receive focus."
}

function Wait-ForManualTargetFocus {
  param(
    [Parameter(Mandatory)][Diagnostics.Process]$Process,
    [Parameter(Mandatory)][string]$ExpectedFileName,
    [ValidateRange(5, 120)][int]$TimeoutSeconds = 45
  )

  Write-Host "Click the '$ExpectedFileName' Notepad window now. Waiting up to $TimeoutSeconds seconds for verified focus."
  $deadline = [datetimeoffset]::UtcNow.AddSeconds($TimeoutSeconds)
  while ([datetimeoffset]::UtcNow -lt $deadline) {
    $foregroundHandle = [Wp11NativeMethods]::GetForegroundWindow()
    $foregroundId = Get-ForegroundProcessId
    $title = Get-WindowTitle $foregroundHandle
    if ($foregroundId -eq $Process.Id -and $title.Contains($ExpectedFileName, [StringComparison]::OrdinalIgnoreCase)) {
      return
    }
    Start-Sleep -Milliseconds 250
  }
  throw "The explicit scratch target did not receive manual focus within $TimeoutSeconds seconds."
}

function Send-Hotkey {
  param(
    [Parameter(Mandatory)][int]$Modifiers,
    [Parameter(Mandatory)][int]$VirtualKey
  )

  Assert-Condition ($Modifiers -ge 1 -and $Modifiers -le 15) "The configured hotkey modifier mask is unsupported."
  Assert-Condition ($VirtualKey -ge 1 -and $VirtualKey -le 254) "The configured virtual key is unsupported."
  $modifierKeys = [Collections.Generic.List[byte]]::new()
  if (($Modifiers -band 2) -ne 0) { $modifierKeys.Add(0x11) }
  if (($Modifiers -band 1) -ne 0) { $modifierKeys.Add(0x12) }
  if (($Modifiers -band 4) -ne 0) { $modifierKeys.Add(0x10) }
  if (($Modifiers -band 8) -ne 0) { $modifierKeys.Add(0x5B) }

  try {
    foreach ($key in $modifierKeys) {
      [Wp11NativeMethods]::keybd_event($key, 0, 0, [UIntPtr]::Zero)
    }
    [Wp11NativeMethods]::keybd_event([byte]$VirtualKey, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [Wp11NativeMethods]::keybd_event([byte]$VirtualKey, 0, 2, [UIntPtr]::Zero)
  }
  finally {
    for ($index = $modifierKeys.Count - 1; $index -ge 0; $index--) {
      [Wp11NativeMethods]::keybd_event($modifierKeys[$index], 0, 2, [UIntPtr]::Zero)
    }
  }
  Start-Sleep -Milliseconds 150
}

function Get-HotkeyDisplay {
  param(
    [Parameter(Mandatory)][int]$Modifiers,
    [Parameter(Mandatory)][int]$VirtualKey
  )

  $parts = [Collections.Generic.List[string]]::new()
  if (($Modifiers -band 2) -ne 0) { $parts.Add("Ctrl") }
  if (($Modifiers -band 1) -ne 0) { $parts.Add("Alt") }
  if (($Modifiers -band 4) -ne 0) { $parts.Add("Shift") }
  if (($Modifiers -band 8) -ne 0) { $parts.Add("Win") }
  $keyName = if ($VirtualKey -eq 0x20) {
    "Space"
  }
  elseif ($VirtualKey -ge 0x30 -and $VirtualKey -le 0x39) {
    [char]$VirtualKey
  }
  elseif ($VirtualKey -ge 0x41 -and $VirtualKey -le 0x5A) {
    [char]$VirtualKey
  }
  elseif ($VirtualKey -ge 0x70 -and $VirtualKey -le 0x87) {
    "F$($VirtualKey - 0x6F)"
  }
  else {
    throw "The operator runner cannot verify display text for virtual key $VirtualKey."
  }
  $parts.Add([string]$keyName)
  return [string]::Join(" + ", $parts)
}

function Get-WavDurationMs {
  param([Parameter(Mandatory)][string]$Path)

  $stream = [IO.File]::OpenRead($Path)
  try {
    $reader = [IO.BinaryReader]::new($stream)
    try {
      Assert-Condition ((-join $reader.ReadChars(4)) -eq "RIFF") "Fixture is not a RIFF WAV file."
      $null = $reader.ReadUInt32()
      Assert-Condition ((-join $reader.ReadChars(4)) -eq "WAVE") "Fixture is not a WAVE file."
      [uint32]$bytesPerSecond = 0
      [uint32]$dataLength = 0
      while ($stream.Position + 8 -le $stream.Length) {
        $chunkId = -join $reader.ReadChars(4)
        [uint32]$chunkLength = $reader.ReadUInt32()
        Assert-Condition ($stream.Position + $chunkLength -le $stream.Length) "Fixture contains a malformed WAV chunk."
        if ($chunkId -eq "fmt ") {
          Assert-Condition ($chunkLength -ge 16) "Fixture WAV format chunk is incomplete."
          [uint16]$format = $reader.ReadUInt16()
          $null = $reader.ReadUInt16()
          $null = $reader.ReadUInt32()
          $bytesPerSecond = $reader.ReadUInt32()
          $null = $reader.ReadUInt16()
          $null = $reader.ReadUInt16()
          Assert-Condition ($format -eq 1) "Only PCM WAV fixtures are supported."
          if ($chunkLength -gt 16) { $stream.Position += ($chunkLength - 16) }
        }
        elseif ($chunkId -eq "data") {
          $dataLength = $chunkLength
          $stream.Position += $chunkLength
        }
        else {
          $stream.Position += $chunkLength
        }
        if (($chunkLength % 2) -ne 0 -and $stream.Position -lt $stream.Length) { $stream.Position++ }
      }
      Assert-Condition ($bytesPerSecond -gt 0 -and $dataLength -gt 0) "Fixture WAV duration could not be determined."
      return [math]::Round(($dataLength * 1000.0) / $bytesPerSecond, 2)
    }
    finally {
      $reader.Dispose()
    }
  }
  finally {
    $stream.Dispose()
  }
}

function Get-LogRecords {
  param([Parameter(Mandatory)][datetimeoffset]$SessionStartedUtc)

  $logRoot = Join-Path -Path $env:LOCALAPPDATA -ChildPath "DictateAnywhere\logs"
  if (-not (Test-Path -LiteralPath $logRoot)) {
    return @()
  }

  $records = [Collections.Generic.List[object]]::new()
  $files = Get-ChildItem -LiteralPath $logRoot -Filter "app-*.log" -File |
    Where-Object { $_.LastWriteTimeUtc -ge $SessionStartedUtc.UtcDateTime.AddSeconds(-2) } |
    Sort-Object FullName
  foreach ($file in $files) {
    $stream = [IO.FileStream]::new(
      $file.FullName,
      [IO.FileMode]::Open,
      [IO.FileAccess]::Read,
      [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
      $reader = [IO.StreamReader]::new($stream)
      try {
        $text = $reader.ReadToEnd()
      }
      finally {
        $reader.Dispose()
      }
    }
    finally {
      $stream.Dispose()
    }
    $lines = [Text.RegularExpressions.Regex]::Split($text, "\r?\n")
    $completeLineCount = if ($text.EndsWith("`n", [StringComparison]::Ordinal)) { $lines.Count } else { [math]::Max(0, $lines.Count - 1) }
    for ($lineIndex = 0; $lineIndex -lt $completeLineCount; $lineIndex++) {
      $line = $lines[$lineIndex]
      if ([string]::IsNullOrWhiteSpace($line)) {
        continue
      }
      try {
        $record = $line | ConvertFrom-Json -Depth 20 -ErrorAction Stop
        if ([datetimeoffset]$record.timestampUtc -ge $SessionStartedUtc.AddSeconds(-2)) {
          $records.Add($record)
        }
      }
      catch {
        throw "Structured application log contains malformed JSON."
      }
    }
  }
  return @($records)
}

function Wait-ForLogRecord {
  param(
    [Parameter(Mandatory)][datetimeoffset]$SessionStartedUtc,
    [Parameter(Mandatory)][datetimeoffset]$AfterUtc,
    [Parameter(Mandatory)][scriptblock]$Predicate,
    [Parameter(Mandatory)][int]$TimeoutSeconds,
    [Parameter(Mandatory)][string]$Description,
    [Diagnostics.Process]$AppProcess
  )

  $deadline = [datetimeoffset]::UtcNow.AddSeconds($TimeoutSeconds)
  while ([datetimeoffset]::UtcNow -lt $deadline) {
    $matches = @(Get-LogRecords -SessionStartedUtc $SessionStartedUtc | Where-Object {
      [datetimeoffset]$_.timestampUtc -ge $AfterUtc -and (& $Predicate $_)
    })
    if ($matches.Count -gt 0) {
      return $matches[-1]
    }
    if ($null -ne $AppProcess) {
      $AppProcess.Refresh()
      Assert-Condition (-not $AppProcess.HasExited) "The owned application exited while waiting for $Description."
    }
    Start-Sleep -Milliseconds 250
  }
  throw "Timed out after $TimeoutSeconds seconds waiting for $Description."
}

function Get-DescendantProcessIds {
  param(
    [Parameter(Mandatory)][int]$RootProcessId,
    [Parameter(Mandatory)][object[]]$Snapshot
  )

  $ids = [Collections.Generic.List[int]]::new()
  $ids.Add($RootProcessId)
  $pending = [Collections.Generic.Queue[int]]::new()
  $pending.Enqueue($RootProcessId)
  while ($pending.Count -gt 0) {
    $parentId = $pending.Dequeue()
    foreach ($entry in $Snapshot | Where-Object { [int]$_.ParentProcessId -eq $parentId }) {
      $childId = [int]$entry.ProcessId
      if (-not $ids.Contains($childId)) {
        $ids.Add($childId)
        $pending.Enqueue($childId)
      }
    }
  }
  return @($ids)
}

function Add-ProcessSample {
  param(
    [Parameter(Mandatory)][Diagnostics.Process]$AppProcess,
    [Parameter(Mandatory)][string]$SampleLabel,
    [Parameter(Mandatory)][Diagnostics.Stopwatch]$Clock
  )

  if ($AppProcess.HasExited) {
    return
  }
  $snapshot = @(Get-CimInstance Win32_Process)
  $ids = @(Get-DescendantProcessIds -RootProcessId $AppProcess.Id -Snapshot $snapshot)
  $processes = @($ids | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
  $python = @($snapshot | Where-Object { $ids -contains [int]$_.ProcessId -and $_.Name -match '^python' })
  $leafWorkers = @($python | Where-Object {
    $candidateId = [int]$_.ProcessId
    -not ($python | Where-Object { [int]$_.ParentProcessId -eq $candidateId })
  })
  [long]$workingSet = 0
  [long]$privateBytes = 0
  [double]$cpuSeconds = 0
  foreach ($process in $processes) {
    $workingSet += [long]$process.WorkingSet64
    $privateBytes += [long]$process.PrivateMemorySize64
    if ($null -ne $process.CPU) { $cpuSeconds += [double]$process.CPU }
  }
  $script:ProcessSamples.Add([pscustomobject]@{
    sample = $SampleLabel
    elapsedMs = [math]::Round($Clock.Elapsed.TotalMilliseconds, 2)
    processCount = $processes.Count
    pythonProcessCount = $python.Count
    leafWorkerCount = $leafWorkers.Count
    workingSetBytes = $workingSet
    privateBytes = $privateBytes
    cumulativeCpuSeconds = [math]::Round($cpuSeconds, 3)
  })
}

function Stop-OwnedApplication {
  param([Parameter(Mandatory)][Diagnostics.Process]$Process)

  $Process.Refresh()
  if ($Process.HasExited) {
    return
  }
  $expectedPath = [IO.Path]::GetFullPath((Join-Path -Path $script:RepoRoot -ChildPath "src\DictateAnywhere.App\bin\Release\net8.0-windows\DictateAnywhere.App.exe"))
  Assert-Condition ([string]::Equals([IO.Path]::GetFullPath($Process.MainModule.FileName), $expectedPath, [StringComparison]::OrdinalIgnoreCase)) "Refusing to terminate a process whose executable identity is not the owned Release application."
  $snapshot = @(Get-CimInstance Win32_Process)
  $ownedIds = @(Get-OwnedProcessIds -RootProcessId $Process.Id -Snapshot $snapshot)
  $ownedProcesses = @($ownedIds | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
  $Process.Kill($true)
  Assert-Condition $Process.WaitForExit(10000) "The owned application process tree did not exit within 10 seconds."
  $deadline = [datetimeoffset]::UtcNow.AddSeconds(10)
  do {
    $remaining = @($ownedProcesses | Where-Object {
      try { -not $_.HasExited } catch { $false }
    })
    if ($remaining.Count -eq 0) {
      return
    }
    Start-Sleep -Milliseconds 100
  } while ([datetimeoffset]::UtcNow -lt $deadline)
  throw "An owned application descendant did not exit within 10 seconds."
}

function Start-OwnedApplication {
  param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$ExpectedHotkeyDisplay
  )

  $existing = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq "DictateAnywhere.App.exe" })
  Assert-Condition ($existing.Count -eq 0) "A pre-existing Koncus Nai application is running; refusing ambiguous ownership."
  $startedUtc = [datetimeoffset]::UtcNow
  $process = Start-Process -FilePath $AppPath -PassThru
  Assert-Condition ($null -ne $process) "The Release application did not start."
  $script:OwnedAppProcess = $process
  $registered = Wait-ForLogRecord -SessionStartedUtc $startedUtc -AfterUtc $startedUtc `
    -Predicate { param($entry) $entry.message -like "Global hotkey registered:*" } `
    -TimeoutSeconds $StartupTimeoutSeconds -Description "global hotkey registration" -AppProcess $process
  Assert-Condition ([string]::Equals([string]$registered.message, "Global hotkey registered: $ExpectedHotkeyDisplay", [StringComparison]::Ordinal)) "The active hotkey differs from current settings; refusing ambiguous automation."
  $null = Wait-ForLogRecord -SessionStartedUtc $startedUtc -AfterUtc $startedUtc `
    -Predicate { param($entry) $entry.message -like "Dictation coordinator started in * mode." } `
    -TimeoutSeconds $StartupTimeoutSeconds -Description "dictation coordinator startup" -AppProcess $process
  return [pscustomobject]@{
    Process = $process
    StartedUtc = $startedUtc
    RegisteredMessage = [string]$registered.message
  }
}

function Start-ScratchTarget {
  param([Parameter(Mandatory)][string]$Path)

  $existing = @(Get-Process -Name notepad -ErrorAction SilentlyContinue)
  $fileName = [IO.Path]::GetFileName($Path)
  $windowed = @($existing | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero })
  if ($windowed.Count -gt 0) {
    $matching = @($windowed | Where-Object {
      $_.MainWindowTitle.Contains($fileName, [StringComparison]::OrdinalIgnoreCase)
    })
    Assert-Condition ($windowed.Count -eq 1 -and $matching.Count -eq 1) "A visible Notepad window other than the single explicit scratch target is running; refusing ambiguous target ownership."
    $script:TargetProcess = $matching[0]
    try {
      Assert-TargetFocused -Process $script:TargetProcess -ExpectedFileName $fileName
    }
    catch {
      Wait-ForManualTargetFocus -Process $script:TargetProcess -ExpectedFileName $fileName
    }
    return $script:TargetProcess
  }
  if (-not (Test-Path -LiteralPath $Path)) {
    $null = New-Item -ItemType File -Path $Path
  }
  $startedUtc = Get-Date
  $null = Start-Process -FilePath "notepad.exe" -ArgumentList @($Path)
  $deadline = [datetimeoffset]::UtcNow.AddSeconds(20)
  while ([datetimeoffset]::UtcNow -lt $deadline) {
    $candidate = @(Get-Process -Name notepad -ErrorAction SilentlyContinue | Where-Object {
      $_.StartTime -ge $startedUtc.AddSeconds(-2) -and $_.MainWindowHandle -ne [IntPtr]::Zero -and $_.MainWindowTitle.Contains($fileName, [StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1)
    if ($candidate.Count -eq 1) {
      $script:TargetProcess = $candidate[0]
      Assert-TargetFocused -Process $script:TargetProcess -ExpectedFileName $fileName
      return $script:TargetProcess
    }
    Start-Sleep -Milliseconds 250
  }
  throw "The dedicated scratch-target Notepad window did not become available."
}

function Get-NumericProperty {
  param(
    [Parameter(Mandatory)][object]$Object,
    [Parameter(Mandatory)][string]$Name
  )

  $property = $Object.PSObject.Properties[$Name]
  Assert-Condition ($null -ne $property -and $null -ne $property.Value) "Completion evidence is missing $Name."
  $numericTypeCodes = @(
    [TypeCode]::Byte, [TypeCode]::SByte, [TypeCode]::Int16, [TypeCode]::UInt16,
    [TypeCode]::Int32, [TypeCode]::UInt32, [TypeCode]::Int64, [TypeCode]::UInt64,
    [TypeCode]::Single, [TypeCode]::Double, [TypeCode]::Decimal
  )
  $typeCode = [Type]::GetTypeCode($property.Value.GetType())
  Assert-Condition ($numericTypeCodes -contains $typeCode) "$Name must be a JSON number, not a numeric-looking string."
  [double]$value = $property.Value
  Assert-Condition (-not [double]::IsNaN($value) -and -not [double]::IsInfinity($value) -and $value -ge 0) "$Name must be finite and non-negative."
  return $value
}

function Complete-SampleEvidence {
  param(
    [Parameter(Mandatory)][string]$Label,
    [Parameter(Mandatory)][ValidateSet("cold", "warm")][string]$Temperature,
    [Parameter(Mandatory)][int]$NominalSeconds,
    [Parameter(Mandatory)][datetimeoffset]$SessionStartedUtc,
    [Parameter(Mandatory)][datetimeoffset]$SampleStartedUtc,
    [Parameter(Mandatory)][object]$Completion,
    [Parameter(Mandatory)][string]$ProviderId,
    [Parameter(Mandatory)][string]$ModelId
  )

  $records = @(Get-LogRecords -SessionStartedUtc $SessionStartedUtc | Where-Object {
    [datetimeoffset]$_.timestampUtc -ge $SampleStartedUtc -and [datetimeoffset]$_.timestampUtc -le ([datetimeoffset]$Completion.timestampUtc).AddSeconds(1)
  })
  $queued = @($records | Where-Object { $_.message -match '^Queued transcription chunk 0 \(([0-9]+) ms, final=true\)\.$' })
  Assert-Condition ($queued.Count -eq 1) "Expected exactly one final captured-audio duration for $Label."
  $null = $queued[0].message -match '^Queued transcription chunk 0 \(([0-9]+) ms, final=true\)\.$'
  [double]$capturedAudioMs = [double]$Matches[1]
  $properties = $Completion.properties
  Assert-Condition ([string]::Equals([string]$properties.providerId, $ProviderId, [StringComparison]::OrdinalIgnoreCase)) "Completion provider does not match current settings."
  Assert-Condition ([string]::Equals([string]$properties.modelId, $ModelId, [StringComparison]::OrdinalIgnoreCase)) "Completion model does not match current settings."
  Assert-Condition ([string]::Equals([string]$properties.insertionOutcome, "VerifiedInserted", [StringComparison]::Ordinal)) "Insertion was not verified."
  $captureFinalizationMs = Get-NumericProperty $properties "captureFinalizationMs"
  $transcriptionWallMs = Get-NumericProperty $properties "transcriptionWallMs"
  $modelReportedMs = Get-NumericProperty $properties "modelReportedMs"
  $transformationMs = Get-NumericProperty $properties "transformationMs"
  $insertionMs = Get-NumericProperty $properties "insertionMs"
  $stopToVisibleMs = Get-NumericProperty $properties "stopToVisibleMs"
  foreach ($stage in @($captureFinalizationMs, $transcriptionWallMs, $modelReportedMs, $transformationMs, $insertionMs)) {
    Assert-Condition ($stopToVisibleMs -ge $stage) "Stop-to-visible timing is smaller than a contained stage."
  }
  $operationId = [string]$Completion.operationId
  Assert-Condition (-not [string]::IsNullOrWhiteSpace($operationId)) "Completion operation ID is missing."
  Assert-Condition (-not ($script:RawSamples | Where-Object { $_.operationId -eq $operationId })) "Duplicate completion operation ID detected."
  [double]$nominalMs = $NominalSeconds * 1000
  $acceptedDuration = $capturedAudioMs -ge ($nominalMs - $DurationEarlyToleranceMs) -and $capturedAudioMs -le ($nominalMs + $DurationLateToleranceMs)

  $request = @($records | Where-Object { $_.message -eq "Cohere transcription requested." } | Select-Object -First 1)
  Assert-Condition ($request.Count -eq 1) "Transcription request event is missing."
  $sessionRecords = @(Get-LogRecords -SessionStartedUtc $SessionStartedUtc)
  $warmupsBeforeRequest = @($sessionRecords | Where-Object {
    $_.message -eq "Cohere worker warmup completed." -and
    [datetimeoffset]$_.timestampUtc -ge $SessionStartedUtc -and
    [datetimeoffset]$_.timestampUtc -le [datetimeoffset]$request[0].timestampUtc
  })
  $temperatureValid = if ($Temperature -eq "cold") { $warmupsBeforeRequest.Count -eq 0 } else { $warmupsBeforeRequest.Count -gt 0 }

  $processSlice = @($script:ProcessSamples | Where-Object { $_.sample -eq $Label })
  Assert-Condition ($processSlice.Count -gt 0) "No owned-process samples were captured for $Label."
  $maximumLeafWorkers = [int](($processSlice | Measure-Object leafWorkerCount -Maximum).Maximum)
  $maximumPythonProcesses = [int](($processSlice | Measure-Object pythonProcessCount -Maximum).Maximum)
  $accepted = $acceptedDuration -and $temperatureValid -and $maximumLeafWorkers -eq 1
  $reasons = [Collections.Generic.List[string]]::new()
  if (-not $acceptedDuration) { $reasons.Add("captured-duration-outside-band") }
  if (-not $temperatureValid) { $reasons.Add("temperature-classification-mismatch") }
  if ($maximumLeafWorkers -ne 1) { $reasons.Add("leaf-worker-count-$maximumLeafWorkers") }

  return [pscustomobject]@{
    label = $Label
    temperature = $Temperature
    nominalSeconds = $NominalSeconds
    operationId = $operationId
    capturedAudioMs = $capturedAudioMs
    captureFinalizationMs = $captureFinalizationMs
    transcriptionWallMs = $transcriptionWallMs
    modelReportedMs = $modelReportedMs
    transformationMs = $transformationMs
    insertionMs = $insertionMs
    stopToVisibleMs = $stopToVisibleMs
    insertionMethod = [string]$properties.insertionMethod
    insertionOutcome = [string]$properties.insertionOutcome
    maximumPythonProcessCount = $maximumPythonProcesses
    maximumLeafWorkerCount = $maximumLeafWorkers
    peakWorkingSetBytes = [long](($processSlice | Measure-Object workingSetBytes -Maximum).Maximum)
    peakPrivateBytes = [long](($processSlice | Measure-Object privateBytes -Maximum).Maximum)
    acceptedByAutomation = $accepted
    operatorVerified = $null
    rejectionReasons = @($reasons)
  }
}

function Invoke-OneSample {
  param(
    [Parameter(Mandatory)][object]$AppSession,
    [Parameter(Mandatory)][Diagnostics.Process]$TargetProcess,
    [Parameter(Mandatory)][string]$TargetFileName,
    [Parameter(Mandatory)][string]$Label,
    [Parameter(Mandatory)][ValidateSet("cold", "warm")][string]$Temperature,
    [Parameter(Mandatory)][int]$NominalSeconds,
    [Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][int]$HotkeyModifiers,
    [Parameter(Mandatory)][int]$HotkeyVirtualKey,
    [Parameter(Mandatory)][string]$ProviderId,
    [Parameter(Mandatory)][string]$ModelId
  )

  Assert-TargetFocused -Process $TargetProcess -ExpectedFileName $TargetFileName
  [Windows.Forms.SendKeys]::SendWait("[$Label] ")
  Start-Sleep -Milliseconds 100
  $sampleStartedUtc = [datetimeoffset]::UtcNow
  Send-Hotkey -Modifiers $HotkeyModifiers -VirtualKey $HotkeyVirtualKey
  $null = Wait-ForLogRecord -SessionStartedUtc $AppSession.StartedUtc -AfterUtc $sampleStartedUtc `
    -Predicate { param($entry) $entry.message -eq "Recording started." } `
    -TimeoutSeconds 10 -Description "recording start for $Label" -AppProcess $AppSession.Process

  $clock = [Diagnostics.Stopwatch]::StartNew()
  $player = [Media.SoundPlayer]::new($FixturePath)
  try {
    $player.Load()
    $player.Play()
    $playUntil = [datetimeoffset]::UtcNow.AddMilliseconds((Get-WavDurationMs -Path $FixturePath) + 100)
    while ([datetimeoffset]::UtcNow -lt $playUntil) {
      Add-ProcessSample -AppProcess $AppSession.Process -SampleLabel $Label -Clock $clock
      Start-Sleep -Milliseconds 250
    }
    $player.Stop()
  }
  finally {
    $player.Dispose()
  }

  Assert-TargetFocused -Process $TargetProcess -ExpectedFileName $TargetFileName
  $stopSentUtc = [datetimeoffset]::UtcNow
  Send-Hotkey -Modifiers $HotkeyModifiers -VirtualKey $HotkeyVirtualKey
  $deadline = [datetimeoffset]::UtcNow.AddSeconds($CompletionTimeoutSeconds)
  $completion = $null
  while ([datetimeoffset]::UtcNow -lt $deadline) {
    Add-ProcessSample -AppProcess $AppSession.Process -SampleLabel $Label -Clock $clock
    $matches = @(Get-LogRecords -SessionStartedUtc $AppSession.StartedUtc | Where-Object {
      [datetimeoffset]$_.timestampUtc -ge $stopSentUtc -and $_.message -eq "Dictation stop-to-visible timing completed."
    })
    if ($matches.Count -gt 0) {
      Assert-Condition ($matches.Count -eq 1) "Duplicate completion events detected for $Label."
      $completion = $matches[0]
      break
    }
    $AppSession.Process.Refresh()
    Assert-Condition (-not $AppSession.Process.HasExited) "The application exited before $Label completed."
    Start-Sleep -Milliseconds 250
  }
  $clock.Stop()
  Assert-Condition ($null -ne $completion) "Timed out waiting for completion of $Label."
  $sample = Complete-SampleEvidence -Label $Label -Temperature $Temperature -NominalSeconds $NominalSeconds `
    -SessionStartedUtc $AppSession.StartedUtc -SampleStartedUtc $sampleStartedUtc -Completion $completion `
    -ProviderId $ProviderId -ModelId $ModelId
  $script:RawSamples.Add($sample)
  Assert-TargetFocused -Process $TargetProcess -ExpectedFileName $TargetFileName
  [Windows.Forms.SendKeys]::SendWait("{ENTER}")
  Start-Sleep -Milliseconds 200
  Write-Host ("{0}: captured={1} ms stop-to-visible={2} ms accepted={3}" -f $Label, $sample.capturedAudioMs, $sample.stopToVisibleMs, $sample.acceptedByAutomation)
}

function Invoke-SelfTest {
  $script:RawSamples.Clear()
  $script:ProcessSamples.Clear()
  $properties = [pscustomobject]@{
    providerId = "cohere-local"
    modelId = "model"
    captureFinalizationMs = 2.5
    transcriptionWallMs = 1000.0
    modelReportedMs = 990.0
    transformationMs = 0.5
    insertionMs = 5.0
    stopToVisibleMs = 1010.0
    insertionMethod = "ClipboardPaste"
    insertionOutcome = "VerifiedInserted"
  }
  foreach ($name in @("captureFinalizationMs", "transcriptionWallMs", "modelReportedMs", "transformationMs", "insertionMs", "stopToVisibleMs")) {
    $value = Get-NumericProperty $properties $name
    Assert-Condition ($value -ge 0) "Synthetic numeric validation failed."
  }
  $stringLookalike = [pscustomobject]@{ transcriptionWallMs = "1000.0" }
  $stringRejected = $false
  try {
    $null = Get-NumericProperty $stringLookalike "transcriptionWallMs"
  }
  catch {
    $stringRejected = $true
  }
  Assert-Condition $stringRejected "String-valued timing lookalike self-test failed."
  $sorted = @(1300.0, 1000.0, 1200.0, 1100.0, 1400.0) | Sort-Object
  $p50 = $sorted[[math]::Ceiling(0.50 * $sorted.Count) - 1]
  $p95 = $sorted[[math]::Ceiling(0.95 * $sorted.Count) - 1]
  Assert-Condition ($p50 -eq 1200.0 -and $p95 -eq 1400.0) "Nearest-rank percentile self-test failed."
  Assert-Condition (3000 -ge (3000 - $DurationEarlyToleranceMs) -and 3000 -le (3000 + $DurationLateToleranceMs)) "Duration acceptance self-test failed."
  Assert-Condition (-not (2100 -ge (3000 - $DurationEarlyToleranceMs))) "Early-duration rejection self-test failed."
  Assert-Condition (-not (3900 -le (3000 + $DurationLateToleranceMs))) "Late-duration rejection self-test failed."
  Write-Host "WP-11 application evidence self-test passed."
}

$script:RepoRoot = Resolve-RepoRoot
Set-Location $script:RepoRoot

if ($Mode -eq "SelfTest") {
  Invoke-SelfTest
  exit 0
}

Assert-Condition $ConfirmKeyboardAutomation "Pass -ConfirmKeyboardAutomation only when the dedicated scratch target may receive automated input."
Ensure-NativeMethods

$artifactRoot = Resolve-PathUnderRoot -Root $script:RepoRoot -Path "artifacts" -Label "Artifact root"
$resolvedOutput = Resolve-PathUnderRoot -Root $artifactRoot -Path $OutputDirectory.Replace('artifacts\', '').Replace('artifacts/', '') -Label "Output directory"
$resolvedTarget = Resolve-PathUnderRoot -Root $artifactRoot -Path $TargetPath.Replace('artifacts\', '').Replace('artifacts/', '') -Label "Scratch target"
$resolvedFixtureDirectory = Resolve-PathUnderRoot -Root $artifactRoot -Path $FixtureDirectory.Replace('artifacts\', '').Replace('artifacts/', '') -Label "Fixture directory"
$null = New-Item -ItemType Directory -Path $resolvedOutput -Force
$null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($resolvedTarget)) -Force

$settingsPath = Join-Path -Path $env:LOCALAPPDATA -ChildPath "DictateAnywhere\settings.json"
Assert-Condition (Test-Path -LiteralPath $settingsPath) "Current settings are unavailable."
$settings = Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json -Depth 20 -ErrorAction Stop
$recordingModeProperty = $settings.PSObject.Properties["recordingMode"]
$recordingMode = if ($null -eq $recordingModeProperty) { "" } else { [string]$recordingModeProperty.Value }
Assert-Condition ([string]::IsNullOrWhiteSpace($recordingMode) -or $recordingMode -eq "ToggleToTalk") "The operator runner supports only ToggleToTalk mode."
[int]$hotkeyModifiers = $settings.hotkeyModifiers
[int]$hotkeyVirtualKey = $settings.hotkeyVirtualKey
$hotkeyDisplay = Get-HotkeyDisplay -Modifiers $hotkeyModifiers -VirtualKey $hotkeyVirtualKey
$providerId = [string]$settings.transcriptionProviderId
$modelId = [string]$settings.transcriptionModelId
Assert-Condition (-not [string]::IsNullOrWhiteSpace($providerId) -and -not [string]::IsNullOrWhiteSpace($modelId)) "Provider/model settings are unavailable."

$appPath = Join-Path -Path $script:RepoRoot -ChildPath "src\DictateAnywhere.App\bin\Release\net8.0-windows\DictateAnywhere.App.exe"
Assert-Condition (Test-Path -LiteralPath $appPath) "Build the Release application before collecting evidence."
$fixtures = @{}
foreach ($seconds in @(3, 7, 11)) {
  $path = Join-Path -Path $resolvedFixtureDirectory -ChildPath "cohere-healthcheck-$seconds.wav"
  Assert-Condition (Test-Path -LiteralPath $path) "Missing $seconds-second fixture at $path"
  $duration = Get-WavDurationMs -Path $path
  Assert-Condition ([math]::Abs($duration - ($seconds * 1000)) -le 1) "$seconds-second fixture duration is $duration ms."
  $fixtures[$seconds] = $path
}

$target = Start-ScratchTarget -Path $resolvedTarget
$targetFileName = [IO.Path]::GetFileName($resolvedTarget)
$matrixStartedUtc = [datetimeoffset]::UtcNow
$warmSession = $null
$coldRequirements = if ($Mode -eq "Replacement") { [ordered]@{ 7 = 1 } } else { [ordered]@{ 3 = $SamplesPerBucket; 7 = $SamplesPerBucket; 11 = $SamplesPerBucket } }
$warmRequirements = if ($Mode -eq "Replacement") { [ordered]@{ 3 = 1; 7 = 2; 11 = 1 } } else { [ordered]@{ 3 = $SamplesPerBucket; 7 = $SamplesPerBucket; 11 = $SamplesPerBucket } }
$labelSuffix = if ($Mode -eq "Replacement") { "R" } else { "" }
try {
  foreach ($entry in $coldRequirements.GetEnumerator()) {
    [int]$seconds = $entry.Key
    [int]$requiredAccepted = $entry.Value
    $acceptedCount = 0
    $attempt = 0
    while ($acceptedCount -lt $requiredAccepted -and $attempt -lt ($requiredAccepted + 5)) {
      $attempt++
      $session = Start-OwnedApplication -AppPath $appPath -ExpectedHotkeyDisplay $hotkeyDisplay
      try {
        $label = "COLD-$seconds-$labelSuffix$($attempt.ToString('00'))"
        Invoke-OneSample -AppSession $session -TargetProcess $target -TargetFileName $targetFileName `
          -Label $label -Temperature "cold" -NominalSeconds $seconds -FixturePath $fixtures[$seconds] `
          -HotkeyModifiers $hotkeyModifiers -HotkeyVirtualKey $hotkeyVirtualKey -ProviderId $providerId -ModelId $modelId
        if ($script:RawSamples[-1].acceptedByAutomation) {
          $acceptedCount++
        }
      }
      finally {
        Stop-OwnedApplication -Process $session.Process
        $script:OwnedAppProcess = $null
      }
      Start-Sleep -Milliseconds 500
    }
    Assert-Condition ($acceptedCount -eq $requiredAccepted) "Unable to collect $requiredAccepted accepted cold $seconds-second samples within the bounded attempt limit."
  }

  $warmSession = Start-OwnedApplication -AppPath $appPath -ExpectedHotkeyDisplay $hotkeyDisplay
  $null = Wait-ForLogRecord -SessionStartedUtc $warmSession.StartedUtc -AfterUtc $warmSession.StartedUtc `
    -Predicate { param($entry) $entry.message -eq "Cohere worker warmup completed." } `
    -TimeoutSeconds $CompletionTimeoutSeconds -Description "warm worker readiness" -AppProcess $warmSession.Process
  foreach ($entry in $warmRequirements.GetEnumerator()) {
    [int]$seconds = $entry.Key
    [int]$requiredAccepted = $entry.Value
    $acceptedCount = 0
    $attempt = 0
    while ($acceptedCount -lt $requiredAccepted -and $attempt -lt ($requiredAccepted + 5)) {
      $attempt++
      $label = "WARM-$seconds-$labelSuffix$($attempt.ToString('00'))"
      Invoke-OneSample -AppSession $warmSession -TargetProcess $target -TargetFileName $targetFileName `
        -Label $label -Temperature "warm" -NominalSeconds $seconds -FixturePath $fixtures[$seconds] `
        -HotkeyModifiers $hotkeyModifiers -HotkeyVirtualKey $hotkeyVirtualKey -ProviderId $providerId -ModelId $modelId
      if ($script:RawSamples[-1].acceptedByAutomation) {
        $acceptedCount++
      }
    }
    Assert-Condition ($acceptedCount -eq $requiredAccepted) "Unable to collect $requiredAccepted accepted warm $seconds-second samples within the bounded attempt limit."
  }

  $raw = [pscustomobject]@{
    schemaVersion = 1
    startedUtc = $matrixStartedUtc.ToString("o")
    completedUtc = [datetimeoffset]::UtcNow.ToString("o")
    providerId = $providerId
    modelId = $modelId
    hotkey = [pscustomobject]@{ modifiers = $hotkeyModifiers; virtualKey = $hotkeyVirtualKey }
    recordingMode = "ToggleToTalk"
    durationAcceptance = [pscustomobject]@{ earlyToleranceMs = $DurationEarlyToleranceMs; lateToleranceMs = $DurationLateToleranceMs }
    samples = $script:RawSamples
    processSamples = $script:ProcessSamples
    operatorVerification = "PENDING"
    note = "Transcript text, window handles, and unrelated process details are intentionally absent."
  }
  $rawName = if ($Mode -eq "Replacement") { "application-evidence-replacements-raw.json" } else { "application-evidence-raw.json" }
  $rawPath = Join-Path -Path $resolvedOutput -ChildPath $rawName
  $raw | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $rawPath -Encoding UTF8
  Write-Host "Matrix captured at $rawPath"
  Write-Host "Leave the owned Koncus Nai application running. Verify the labelled scratch results, then exit Koncus Nai through its tray command for graceful-shutdown evidence."
}
catch {
  if ($null -ne $script:OwnedAppProcess) {
    Stop-OwnedApplication -Process $script:OwnedAppProcess
    $script:OwnedAppProcess = $null
  }
  throw
}
