param(
  [string]$PolicyPath = "docs/architecture/project-reference-guardrails.json",
  [string]$OutputDirectory = "artifacts/architecture",
  [string]$ReportPath = "docs/release/project-reference-guardrails-report.md"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Resolve-RepoRoot {
  return [IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath ".."))
}

function Resolve-RepoPath {
  param(
    [Parameter(Mandatory = $true)][string]$PathValue,
    [Parameter(Mandatory = $true)][string]$RepoRoot
  )

  if ([IO.Path]::IsPathRooted($PathValue)) {
    return [IO.Path]::GetFullPath($PathValue)
  }

  return [IO.Path]::GetFullPath((Join-Path -Path $RepoRoot -ChildPath $PathValue))
}

function Get-ProjectFiles {
  param([Parameter(Mandatory = $true)][string]$RepoRoot)

  $projectRoots = @("src", "tools") | ForEach-Object {
    Join-Path -Path $RepoRoot -ChildPath $_
  }

  return Get-ChildItem -Path $projectRoots -Filter *.csproj -Recurse -File |
    Where-Object {
      $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
      $_.Name -notlike '*_wpftmp.csproj'
    }
}

function Get-ProjectNameFromXml {
  param(
    [Parameter(Mandatory = $true)][xml]$ProjectXml,
    [Parameter(Mandatory = $true)][string]$DefaultName
  )

  $assemblyNameNode = $ProjectXml.SelectSingleNode("/Project/PropertyGroup/AssemblyName")
  if ($null -ne $assemblyNameNode -and -not [string]::IsNullOrWhiteSpace([string]$assemblyNameNode.InnerText)) {
    return [string]$assemblyNameNode.InnerText
  }

  return $DefaultName
}

function Get-ProjectGraph {
  param([Parameter(Mandatory = $true)][string]$RepoRoot)

  $projectFiles = Get-ProjectFiles -RepoRoot $RepoRoot
  $projectsByPath = @{}

  foreach ($projectFile in $projectFiles) {
    [xml]$projectXml = Get-Content -Path $projectFile.FullName -Raw
    $projectName = Get-ProjectNameFromXml -ProjectXml $projectXml -DefaultName ([IO.Path]::GetFileNameWithoutExtension($projectFile.Name))
    $projectsByPath[[IO.Path]::GetFullPath($projectFile.FullName)] = [PSCustomObject]@{
      Name = $projectName
      Path = [IO.Path]::GetFullPath($projectFile.FullName)
      References = New-Object System.Collections.Generic.List[string]
    }
  }

  foreach ($projectEntry in $projectsByPath.Values) {
    [xml]$projectXml = Get-Content -Path $projectEntry.Path -Raw
    $projectDirectory = Split-Path -Parent $projectEntry.Path
    $projectReferences = @($projectXml.SelectNodes("//ProjectReference"))

    foreach ($projectReference in $projectReferences) {
      if ($null -eq $projectReference) {
        continue
      }

      $includePath = [string]$projectReference.Include
      if ([string]::IsNullOrWhiteSpace($includePath)) {
        continue
      }

      $resolvedPath = [IO.Path]::GetFullPath((Join-Path -Path $projectDirectory -ChildPath $includePath))
      if (-not $projectsByPath.ContainsKey($resolvedPath)) {
        throw "Project '$($projectEntry.Name)' references unknown project path '$includePath'."
      }

      $projectEntry.References.Add([string]$projectsByPath[$resolvedPath].Name)
    }
  }

  return $projectsByPath.Values | Sort-Object Name
}

function Find-Cycles {
  param([Parameter(Mandatory = $true)][object[]]$Projects)

  $projectMap = @{}
  foreach ($project in $Projects) {
    $projectMap[$project.Name] = $project
  }

  $visited = @{}
  $active = @{}
  $cycles = New-Object System.Collections.Generic.List[string]

  function Visit {
    param(
      [Parameter(Mandatory = $true)][string]$ProjectName,
      [System.Collections.Generic.List[string]]$PathStack
    )

    if ($active.ContainsKey($ProjectName)) {
      $cycleStart = $PathStack.IndexOf($ProjectName)
      if ($cycleStart -ge 0) {
        $cycleNodes = $PathStack.GetRange($cycleStart, $PathStack.Count - $cycleStart)
        $cycleNodes.Add($ProjectName)
        $cycles.Add(($cycleNodes -join " -> "))
      }

      return
    }

    if ($visited.ContainsKey($ProjectName)) {
      return
    }

    $visited[$ProjectName] = $true
    $active[$ProjectName] = $true
    $PathStack.Add($ProjectName)

    foreach ($referenceName in $projectMap[$ProjectName].References) {
      Visit -ProjectName $referenceName -PathStack $PathStack
    }

    $null = $PathStack.RemoveAt($PathStack.Count - 1)
    $active.Remove($ProjectName)
  }

  foreach ($project in $Projects) {
    Visit -ProjectName $project.Name -PathStack (New-Object System.Collections.Generic.List[string])
  }

  return $cycles
}

