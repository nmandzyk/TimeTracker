<#
.SYNOPSIS
Creates a gitflow release: tests, publishes all platforms, zips, merges to master/develop, tags and creates a GitHub release.

.EXAMPLE
.\scripts\release.ps1 -Version 1.2.0
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string[]]$Runtimes = @('win-x64', 'linux-x64', 'osx-arm64', 'osx-x64'),
    [switch]$SkipTests,
    [switch]$NoGitHubRelease
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

function Invoke-Step {
    param([scriptblock]$Command)
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "Command failed (exit code $LASTEXITCODE): $Command" }
}

$tag = "v$Version"
$branch = "release/$Version"

if (git status --porcelain) { throw 'Working tree is not clean. Commit or stash changes first.' }
if (Get-Process -Name TimeTracker -ErrorAction SilentlyContinue) {
    throw 'TimeTracker is running. Quit it (tray -> Quit) so the published exe is not locked.'
}
if (git tag --list $tag) { throw "Tag $tag already exists." }

Write-Host "==> Creating $branch from develop"
Invoke-Step { git checkout develop }
Invoke-Step { git pull origin develop }
Invoke-Step { git checkout -b $branch }

if (-not $SkipTests) {
    Write-Host '==> Running tests'
    Invoke-Step { dotnet test -c Release }
}

$dist = Join-Path 'publish' 'dist'
Remove-Item publish -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $dist -Force | Out-Null

foreach ($rid in $Runtimes) {
    Write-Host "==> Publishing $rid"
    Invoke-Step {
        dotnet publish src/TimeTracker.App -c Release -r $rid --self-contained `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:DebugType=None -p:Version=$Version -o "publish/$rid"
    }
    Compress-Archive -Path "publish/$rid/*" -DestinationPath (Join-Path $dist "TimeTracker-$Version-$rid.zip") -Force
}

Write-Host '==> Merging into master and tagging'
Invoke-Step { git checkout master }
Invoke-Step { git pull origin master }
Invoke-Step { git merge --no-ff $branch -m "Release $Version" }
Invoke-Step { git tag -a $tag -m "Release $Version" }

Write-Host '==> Merging back into develop'
Invoke-Step { git checkout develop }
Invoke-Step { git merge --no-ff $branch -m "Merge release $Version into develop" }

Write-Host '==> Pushing'
Invoke-Step { git push origin master develop }
Invoke-Step { git push origin $tag }
Invoke-Step { git branch -d $branch }

if (-not $NoGitHubRelease) {
    Write-Host '==> Creating GitHub release'
    $assets = Get-ChildItem $dist -Filter *.zip | ForEach-Object FullName
    Invoke-Step { gh release create $tag @assets --title $tag --generate-notes }
}

Write-Host "Release $tag done."
