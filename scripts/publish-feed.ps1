<#
.SYNOPSIS
  Builds a NuGet v3 static feed under site/public/nuget/ from .nupkg files in artifacts/.

.DESCRIPTION
  Emits flatcontainer (PackageBaseAddress/3.0.0) + registration (RegistrationsBaseUrl/3.6.0)
  resources plus a service index. Output is pure static files — Astro copies public/ verbatim
  during `npm run build`.

  Consumers add the feed with:
    dotnet nuget add source <BaseUrl>/nuget/v3/index.json --name SmartData

.PARAMETER BaseUrl
  Absolute site origin (and base path if any), e.g. https://smartdata.example.com
  or https://codezerg.github.io/SmartData. No trailing slash.

.PARAMETER ArtifactsDir
  Source .nupkg directory. Defaults to <repo>/artifacts.

.PARAMETER OutputDir
  Feed output directory. Defaults to <repo>/site/public/nuget.

.EXAMPLE
  pwsh scripts/publish-feed.ps1 -BaseUrl https://codezerg.github.io/SmartData
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $BaseUrl,

    [string] $ArtifactsDir,
    [string] $OutputDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ArtifactsDir) { $ArtifactsDir = Join-Path $repoRoot 'artifacts' }
if (-not $OutputDir)    { $OutputDir    = Join-Path $repoRoot 'site\public\nuget' }

$BaseUrl = $BaseUrl.TrimEnd('/')
$feedBase = "$BaseUrl/nuget/v3"

Write-Host "Source:  $ArtifactsDir"
Write-Host "Output:  $OutputDir"
Write-Host "FeedUrl: $feedBase/index.json"

if (-not (Test-Path $ArtifactsDir)) { throw "Artifacts dir not found: $ArtifactsDir" }

# Clean output
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
$null = New-Item -ItemType Directory -Path $OutputDir -Force
$v3Dir       = Join-Path $OutputDir 'v3'
$flatDir     = Join-Path $v3Dir     'flatcontainer'
$regDir      = Join-Path $v3Dir     'registration'
$null = New-Item -ItemType Directory -Path $flatDir -Force
$null = New-Item -ItemType Directory -Path $regDir  -Force

function Read-Nuspec {
    param([string] $NupkgPath)
    $zip = [System.IO.Compression.ZipFile]::OpenRead($NupkgPath)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -match '^[^/]+\.nuspec$' } | Select-Object -First 1
        if (-not $entry) { throw "No .nuspec in $NupkgPath" }
        $stream = $entry.Open()
        try {
            $reader = New-Object System.IO.StreamReader($stream)
            try { return [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
        } finally { $stream.Dispose() }
    } finally { $zip.Dispose() }
}

# Collect + group packages by id
$packages = @{}
foreach ($file in Get-ChildItem -Path $ArtifactsDir -Filter '*.nupkg' -File) {
    $nuspec = Read-Nuspec $file.FullName
    $meta   = $nuspec.package.metadata
    $id     = [string]$meta.id
    $ver    = [string]$meta.version
    $idLow  = $id.ToLowerInvariant()

    if (-not $packages.ContainsKey($idLow)) { $packages[$idLow] = @() }
    $packages[$idLow] += [pscustomobject]@{
        Id       = $id
        IdLower  = $idLow
        Version  = $ver
        Nupkg    = $file.FullName
        FileSize = $file.Length
        Nuspec   = $nuspec
        Meta     = $meta
    }
}

if ($packages.Count -eq 0) { throw "No .nupkg files in $ArtifactsDir" }

# Emit flatcontainer + per-version nuspec + copy nupkg
foreach ($idLow in $packages.Keys) {
    $pkgRoot = Join-Path $flatDir $idLow
    $null = New-Item -ItemType Directory -Path $pkgRoot -Force

    $versions = @()
    foreach ($p in ($packages[$idLow] | Sort-Object Version)) {
        $versions += $p.Version
        $verDir = Join-Path $pkgRoot $p.Version
        $null = New-Item -ItemType Directory -Path $verDir -Force

        Copy-Item $p.Nupkg (Join-Path $verDir "$idLow.$($p.Version).nupkg") -Force

        # Extract nuspec next to the nupkg (spec requires this)
        $zip = [System.IO.Compression.ZipFile]::OpenRead($p.Nupkg)
        try {
            $entry = $zip.Entries | Where-Object { $_.FullName -match '^[^/]+\.nuspec$' } | Select-Object -First 1
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile(
                $entry, (Join-Path $verDir "$idLow.nuspec"), $true)
        } finally { $zip.Dispose() }
    }

    $versionsJson = [pscustomobject]@{ versions = $versions } | ConvertTo-Json -Depth 5
    Set-Content -Path (Join-Path $pkgRoot 'index.json') -Value $versionsJson -Encoding utf8
}

