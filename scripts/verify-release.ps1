[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ArtifactsDirectory,[Parameter(Mandatory=$true)][string]$Version)
$ErrorActionPreference='Stop'
$dir=[IO.Path]::GetFullPath($ArtifactsDirectory);$manifest=Get-Content (Join-Path $dir 'manifest.json') -Raw|ConvertFrom-Json
if($manifest.version-ne$Version-or [string]::IsNullOrWhiteSpace($manifest.commitHash)){throw 'Manifest version or commit hash is invalid.'}
foreach($a in $manifest.archives){$p=Join-Path $dir $a.file;if(-not(Test-Path $p)){throw "Missing archive $($a.file)"};if((Get-FileHash $p -Algorithm SHA256).Hash.ToLowerInvariant()-ne$a.sha256){throw "SHA-256 mismatch: $($a.file)"}}
$main=Join-Path $dir "GameControlMapper-$Version-win-x64.zip";$harness=Join-Path $dir "GameControlMapper-TouchTestHarness-$Version-win-x64.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$m=[IO.Compression.ZipFile]::OpenRead($main);try{$names=$m.Entries.FullName;if(-not($names-match'GameControlMapper.exe$')){throw 'Main EXE missing.'};$forbiddenJson=$names|Where-Object{$_-match'\.json$'-and $_-notmatch'\.(deps|runtimeconfig)\.json$'};if($forbiddenJson-or $names-match'\.log$|\.bak$|Profiles/'){throw 'Forbidden user data in main archive.'};foreach($e in $m.Entries|Where-Object{$_.FullName-match'\.(md|txt|xml|config|json)$'}){if($e.Length-gt 0){$r=[IO.StreamReader]::new($e.Open());try{if($r.ReadToEnd()-match'[A-Za-z]:\\Users\\'){throw 'Personal path found in main archive.'}}finally{$r.Dispose()}}}}finally{$m.Dispose()}
$h=[IO.Compression.ZipFile]::OpenRead($harness);try{if(-not($h.Entries.FullName-match'GameControlMapper.TouchTestHarness.exe$')){throw 'Harness EXE missing.'};if($h.Entries.FullName-match'^GameControlMapper.exe$'){throw 'Main executable found in harness archive.'}}finally{$h.Dispose()}
Write-Host 'Artifact verification passed.'
