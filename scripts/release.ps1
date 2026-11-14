<#
.SYNOPSIS
Creates a gitflow release: tests, publishes all platforms, zips, builds .deb/.rpm (needs nfpm),
merges to master/develop, tags, creates a GitHub release and updates the Scoop bucket and Homebrew tap.

.EXAMPLE
.\scripts\release.ps1 -Version 1.2.0
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string[]]$Runtimes = @('win-x64', 'linux-x64', 'osx-arm64', 'osx-x64'),
    [string]$GitHubRepo = 'nmandzyk/TimeTracker',
    [string]$ScoopBucketRepo = 'nmandzyk/scoop-bucket',
    [string]$HomebrewTapRepo = 'nmandzyk/homebrew-tap',
    [switch]$SkipTests,
    [switch]$NoGitHubRelease,
    [switch]$SkipPackageManagers
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

function Invoke-Step {
    param([scriptblock]$Command)
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "Command failed (exit code $LASTEXITCODE): $Command" }
}

function Expand-Template {
    param([string]$Path, [hashtable]$Values)
    $text = [IO.File]::ReadAllText((Resolve-Path $Path))
    foreach ($key in $Values.Keys) { $text = $text.Replace("{{$key}}", $Values[$key]) }
    $text
}

function Write-Utf8NoBom {
    param([string]$Path, [string]$Content)
    $dir = Split-Path $Path -Parent
    if ($dir) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

# Clones (creating it if missing) a package repo, writes the given files and pushes the change.
function Publish-PackageRepo {
    param([string]$Repo, [string]$Description, [hashtable]$Files, [string]$Message)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    gh repo view $Repo *> $null
    $exists = $LASTEXITCODE -eq 0
    $ErrorActionPreference = $previous

    if (-not $exists) {
        Write-Host "    Creating GitHub repo $Repo"
        Invoke-Step { gh repo create $Repo --public --description $Description }
    }

    $work = Join-Path ([IO.Path]::GetTempPath()) "tt-pkg-$([guid]::NewGuid())"
    try {
        Invoke-Step { gh repo clone $Repo $work }
        foreach ($file in $Files.Keys) { Write-Utf8NoBom (Join-Path $work $file) $Files[$file] }
        Invoke-Step { git -C $work add -A }
        if (git -C $work status --porcelain) {
            Invoke-Step { git -C $work commit -m $Message }
            Invoke-Step { git -C $work push origin HEAD }
        }
        else {
            Write-Host "    $Repo is already up to date"
        }
    }
    finally {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
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

$assetBase = "https://github.com/$GitHubRepo/releases/download/$tag"
$zips = @{}
foreach ($rid in $Runtimes) {
    $name = "TimeTracker-$Version-$rid.zip"
    $zips[$rid] = @{
        Url    = "$assetBase/$name"
        Sha256 = (Get-FileHash (Join-Path $dist $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

if (-not $SkipPackageManagers -and $zips.ContainsKey('linux-x64')) {
    if (Get-Command nfpm -ErrorAction SilentlyContinue) {
        Write-Host '==> Building .deb and .rpm'
        $binary = (Resolve-Path 'publish/linux-x64/TimeTracker').Path -replace '\\', '/'
        $nfpmConfig = Join-Path $dist 'nfpm.yaml'
        Write-Utf8NoBom $nfpmConfig (Expand-Template 'packaging/nfpm.yaml.template' @{ VERSION = $Version; BINARY = $binary })
        foreach ($format in 'deb', 'rpm') {
            Invoke-Step { nfpm package --config $nfpmConfig --packager $format --target $dist }
        }
        Remove-Item $nfpmConfig
    }
    else {
        Write-Warning 'nfpm not found - skipping .deb/.rpm (install: scoop install nfpm / winget install goreleaser.nfpm).'
    }
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
    $assets = Get-ChildItem $dist -Include *.zip, *.deb, *.rpm -Recurse | ForEach-Object FullName
    Invoke-Step { gh release create $tag @assets --title $tag --generate-notes }

    if (-not $SkipPackageManagers) {
        if ($zips.ContainsKey('win-x64')) {
            Write-Host '==> Updating Scoop bucket'
            $manifest = Expand-Template 'packaging/scoop/timetracker.json.template' @{
                VERSION = $Version; WIN_X64_URL = $zips['win-x64'].Url; WIN_X64_SHA256 = $zips['win-x64'].Sha256
            }
            Publish-PackageRepo $ScoopBucketRepo 'Scoop bucket for TimeTracker' `
                @{ 'bucket/timetracker.json' = $manifest } "timetracker $Version"
        }

        if ($zips.ContainsKey('osx-arm64') -and $zips.ContainsKey('osx-x64') -and $zips.ContainsKey('linux-x64')) {
            Write-Host '==> Updating Homebrew tap'
            $formula = Expand-Template 'packaging/homebrew/timetracker.rb.template' @{
                VERSION          = $Version
                OSX_ARM64_URL    = $zips['osx-arm64'].Url; OSX_ARM64_SHA256 = $zips['osx-arm64'].Sha256
                OSX_X64_URL      = $zips['osx-x64'].Url;   OSX_X64_SHA256   = $zips['osx-x64'].Sha256
                LINUX_X64_URL    = $zips['linux-x64'].Url; LINUX_X64_SHA256 = $zips['linux-x64'].Sha256
            }
            Publish-PackageRepo $HomebrewTapRepo 'Homebrew tap for TimeTracker' `
                @{ 'Formula/timetracker.rb' = $formula } "timetracker $Version"
        }
        else {
            Write-Warning 'Skipping Homebrew tap: needs osx-arm64, osx-x64 and linux-x64 runtimes.'
        }
    }
}

Write-Host "Release $tag done."
