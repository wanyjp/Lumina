#requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

$solution = 'Lumina.sln'

# Before P0-03 there is no solution. Skip so CI stays green during bootstrap.
if (-not (Test-Path -LiteralPath $solution)) {
    Write-Host '[skip] Lumina.sln not found. It will be created in P0-03; full verification runs after that.'
    exit 0
}

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    Write-Host ''
    Write-Host "==> $Name"
    & $Action
    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "Step failed: $Name (exit code $LASTEXITCODE)"
    }
}

Invoke-Step 'layer dependency check' { & (Join-Path $repoRoot 'scripts\check-layers.ps1') }
Invoke-Step 'dotnet restore' { dotnet restore $solution }
Invoke-Step 'dotnet build' { dotnet build $solution -c Release --no-restore }
Invoke-Step 'xunit tests' { dotnet run --project Lumina.Tests/Lumina.Tests.csproj -c Release --no-build }
Invoke-Step 'dotnet format' { dotnet format $solution --verify-no-changes --no-restore --verbosity minimal }

Write-Host ''
Write-Host '[ok] restore, build, test and format checks passed.'
