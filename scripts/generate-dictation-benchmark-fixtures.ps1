param([string]$OutputDirectory = 'artifacts/dictation-latency/fixtures')
$ErrorActionPreference = 'Stop'
$fixtureRoot = [IO.Path]::GetFullPath($OutputDirectory)
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\') + '\'
if ($fixtureRoot.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase) -and
    -not $fixtureRoot.StartsWith((Join-Path $repoRoot 'artifacts\'), [StringComparison]::OrdinalIgnoreCase)) {
  throw 'Benchmark recordings and reference transcripts must be under ignored artifacts/ or outside the checkout.'
}
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
$texts = @(
  @{ id = 'short'; text = 'Please open the meeting notes.' },
  @{ id = 'medium'; text = 'Sarah and Michael will meet in Boston on Tuesday. Please bring the updated project schedule.' },
  @{ id = 'numbers'; text = 'Order number four hundred twenty seven costs nineteen dollars. The appointment is at three thirty on September twenty nine.' },
  @{ id = 'long'; text = 'Please review the project plan before the team meeting. Sarah will prepare the design notes and Michael will check the budget. We need to finish the first milestone on Tuesday and the second milestone on Friday. The office in Boston will receive the package at three thirty. Remember to include the meeting notes, the updated schedule, and the list of open questions. After the review, please send a short summary to the team. We should discuss the expected costs and agree on a clear date for the next release. If the schedule changes, update the plan and explain the reason. The final report should describe what changed, how it was tested, and which decisions still need attention.' }
)
$voice = New-Object -ComObject SAPI.SpVoice
$voice.Rate = 0
$manifest = @()
foreach ($entry in $texts) {
  $wavePath = Join-Path $fixtureRoot ($entry.id + '.wav')
  $stream = New-Object -ComObject SAPI.SpFileStream
  $stream.Format.Type = 18
  $stream.Open($wavePath, 3, $false)
  $voice.AudioOutputStream = $stream
  $null = $voice.Speak($entry.text)
  $stream.Close()
  $manifest += @{ id = $entry.id; audio = ($entry.id + '.wav'); reference = $entry.text; language = 'en'; source = 'synthetic-windows-sapi'; voice = $voice.Voice.GetDescription() }
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $fixtureRoot 'manifest.json') -Encoding utf8
Write-Output "Generated $($manifest.Count) synthetic fixtures."
