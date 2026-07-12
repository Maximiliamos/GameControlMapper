[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReportPath,[Parameter(Mandatory)][string]$ApplicationArchive,[Parameter(Mandatory)][string]$HarnessArchive,[Parameter(Mandatory)][string]$ExpectedVersion,[Parameter(Mandatory)][string]$ExpectedCommit)
$ErrorActionPreference='Stop'
try{$r=Get-Content -LiteralPath $ReportPath -Raw|ConvertFrom-Json}catch{throw "Invalid validation JSON: $($_.Exception.Message)"}
if($r.schemaVersion-ne'1.0'){throw 'Unsupported schema version.'}
if($r.productVersion-ne$ExpectedVersion){throw 'Wrong product version.'}
if($r.commitHash-ne$ExpectedCommit-or$r.applicationCommitHash-ne$ExpectedCommit-or$r.harnessCommitHash-ne$ExpectedCommit){throw 'Wrong commit hash.'}
if((Get-FileHash -LiteralPath $ApplicationArchive -Algorithm SHA256).Hash.ToLowerInvariant()-ne$r.applicationArchiveSha256){throw 'Application archive hash mismatch.'}
if((Get-FileHash -LiteralPath $HarnessArchive -Algorithm SHA256).Hash.ToLowerInvariant()-ne$r.harnessArchiveSha256){throw 'Harness archive hash mismatch.'}
if($r.protocolErrors.Count-gt0){throw 'Protocol errors are present.'};if([int]$r.activeContactsAtEnd-ne0){throw 'Active contacts remain.'}
$incomplete=@($r.scenarios|Where-Object{$_.status-in @('NotStarted','InProgress')});if($incomplete){throw 'Report contains incomplete scenarios.'}
if($r.scenarios|Where-Object{$_.status-eq'Failed'}){throw 'Report contains failed scenarios.'}
$badUnavailable=@($r.scenarios|Where-Object{$_.status-eq'NotAvailable'-and-not $_.environmentOnly});if($badUnavailable){throw 'Core scenario cannot be NotAvailable.'}
$hasUnavailable=@($r.scenarios|Where-Object{$_.status-eq'NotAvailable'}).Count-gt0;$expected=if($hasUnavailable){'PassedWithUnverifiedEnvironments'}else{'Passed'};if($r.verdict-ne$expected){throw 'Validation verdict is inconsistent.'}
Write-Host "Manual validation accepted: $expected"
