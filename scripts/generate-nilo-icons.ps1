[CmdletBinding()]
param([string]$OutputDirectory = '')
# Historical command alias.
& (Join-Path $PSScriptRoot 'generate-koncus-nai-icons.ps1') -OutputDirectory $OutputDirectory