# Emit registration (inline single-page form)
foreach ($idLow in $packages.Keys) {
    $pkgReg = Join-Path $regDir $idLow
    $null = New-Item -ItemType Directory -Path $pkgReg -Force
    $regIndexUrl = "$feedBase/registration/$idLow/index.json"

    $items = @()
    $sorted = $packages[$idLow] | Sort-Object Version
    foreach ($p in $sorted) {
        $meta = $p.Meta

        # Dependency groups
        $depGroups = @()
        $groupNodes = $meta.dependencies.group
        if ($groupNodes) {
            foreach ($g in @($groupNodes)) {
                $deps = @()
                if ($g.dependency) {
                    foreach ($d in @($g.dependency)) {
                        $deps += [pscustomobject]@{
                            '@id'       = "$feedBase/registration/$idLow/index.json#dep"
                            '@type'     = 'PackageDependency'
                            id          = [string]$d.id
                            range       = [string]$d.version
                        }
                    }
                }
                $depGroups += [pscustomobject]@{
                    '@id'            = "$feedBase/registration/$idLow/index.json#group"
                    '@type'          = 'PackageDependencyGroup'
                    targetFramework  = [string]$g.targetFramework
                    dependencies     = $deps
                }
            }
        }

        $catalogEntry = [ordered]@{
            '@id'          = "$feedBase/registration/$idLow/$($p.Version).json#catalog"
            '@type'        = 'PackageDetails'
            id             = $p.Id
            version        = $p.Version
            authors        = [string]$meta.authors
            description    = [string]$meta.description
            listed         = $true
            packageContent = "$feedBase/flatcontainer/$idLow/$($p.Version)/$idLow.$($p.Version).nupkg"
            published      = (Get-Date).ToUniversalTime().ToString('o')
        }
        if ($depGroups.Count -gt 0) { $catalogEntry['dependencyGroups'] = $depGroups }

        $leafUrl = "$feedBase/registration/$idLow/$($p.Version).json"

        $leaf = [ordered]@{
            '@id'            = $leafUrl
            '@type'          = @('Package','http://schema.nuget.org/catalog#Permalink')
            catalogEntry     = $catalogEntry
            packageContent   = $catalogEntry.packageContent
            registration     = $regIndexUrl
        }
        Set-Content -Path (Join-Path $pkgReg "$($p.Version).json") `
                    -Value ($leaf | ConvertTo-Json -Depth 20) -Encoding utf8

        $items += [ordered]@{
            '@id'          = "$leafUrl#page"
            '@type'        = 'Package'
            catalogEntry   = $catalogEntry
            packageContent = $catalogEntry.packageContent
            registration   = $regIndexUrl
        }
    }

    $lower = ($sorted | Select-Object -First 1).Version
    $upper = ($sorted | Select-Object -Last  1).Version

    $index = [ordered]@{
        '@id'        = $regIndexUrl
        '@type'      = @('catalog:CatalogRoot','PackageRegistration','catalog:Permalink')
        count        = 1
        items        = @(
            [ordered]@{
                '@id'   = "$regIndexUrl#page"
                '@type' = 'catalog:CatalogPage'
                count   = $items.Count
                lower   = $lower
                upper   = $upper
                items   = $items
                parent  = $regIndexUrl
            }
        )
        '@context'   = [ordered]@{
            '@vocab'     = 'http://schema.nuget.org/schema#'
            catalog      = 'http://schema.nuget.org/catalog#'
        }
    }
    Set-Content -Path (Join-Path $pkgReg 'index.json') `
                -Value ($index | ConvertTo-Json -Depth 20) -Encoding utf8
}

# Service index
$serviceIndex = [ordered]@{
    version   = '3.0.0'
    resources = @(
        [ordered]@{
            '@id'      = "$feedBase/flatcontainer/"
            '@type'    = 'PackageBaseAddress/3.0.0'
            comment    = 'Base URL for package content (nupkg + nuspec)'
        },
        [ordered]@{
            '@id'      = "$feedBase/registration/"
            '@type'    = 'RegistrationsBaseUrl/3.6.0'
            comment    = 'Registration (metadata + dependency) resource, SemVer2'
        }
    )
}
Set-Content -Path (Join-Path $v3Dir 'index.json') `
            -Value ($serviceIndex | ConvertTo-Json -Depth 10) -Encoding utf8

Write-Host ""
Write-Host "Feed written: $(($packages.Values | Measure-Object).Count) id(s), $((($packages.Values | ForEach-Object { $_ }) | Measure-Object).Count) version(s)"
Write-Host "Add source:"
Write-Host "  dotnet nuget add source $feedBase/index.json --name SmartData"
