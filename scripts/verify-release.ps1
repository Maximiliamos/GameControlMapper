[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactsDirectory,
    [Parameter(Mandatory)][string]$Version,
    [string]$ExpectedCommit)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'release-common.ps1')
$directory = [IO.Path]::GetFullPath($ArtifactsDirectory)
$manifestPath = Join-Path $directory 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Missing manifest.' }
try { $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json }
catch { throw "Malformed manifest: $($_.Exception.Message)" }
if ($manifest.schemaVersion -ne '2.0' -or $manifest.version -ne $Version -or [string]::IsNullOrWhiteSpace($manifest.commitHash)) { throw 'Manifest schema, version or commit is invalid.' }
if ($ExpectedCommit -and $manifest.commitHash -cne $ExpectedCommit) { throw 'Manifest commit does not match expected commit.' }
if ($manifest.rid -ne 'win-x64') { throw 'Wrong RID.' }
if ($manifest.targetFramework -ne 'net8.0-windows') { throw 'Wrong target framework.' }
if ($manifest.selfContained -ne $true) { throw 'Framework-dependent archive is not allowed.' }
if ([int]$manifest.testCount -le 0 -or [int]$manifest.testCounters.total -ne [int]$manifest.testCount -or [int]$manifest.testCounters.failed -ne 0 -or [int]$manifest.testCounters.skipped -ne 0) { throw 'Invalid test counters.' }

foreach ($archive in $manifest.archives) {
    $path = Join-Path $directory $archive.file
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing archive $($archive.file)." }
    if ((Get-Sha256 -Path $path) -cne $archive.sha256) { throw "SHA-256 mismatch: $($archive.file)." }
    if ($archive.file -notmatch [regex]::Escape($Version)) { throw 'Archive name does not contain manifest version.' }
}

$temporary = Join-Path ([IO.Path]::GetTempPath()) ("gcm-verify-" + [guid]::NewGuid().ToString('N'))
try {
    foreach ($binary in $manifest.binaries) {
        $archive = @($manifest.archives | Where-Object { $_.binary -eq $binary.name })
        if ($archive.Count -ne 1) { throw "Archive mapping is invalid for $($binary.name)." }
        $expanded = Join-Path $temporary $binary.name
        Expand-ReleaseArchive -Archive (Join-Path $directory $archive[0].file) -Destination $expanded
        Assert-ReleaseBinaryMetadata -Metadata $binary -Directory $expanded -ExpectedVersion $Version -ExpectedCommit $manifest.commitHash
        $names = @(Get-ChildItem -LiteralPath $expanded -Recurse -File | ForEach-Object { $_.FullName.Substring($expanded.Length + 1) })
        if ($names | Where-Object { $_ -match '\.log$|\.bak$|(^|\\)Profiles(\\|$)' }) { throw 'Forbidden user data in archive.' }
        foreach ($textFile in Get-ChildItem -LiteralPath $expanded -Recurse -File -Include '*.md','*.txt','*.xml','*.config','*.json') {
            if ((Get-Content -LiteralPath $textFile.FullName -Raw -ErrorAction SilentlyContinue) -match '[A-Za-z]:\\Users\\') { throw 'Personal path found in archive.' }
        }
    }
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force } }
Write-Host 'Artifact verification passed.'
