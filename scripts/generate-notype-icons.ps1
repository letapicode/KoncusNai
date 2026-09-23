[CmdletBinding()]
param([string]$OutputDirectory = "")
# Compatibility entry point. The Koncus Nai SVG is the only drawing source.
& (Join-Path $PSScriptRoot "generate-koncus-nai-icons.ps1") -OutputDirectory $OutputDirectory
