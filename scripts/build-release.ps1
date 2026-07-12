[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$OutputDirectory,
    [string]$CommitHash
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot '..\artifacts' }

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw "Version '$Version' is not a valid SemVer version." }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    $dirty = git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect Git working tree.' }
    if ($dirty) { throw 'Release packaging requires a clean Git working tree.' }
    if (-not $CommitHash) { $CommitHash = (git rev-parse HEAD).Trim() }
    if ($LASTEXITCODE -ne 0 -or $CommitHash -notmatch '^[0-9a-fA-F]{7,40}$') { throw 'A valid Git commit hash is required.' }

    $out = [IO.Path]::GetFullPath($OutputDirectory)
    $stage = Join-Path $out '._release-staging'
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    @('manifest.json','SHA256SUMS.txt',"GameControlMapper-$Version-win-x64.zip","GameControlMapper-TouchTestHarness-$Version-win-x64.zip") | ForEach-Object { $p=Join-Path $out $_; if(Test-Path $p){Remove-Item -Force $p} }
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
    [xml]$trx = Get-Content (Join-Path $results 'release.trx')
    $counters = $trx.TestRun.ResultSummary.Counters
    $passed = [int]$counters.passed
    if ([int]$counters.failed -ne 0) { throw 'TRX reports failed tests; packaging aborted.' }

    dotnet publish src/GameControlMapper/GameControlMapper.csproj -c Release -r win-x64 --self-contained true --no-restore -p:Version=$Version -p:SourceRevisionId=$CommitHash -p:PublishSingleFile=false -o $main
    if ($LASTEXITCODE) { throw 'Main application publish failed.' }
    dotnet publish src/GameControlMapper.TouchTestHarness/GameControlMapper.TouchTestHarness.csproj -c Release -r win-x64 --self-contained true --no-restore -p:Version=$Version -p:SourceRevisionId=$CommitHash -p:PublishSingleFile=false -o $harness
    if ($LASTEXITCODE) { throw 'TouchTestHarness publish failed.' }

    Copy-Item docs/RELEASE_CANDIDATE.md (Join-Path $main 'README.md')
    Copy-Item docs/MANUAL_RELEASE_VALIDATION.md (Join-Path $main 'MANUAL_RELEASE_VALIDATION.md')
    Copy-Item docs/RELEASE_CANDIDATE.md (Join-Path $harness 'README.md')
    Get-ChildItem $stage -Recurse -Include '*.trx','*.tmp','*.log','*.bak' | Remove-Item -Force
    $mainZip = Join-Path $out "GameControlMapper-$Version-win-x64.zip"
    $harnessZip = Join-Path $out "GameControlMapper-TouchTestHarness-$Version-win-x64.zip"
    Compress-Archive -Path (Join-Path $main '*') -DestinationPath $mainZip -CompressionLevel Optimal
    Compress-Archive -Path (Join-Path $harness '*') -DestinationPath $harnessZip -CompressionLevel Optimal
    $mainHash = (Get-FileHash $mainZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $harnessHash = (Get-FileHash $harnessZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = [ordered]@{ product='Game Control Mapper'; version=$Version; commitHash=$CommitHash; buildDateUtc=(Get-Date).ToUniversalTime().ToString('o'); targetFramework='net8.0-windows'; rid='win-x64'; selfContained=$true; testCount=$passed; manualValidation='NotPerformed'; archives=@(@{file=[IO.Path]::GetFileName($mainZip);sha256=$mainHash},@{file=[IO.Path]::GetFileName($harnessZip);sha256=$harnessHash}) }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $out 'manifest.json') -Encoding utf8
    @("$mainHash  $([IO.Path]::GetFileName($mainZip))","$harnessHash  $([IO.Path]::GetFileName($harnessZip))") | Set-Content (Join-Path $out 'SHA256SUMS.txt') -Encoding ascii
    & (Join-Path $PSScriptRoot 'verify-release.ps1') -ArtifactsDirectory $out -Version $Version
    if ($LASTEXITCODE) { throw 'Artifact verification failed.' }
    Remove-Item -Recurse -Force $stage
    Write-Host "Release candidate created in $out"
} finally { Pop-Location }
