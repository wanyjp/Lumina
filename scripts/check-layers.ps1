#requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

# Allowed project-reference direction. Domain must stay dependency-free.
$allowed = @{
    'Lumina.Domain'         = @()
    'Lumina.Data'           = @('Lumina.Domain')
    'Lumina.Services'       = @('Lumina.Domain')
    'Lumina.SystemAdapters' = @('Lumina.Domain', 'Lumina.Services')
    'Lumina.UI.Wpf'         = @('Lumina.Domain', 'Lumina.Services')
    'Lumina.Widgets'        = @('Lumina.Domain', 'Lumina.Services', 'Lumina.UI.Wpf')
    'Lumina.App'            = @('Lumina.Domain', 'Lumina.Data', 'Lumina.Services', 'Lumina.SystemAdapters', 'Lumina.UI.Wpf', 'Lumina.Widgets')
    'Lumina.Tests'          = @('Lumina.Domain', 'Lumina.Data', 'Lumina.Services')
}

$projects = Get-ChildItem -Path $repoRoot -Recurse -Filter *.csproj |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

$violations = New-Object System.Collections.Generic.List[string]
$checked = 0

foreach ($project in $projects) {
    $name = $project.BaseName
    if (-not $allowed.ContainsKey($name)) {
        $violations.Add("UNKNOWN_PROJECT: $name")
        continue
    }

    [xml]$xml = Get-Content -Raw -LiteralPath $project.FullName
    $references = @()
    foreach ($node in $xml.SelectNodes('//ProjectReference')) {
        $include = $node.GetAttribute('Include')
        if ($include) {
            $references += [System.IO.Path]::GetFileNameWithoutExtension($include)
        }
    }

    foreach ($reference in $references) {
        if ($allowed[$name] -notcontains $reference) {
            $violations.Add("FORBIDDEN: $name -> $reference")
        }
    }

    $checked++
}

if ($violations.Count -gt 0) {
    Write-Host 'Layer dependency violations:'
    foreach ($violation in $violations) {
        Write-Host "  $violation"
    }
    throw "Layer dependency check failed with $($violations.Count) violation(s)."
}

Write-Host "[ok] layer dependency check passed ($checked projects)."