$repoRoot = Resolve-RepoRoot
$resolvedPolicyPath = Resolve-RepoPath -PathValue $PolicyPath -RepoRoot $repoRoot
$resolvedOutputDirectory = Resolve-RepoPath -PathValue $OutputDirectory -RepoRoot $repoRoot
$resolvedReportPath = Resolve-RepoPath -PathValue $ReportPath -RepoRoot $repoRoot

if (-not (Test-Path -Path $resolvedPolicyPath -PathType Leaf)) {
  throw "Guardrail policy not found: $resolvedPolicyPath"
}

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedReportPath) -Force | Out-Null

$projects = @(Get-ProjectGraph -RepoRoot $repoRoot)
$policy = Get-Content -Path $resolvedPolicyPath -Raw | ConvertFrom-Json
$guardrails = @($policy.guardrails)

$projectMap = @{}
foreach ($project in $projects) {
  $projectMap[$project.Name] = $project
}

$violations = New-Object System.Collections.Generic.List[string]
$policyMap = @{}

foreach ($guardrail in $guardrails) {
  $policyMap[$guardrail.project] = $guardrail

  if (-not $projectMap.ContainsKey($guardrail.project)) {
    $violations.Add("Policy references unknown project '$($guardrail.project)'.")
    continue
  }

  $allowedReferences = @($guardrail.allowedProjectReferences | ForEach-Object { [string]$_ })

  foreach ($actualReference in $projectMap[$guardrail.project].References) {
    if (-not ($allowedReferences -ccontains [string]$actualReference)) {
      $violations.Add("Project '$($guardrail.project)' has disallowed project reference '$actualReference'.")
    }
  }
}

foreach ($project in $projects) {
  if (-not $policyMap.ContainsKey($project.Name)) {
    $violations.Add("Project '$($project.Name)' is missing from the guardrail policy.")
  }
}

$cycles = @(Find-Cycles -Projects $projects)
foreach ($cycle in $cycles) {
  $violations.Add("Circular dependency detected: $cycle")
}

$summary = [PSCustomObject]@{
  generatedAt = (Get-Date).ToString("o")
  policyPath = $resolvedPolicyPath
  projects = @(
    foreach ($project in $projects) {
      [PSCustomObject]@{
        name = $project.Name
        references = @($project.References | Sort-Object)
      }
    }
  )
  violations = @($violations)
}

$summaryPath = Join-Path -Path $resolvedOutputDirectory -ChildPath "project-reference-guardrails-results.json"
$summary | ConvertTo-Json -Depth 6 | Set-Content -Path $summaryPath -Encoding UTF8

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# Project Reference Guardrails Report")
$reportLines.Add("")
$reportLines.Add("- Generated: $(Get-Date -Format o)")
$reportLines.Add("- Policy: $resolvedPolicyPath")
$reportLines.Add("- Summary JSON: $summaryPath")
$reportLines.Add("")
$reportLines.Add("| Project | References |")
$reportLines.Add("|---|---|")
foreach ($project in $projects) {
  $references = if ($project.References.Count -gt 0) { ($project.References | Sort-Object) -join ", " } else { "(none)" }
  $reportLines.Add("| $($project.Name) | $references |")
}
$reportLines.Add("")
$reportLines.Add("## Result")
if ($violations.Count -eq 0) {
  $reportLines.Add("- PASS")
}
else {
  $reportLines.Add("- FAIL")
  foreach ($violation in $violations) {
    $reportLines.Add("- $violation")
  }
}
$reportLines | Set-Content -Path $resolvedReportPath -Encoding UTF8

Write-Host "Report: $resolvedReportPath"
Write-Host "JSON:   $summaryPath"

if ($violations.Count -gt 0) {
  throw "Project reference guardrails failed."
}
