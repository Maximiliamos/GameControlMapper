[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReportPath,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Commit,
    [Parameter(Mandatory)][string]$ApplicationArchive,
    [Parameter(Mandatory)][string]$HarnessArchive,
    [Parameter(Mandatory)][string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Version -ne '1.0.0') { throw 'Final release version must be 1.0.0.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    $head = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -cne $Commit) { throw 'Current source commit differs from the manually validated commit.' }
    if (git status --porcelain) { throw 'Finalization requires a clean tree; production code may not change after validation.' }

    $applicationDirectory = Split-Path -Parent ([IO.Path]::GetFullPath($ApplicationArchive))
    $harnessDirectory = Split-Path -Parent ([IO.Path]::GetFullPath($HarnessArchive))
    if ($applicationDirectory -cne $harnessDirectory) { throw 'Validated candidate archives must share one artifact directory.' }
    $candidateManifestPath = Join-Path $applicationDirectory 'manifest.json'
    if (-not (Test-Path -LiteralPath $candidateManifestPath)) { throw 'Validated candidate manifest is missing.' }
    try { $candidateManifest = Get-Content -LiteralPath $candidateManifestPath -Raw | ConvertFrom-Json }
    catch { throw "Malformed candidate manifest: $($_.Exception.Message)" }
    if ($candidateManifest.commitHash -cne $Commit) { throw 'Validated candidate commit mismatch.' }
    $candidateVersion = [string]$candidateManifest.version
    if ($candidateVersion -eq $Version) { throw 'Finalizer requires a separately versioned validated beta candidate.' }

    & (Join-Path $PSScriptRoot 'validate-manual-release.ps1') `
        -ReportPath $ReportPath -ApplicationArchive $ApplicationArchive -HarnessArchive $HarnessArchive `
        -ExpectedVersion $candidateVersion -ExpectedCommit $Commit -CandidateManifest $candidateManifestPath
    if ($LASTEXITCODE) { throw 'Manual validation failed.' }
    $validatedReport = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json

    $output = [IO.Path]::GetFullPath($OutputDirectory)
    & (Join-Path $PSScriptRoot 'build-release.ps1') -Version $Version -OutputDirectory $output -CommitHash $Commit
    if ($LASTEXITCODE) { throw 'Final binaries could not be rebuilt from the validated commit.' }

    $finalManifestPath = Join-Path $output 'manifest.json'
    $finalManifest = Get-Content -LiteralPath $finalManifestPath -Raw | ConvertFrom-Json
    $candidateHashes = @($candidateManifest.archives | ForEach-Object { [ordered]@{ file=$_.file; sha256=$_.sha256 } })
    $finalHashes = @($finalManifest.archives | ForEach-Object { [ordered]@{ file=$_.file; sha256=$_.sha256 } })
    foreach ($finalHash in $finalHashes) {
        if ($candidateHashes.sha256 -contains $finalHash.sha256) { throw 'A final archive is byte-identical to a renamed validated candidate.' }
    }

    $finalManifest | Add-Member -NotePropertyName manualValidation -NotePropertyValue ([string]$validatedReport.verdict) -Force
    $finalManifest | Add-Member -NotePropertyName validatedCandidateVersion -NotePropertyValue $candidateVersion -Force
    $finalManifest | Add-Member -NotePropertyName validatedRcHashes -NotePropertyValue $candidateHashes -Force
    $finalManifest | Add-Member -NotePropertyName finalArchiveHashes -NotePropertyValue $finalHashes -Force
    $finalManifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $finalManifestPath -Encoding utf8
    Copy-Item -LiteralPath $ReportPath -Destination (Join-Path $output 'manual-validation-report.json') -Force
    $reportText = [IO.Path]::ChangeExtension($ReportPath,'.txt')
    if (Test-Path -LiteralPath $reportText) { Copy-Item -LiteralPath $reportText -Destination (Join-Path $output 'manual-validation-report.txt') -Force }
    Copy-Item -LiteralPath (Join-Path $root 'CHANGELOG.md') -Destination (Join-Path $output 'RELEASE_NOTES.md') -Force
    $environmentRows = @(
        @{ Id=44; Name='High DPI (125% or more)' },
        @{ Id=45; Name='Multiple monitors' },
        @{ Id=46; Name='Negative screen origin' },
        @{ Id=47; Name='Mixed DPI' })
    $supportLines = @(
        '# Validated support matrix', '',
        "Generated from manual report for version $Version and commit $Commit.", '',
        '| Area | Status | Evidence |', '|---|---|---|',
        '| Core Windows Touch scenarios | Supported | All required core scenarios passed with zero active contacts |')
    foreach ($row in $environmentRows) {
        $scenario = @($validatedReport.scenarios | Where-Object { [int]$_.id -eq $row.Id })[0]
        $status = if ($scenario.status -eq 'Passed') { 'Supported' } else { 'AutomatedOnly' }
        $supportLines += "| $($row.Name) | $status | guided scenario $($row.Id): $($scenario.status) |"
    }
    $supportLines += @(
        '| Specific games and emulators | Experimental | not established by the generic harness |',
        '| XInput, Macro/Sequence, ADB, Interception, ViGEm, pinch, rotation | Unsupported | runtime absent |')
    $supportLines | Set-Content -LiteralPath (Join-Path $output 'SUPPORT_MATRIX.md') -Encoding utf8
    & (Join-Path $PSScriptRoot 'verify-release.ps1') -ArtifactsDirectory $output -Version $Version -ExpectedCommit $Commit
    if ($LASTEXITCODE) { throw 'Final artifact verification failed.' }
    Write-Host "Final release rebuilt and created in $output"
} finally { Pop-Location }
