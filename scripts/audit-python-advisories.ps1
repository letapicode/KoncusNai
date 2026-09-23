[CmdletBinding()]
param(
  [string]$OutputPath = 'artifacts/release-audit/python-advisories.json',
  [switch]$FailOnFindings
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$inventory = Get-Content -LiteralPath (Join-Path $root 'docs/security/python-package-provenance.json') -Raw | ConvertFrom-Json
$packages = @($inventory.packages)
if ($packages.Count -eq 0) { throw 'Python provenance inventory is empty.' }
$queries = @($packages | ForEach-Object {
  if (-not $_.name -or -not $_.version) { throw 'Python inventory entry lacks a name or version.' }
  @{ package = @{ name = $_.name; ecosystem = 'PyPI' }; version = $_.version }
})
# Only public dependency names and versions leave the machine; no source or user data.
$response = Invoke-RestMethod -Uri 'https://api.osv.dev/v1/querybatch' -Method Post `
  -ContentType 'application/json' -Body (@{ queries = $queries } | ConvertTo-Json -Depth 6) -TimeoutSec 45
$results = @($response.results)
if ($results.Count -ne $packages.Count) { throw 'Advisory service returned an incomplete result set.' }
$findings = @()
foreach ($index in 0..($packages.Count - 1)) {
  $result = $results[$index]
  if ($result.PSObject.Properties['next_page_token'] -and $result.next_page_token) {
    throw 'Advisory response needs pagination; this scan cannot be marked complete.'
  }
  if ($result.PSObject.Properties['vulns'] -and @($result.vulns).Count -gt 0) {
    $findings += [pscustomobject]@{
      name = $packages[$index].name
      version = $packages[$index].version
      vulnerabilities = @($result.vulns)
    }
  }
}
$destination = if ([IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $root $OutputPath }
$destination = [IO.Path]::GetFullPath($destination)
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
@{
  queriedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
  source = 'https://api.osv.dev/v1/querybatch'
  queries = $packages.Count
  findings = $findings
  limitation = 'Declared PyPI version matches, not proof of reachability or coverage of embedded native libraries.'
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $destination -Encoding utf8
Write-Host "Queried $($packages.Count) Python package versions; $($findings.Count) have advisory matches. Report: $destination"
if ($FailOnFindings -and $findings.Count -gt 0) {
  throw 'Python dependency advisories require remediation or documented reachability review before release.'
}
