<#
.SYNOPSIS
    Builds a SyncVote release locally: one build per supported Jellyfin generation, manifest.json updated.
.DESCRIPTION
    Runs build.ps1, then (optionally) commits manifest.json, tags the release and creates the GitHub release.
    Nothing is pushed unless -Push is given.
.PARAMETER Version
    Release number "X.Y.Z" (a tag such as 0.2.0). The builds get 0.2.0.0 (Jellyfin 10.10), 0.2.0.1 (10.11), 0.2.0.2 (12).
.PARAMETER Push
    Commit manifest.json, create the tag and push both.
.PARAMETER CreateRelease
    With -Push: also create the GitHub release with the zips (needs the gh CLI).
.EXAMPLE
    ./release.ps1 -Version 0.2.0
.EXAMPLE
    ./release.ps1 -Version 0.2.0 -Push -CreateRelease
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [switch]$Push,

    [switch]$CreateRelease
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$repoUrl = (git remote get-url origin) -replace '\.git$', '' -replace 'git@github.com:', 'https://github.com/'
./build.ps1 -ReleaseVersion $Version -UpdateManifest -DownloadBaseUrl "$repoUrl/releases/download/$Version"

if ($Push) {
    git add manifest.json
    git commit -m "Release $Version"
    git tag -a $Version -m "Release $Version"
    git push origin HEAD --tags
    if ($CreateRelease) {
        if (Get-Command gh -ErrorAction SilentlyContinue) {
            gh release create $Version (Get-ChildItem artifacts -Filter *.zip).FullName --title "SyncVote $Version" --generate-notes
        }
        else {
            Write-Warning "GitHub CLI (gh) not found: upload artifacts/*.zip to the release by hand."
        }
    }
}
else {
    Write-Host "Not pushed. Review 'git diff manifest.json', then run again with -Push (and -CreateRelease)." -ForegroundColor Yellow
}
