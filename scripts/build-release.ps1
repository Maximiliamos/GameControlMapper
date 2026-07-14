[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$OutputDirectory,
    [string]$CommitHash)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'release-common.ps1')
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot '..\artifacts' }
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw "Version '$Version' is not valid SemVer." }

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    $dirty = git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect Git working tree.' }
    if ($dirty) { throw 'Release packaging requires a clean Git working tree.' }
    $head = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve Git HEAD.' }
    if (-not $CommitHash) { $CommitHash = $head }
    if ($CommitHash -notmatch '^[0-9a-fA-F]{40}$' -or $CommitHash -cne $head) { throw 'Release commit must equal the full current Git HEAD.' }

    $out = [IO.Path]::GetFullPath($OutputDirectory)
    $stage = Join-Path $out '._release-staging'
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    @('manifest.json','SHA256SUMS.txt',"GameControlMapper-$Version-win-x64.zip","GameControlMapper-TouchTestHarness-$Version-win-x64.zip") | ForEach-Object {
        $old = Join-Path $out $_
        if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force }
    }
    $main = Join-Path $stage 'GameControlMapper'
    $harness = Join-Path $stage 'TouchTestHarness'
    $results = Join-Path $stage 'TestResults'
    New-Item -ItemType Directory -Force -Path $main,$harness,$results | Out-Null

    dotnet clean GameControlMapper.sln -c Release
    if ($LASTEXITCODE) { throw 'Clean failed.' }
    dotnet restore GameControlMapper.sln -r win-x64
    if ($LASTEXITCODE) { throw 'Restore failed.' }
    dotnet build GameControlMapper.sln -c Release --no-restore -p:Version=$Version -p:SourceRevisionId=$CommitHash
    if ($LASTEXITCODE) { throw 'Release build failed.' }
    dotnet test GameControlMapper.sln -c Release --no-build --logger "trx;LogFileName=release.trx" --results-directory $results
    if ($LASTEXITCODE) { throw 'Tests failed; packaging aborted.' }
    [xml]$trx = Get-Content -LiteralPath (Join-Path $results 'release.trx')
    $counters = $trx.TestRun.ResultSummary.Counters
    $totalTests = [int]$counters.total
    if ($totalTests -le 0 -or [int]$counters.failed -ne 0 -or [int]$counters.notExecuted -ne 0) { throw 'TRX test counters are not acceptable.' }

    $publishProperties = @('-p:PublishSingleFile=false',"-p:Version=$Version","-p:SourceRevisionId=$CommitHash")
    dotnet publish src/GameControlMapper/GameControlMapper.csproj -c Release -r win-x64 --self-contained true --no-restore @publishProperties -o $main
    if ($LASTEXITCODE) { throw 'Main application publish failed.' }
    dotnet publish src/GameControlMapper.TouchTestHarness/GameControlMapper.TouchTestHarness.csproj -c Release -r win-x64 --self-contained true --no-restore @publishProperties -o $harness
    if ($LASTEXITCODE) { throw 'TouchTestHarness publish failed.' }

    Copy-Item docs/RELEASE_CANDIDATE.md (Join-Path $main 'README.md')
    Copy-Item docs/MANUAL_RELEASE_VALIDATION.md (Join-Path $main 'MANUAL_RELEASE_VALIDATION.md')
    Copy-Item docs/RELEASE_CANDIDATE.md (Join-Path $harness 'README.md')
    Get-ChildItem $stage -Recurse -Include '*.trx','*.tmp','*.log','*.bak' | Remove-Item -Force

    $binaries = @(
        Get-ReleaseBinaryMetadata -Directory $main -BaseName 'GameControlMapper'
        Get-ReleaseBinaryMetadata -Directory $harness -BaseName 'GameControlMapper.TouchTestHarness')
    foreach ($binary in $binaries) {
        if ($binary.informationalVersion -cne "$Version+$CommitHash") { throw "Published informational version is wrong for $($binary.name)." }
    }

    $mainZip = Join-Path $out "GameControlMapper-$Version-win-x64.zip"
    $harnessZip = Join-Path $out "GameControlMapper-TouchTestHarness-$Version-win-x64.zip"
    Compress-Archive -Path (Join-Path $main '*') -DestinationPath $mainZip -CompressionLevel Optimal
    Compress-Archive -Path (Join-Path $harness '*') -DestinationPath $harnessZip -CompressionLevel Optimal
    $archives = @(
        [ordered]@{ file=[IO.Path]::GetFileName($mainZip); sha256=Get-Sha256 -Path $mainZip; binary='GameControlMapper' },
        [ordered]@{ file=[IO.Path]::GetFileName($harnessZip); sha256=Get-Sha256 -Path $harnessZip; binary='GameControlMapper.TouchTestHarness' })
    $manifest = [ordered]@{
        schemaVersion='2.0'; product='Game Control Mapper'; version=$Version; commitHash=$CommitHash
        buildDateUtc=(Get-Date).ToUniversalTime().ToString('o'); ciRunIdentifier=$env:GITHUB_RUN_ID
        targetFramework='net8.0-windows'; rid='win-x64'; selfContained=$true
        testCount=$totalTests; testCounters=[ordered]@{ total=$totalTests; passed=[int]$counters.passed; failed=[int]$counters.failed; skipped=[int]$counters.notExecuted }
        manualValidation='NotPerformed'; binaries=$binaries; archives=$archives
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $out 'manifest.json') -Encoding utf8
    $archives | ForEach-Object { "$($_.sha256)  $($_.file)" } | Set-Content (Join-Path $out 'SHA256SUMS.txt') -Encoding ascii
    & (Join-Path $PSScriptRoot 'verify-release.ps1') -ArtifactsDirectory $out -Version $Version -ExpectedCommit $CommitHash
    Remove-Item -LiteralPath $stage -Recurse -Force
    Write-Host "Release candidate created in $out"
} finally { Pop-Location }
