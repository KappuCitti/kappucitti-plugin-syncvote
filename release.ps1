<#
.SYNOPSIS
    Checks that a release can be made and, on request, creates and pushes its tag.
.DESCRIPTION
    The release itself (zips, GitHub release, manifest.json) is made by the "Build and Release" workflow
    when the tag is pushed. This script only prepares it:
      - it refuses to go on when the working tree is dirty, the branch is not the default one or behind
        the remote, or the tag already exists;
      - it runs the unit tests and builds every package, to be sure the tag will build (see ./build.ps1);
      - with -Tag it creates the annotated tag, with -Push it also pushes it, which starts the workflow.
.PARAMETER Version
    Release number "X.Y.Z" (also the tag). Packages get X.Y.Z.0 (Jellyfin 10.10), X.Y.Z.1 (10.11), X.Y.Z.2 (12).
.PARAMETER Tag
    Create the tag locally once the checks pass.
.PARAMETER Push
    Create the tag and push it to origin.
.EXAMPLE
    ./release.ps1 -Version 0.2.0            # checks only
.EXAMPLE
    ./release.ps1 -Version 0.2.0 -Push      # checks, tag, push
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [switch]$Tag,

    [switch]$Push
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')." }

function Fail($message) { throw "Not releasing: $message" }

# Where we are
$default = ((git symbolic-ref --short refs/remotes/origin/HEAD) -replace '^origin/', '')
if ((git branch --show-current) -ne $default) { Fail "switch to '$default' first." }
if (git status --porcelain) { Fail "the working tree has uncommitted changes." }
git fetch origin --tags --quiet
if ((git rev-list --count "HEAD..origin/$default") -gt 0) { Fail "'$default' is behind origin: pull first." }
if (git tag --list $Version) { Fail "the tag $Version already exists." }
$manifest = @(Get-Content manifest.json -Raw | ConvertFrom-Json)[0]
if ($manifest.versions | Where-Object { $_.version -like "$Version.*" }) { Fail "manifest.json already lists $Version." }

# Does it build and pass? (nothing is published, the zips go to ./artifacts)
./build.ps1 -ReleaseVersion $Version
Write-Host "Checks passed for $Version." -ForegroundColor Green

if ($Tag -or $Push) {
    git tag -a $Version -m "Release $Version"
    Write-Host "Created tag $Version." -ForegroundColor Green
}

if ($Push) {
    git push origin $Version
    Write-Host "Pushed tag ${Version}: the release workflow takes it from here." -ForegroundColor Green
}
elseif (-not $Tag) {
    Write-Host "Nothing was tagged. Run again with -Tag, or -Push to start the release." -ForegroundColor Yellow
}
