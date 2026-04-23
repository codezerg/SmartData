#!/usr/bin/env pwsh
# Pack all SmartData projects with a single, frozen version stamp shared
# across every project — prevents transitive ProjectReference package
# versions from skewing when DateTime.UtcNow re-evaluates per-project.
#
# Usage:  pwsh ./scripts/pack.ps1 [-Configuration Release]

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$stamp = (Get-Date).ToUniversalTime().ToString("yy.M.d-HHmmss")

Write-Host "Packing SmartData with stamp: $stamp"

$env:SMARTDATA_STAMP = $stamp
try {
    dotnet pack (Join-Path $repoRoot "SmartData.slnx") -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed (exit $LASTEXITCODE)" }
} finally {
    Remove-Item Env:\SMARTDATA_STAMP -ErrorAction SilentlyContinue
}

Write-Host "Done. Artifacts: $repoRoot\artifacts\"
