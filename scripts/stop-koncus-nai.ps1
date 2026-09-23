[CmdletBinding()]
param(
  [switch]$AllOllama,
  [ValidateRange(0, 30)]
  [int]$GracePeriodSeconds = 2
)
& (Join-Path $PSScriptRoot 'stop-notype.ps1') @PSBoundParameters
