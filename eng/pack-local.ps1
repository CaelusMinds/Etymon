<#
.SYNOPSIS
    Packs every shipping package into a local NuGet feed that other solutions
    on this machine can restore from.

.DESCRIPTION
    Publishing to nuget.org is permanent: a version cannot be deleted, only
    unlisted, and the number is spent either way. While Etymon is being built
    its first consumer should be restoring real packages, not project
    references -- so this packs to a folder and that folder is a NuGet source.

    A consuming solution needs one file at its root:

        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <add key="etymon-local" value="C:\Workspaces\.packages" />
          </packageSources>
        </configuration>

    and then references packages normally:

        <PackageVersion Include="Etymon.Core" Version="0.1.0-preview.*" />

.PARAMETER Feed
    Where to write the packages. Defaults to a sibling of the repository so it
    is shared by every solution on this machine.

.PARAMETER Suffix
    Prerelease suffix. Bump it when a consumer needs to pick up changes, since
    NuGet caches a given version permanently.

.EXAMPLE
    ./eng/pack-local.ps1 -Suffix preview.2
#>
[CmdletBinding()]
param(
    [string] $Feed = (Join-Path (Split-Path -Parent $PSScriptRoot) '..' | Join-Path -ChildPath '.packages'),
    [string] $Suffix = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

$Feed = [System.IO.Path]::GetFullPath($Feed)
if (-not (Test-Path $Feed)) {
    New-Item -ItemType Directory -Path $Feed -Force | Out-Null
    Write-Host "Created feed at $Feed"
}

$packArgs = @('pack', (Join-Path $repo 'Etymon.slnx'), '-c', 'Release', '-o', $Feed)
if ($Suffix) { $packArgs += "-p:VersionSuffix=$Suffix" }

Write-Host "Packing to $Feed" -ForegroundColor Cyan
& dotnet @packArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed with exit code $LASTEXITCODE" }

Write-Host ''
Write-Host 'Packages in the feed:' -ForegroundColor Cyan
Get-ChildItem $Feed -Filter *.nupkg |
    Sort-Object Name |
    ForEach-Object { '  {0,-48} {1,7:N1} KB' -f $_.Name, ($_.Length / 1KB) }

Write-Host ''
Write-Host 'A consumer that already restored one of these must clear its cache to see a rebuilt version:' -ForegroundColor DarkGray
Write-Host '  dotnet nuget locals http-cache --clear' -ForegroundColor DarkGray
