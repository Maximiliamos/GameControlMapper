[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReportPath,
    [Parameter(Mandatory)][string]$ApplicationArchive,
    [Parameter(Mandatory)][string]$HarnessArchive,
    [Parameter(Mandatory)][string]$ExpectedVersion,
    [Parameter(Mandatory)][string]$ExpectedCommit)

$ErrorActionPreference = 'Stop'
try { $report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json }
catch { throw "Invalid validation JSON: $($_.Exception.Message)" }

if ($report.schemaVersion -ne '1.0') { throw 'Unsupported schema version.' }
if ($report.productVersion -ne $ExpectedVersion) { throw 'Wrong product version.' }
if ($report.commitHash -ne $ExpectedCommit -or $report.applicationCommitHash -ne $ExpectedCommit -or $report.harnessCommitHash -ne $ExpectedCommit) { throw 'Wrong commit hash.' }
if ((Get-FileHash -LiteralPath $ApplicationArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $report.applicationArchiveSha256) { throw 'Application archive hash mismatch.' }
if ((Get-FileHash -LiteralPath $HarnessArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $report.harnessArchiveSha256) { throw 'Harness archive hash mismatch.' }
if ($report.protocolErrors.Count -gt 0) { throw 'Protocol errors are present.' }
if ([int]$report.activeContactsAtEnd -ne 0) { throw 'Active contacts remain.' }

$knownNameHashes = [ordered]@{
    1='464d23b54f26916cba7bf34f327f10714e7b64bdcc067821dd96b5d77a1b9ac8';2='8e685d5409b3a543b1cadcc35b095d64847867539c12b21a36d55991ca0cdce1';3='4dc6114f9856f8519e52f81ce271d1cd481bb91d326f58a399459b068ab7c58e';4='3f275d858425700456cc0acbc2c3ee28a7ac38148288438596f9a3d0115048a0';
    5='ee5075ff3d6a1422f97461af6e771dc4c6fb50508644388c8a34173e4ebe4055';6='9757db982881957b6123eee42d7bc5d1cd114e1eedc70410bbea968cfd5d42a7';7='05bc40eb3f2eefc18b22ab0e86733af1b26c51614ea8f9bf1a0f5750cdb6cb89';8='b5ed580647d89739c451889230e9c2d601b5a804e3bac39b31b646e036dcbf22';
    9='493fbe6a70080c390852789568b8762f4a1a54191457f6eab1d50fd3b4c201cb';10='f303d6bcd01d9389139dc43d13e7becd48a8c84696c7dee079dc7e57df36619f';11='de2fcd9f5a017ab25807bfb3de1ce67defb5aaeac24b86615142cffe4281bc62';12='112b94f4afaf592351430577a56e431142e170df927be8218692d68db216547a';
    13='e6ad1b872cdda35a13ad72c8732df8db2015dc10d348f10785a8b3e0fbdfba83';14='44cc934ac7ed5d2a8041c2ca4c4435e69df4d55c652d68b210ce7b1a5352487c';15='1392c9bff2fe2ed1bcb56319bd24a49c9adf5469cc022efc79a56a495931e621';16='bf6d49cb95a70e258b6557285b8a8a2eaac25f4d0bb95164069556aa0479c936';
    17='07f091d15451d8778fc33ae3130a9f50fc00e3e6e8d67a05c3f46450d635ff92';18='bcec62444a5eb82ffb86b7bab52bb9a26fae593a1c46d9298046f3ff11f4903e';19='8d0f2b41ee708b570c4394f026bec101fdd6b914e2cff0eec748f12ddc22222b';20='402d28028cd88a706a18396a6960c5b614c8a16a1753bf4099b3ebad5e922215';
    21='3ddd600691d1487ca397a910f07ef59d793fb45614664025d0d513b0bc38dd88';22='5cb79628dfed1f7d886a5cc5e56db8d7462fce9d572d223297044855be3f8a3d';23='2aebcf4e653c00f7e75528f481da5310bd2329efc9c8082a4d249de0471c1450';24='531061424c6d23e679e8679010c6bf5113bd43bb4a24d0bed58b7e98c2fc7045';
    25='429f7a3aedb3e98579460c256748c2aaedd15126877f9638f9c42b3654d6965b';26='4d8d200c21fc21375c3eee400ca76fd291f8a0255da026fdab5f9e4d342ace01';27='306b1f23b999c26b944d6c46d99fd0876d6076d968ca05c4b5fc6ffa21ca5eda';28='e15d858d028573d716ba5dad597f6639dc739ebd76a9ef8e284c6cf6b9079e01';
    29='2a3b30f7a40e95af1e0865b213cb089e0defca4dcffdab87313282cc91bb6e96';30='5d7d411914caac76ff2127db63482ca514f5283c607a846f3bfe52c6229a509b';31='6b4b78456bea5a208bb82aa37e30c3bed158e2f97dc5e75112cf0193550320c3';32='bfd15b9785e87f26ad0455a82d628a0532eb9be238658a015d9db95db8fce03b';
    33='d2ebd40269f450af96cc5cd491d98207e0f8736745e091d3c297427139512ce7';34='2e1904fbae37bdad408945ff15a45e7b6b00926ea614455817957919a098e19b';35='d98f4d390d4dff55fe0923c071bd0dd177543c938a0ea93e5c7c9fef010cd29a';36='106b7d80aafffeb7faae672b29534315f343c75050589a053da5e506a34c3046';
    37='292451c83a23cef4c392f1e530a9b0a451f2cff911133b6a731187ef0508dfdf';38='b03d9a5da4aa6c1808583d4597437d66b5793dbe556b17dd829fdcb5f2694298';39='3fec2c2816c8943d25235aac8f351d7e538f77c27d7e1c3bd47360abd0050812';40='bb3be069928cd768b3f07a7a4d38dbe75706a4d66a6fa7f59b8b8b8e5114d8c2';
    41='ccd94813a44e7b54d8421dead837ecdf0ca205d3915d0eaaa541191b4fd20a71';42='801a3c94a04d891737dc890ffc1cdddf1d1a1431314cc35433498dab7273ba6d';43='1b802231bf6aa9065e0f095878a15600ca5a5836364ba12752eb02696499f50b';44='1bb1fd0c6401d1ebcfd238bb7ab4c528beda142c55539fc4b8e6fc79e1f1af4a';
    45='50500ce6d92ba602f838c9750abbb3b5ac4201842600e44887af16f14c148451';46='1506a42eaf9cd0209b1bd4d6c9834a67ff8683c3486080e34b9014de2dfac716';47='3472fd512ab84a6a3c7699e0f68739cd5e68cb18433213c6b2d30184c8c94827'
}

$scenarios = @($report.scenarios)
if ($scenarios.Count -ne $knownNameHashes.Count) { throw 'Missing required validation scenario.' }
$duplicateIds = @($scenarios | Group-Object { [int]$_.id } | Where-Object Count -gt 1)
if ($duplicateIds.Count -gt 0) { throw 'Duplicate validation scenario ID.' }
foreach ($scenario in $scenarios) {
    $id = [int]$scenario.id
    $knownEntry = @($knownNameHashes.GetEnumerator() | Where-Object { [int]$_.Key -eq $id })
    if ($knownEntry.Count -ne 1) { throw 'Unknown validation scenario ID.' }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $actualNameHash = -join ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes([string]$scenario.name)) | ForEach-Object { $_.ToString('x2') }) }
    finally { $sha.Dispose() }
    if ($actualNameHash -cne [string]$knownEntry[0].Value) { throw "Modified validation scenario name for ID $id." }
}
foreach ($id in $knownNameHashes.Keys) {
    if (-not ($scenarios | Where-Object { [int]$_.id -eq [int]$id })) { throw 'Missing required validation scenario.' }
}

$incomplete = @($scenarios | Where-Object { $_.status -in @('NotStarted','InProgress') })
if ($incomplete.Count -gt 0) { throw 'Report contains incomplete scenarios.' }
if ($scenarios | Where-Object { $_.status -eq 'Failed' }) { throw 'Report contains failed scenarios.' }
$machineEvidenceIds = @(1,2,3,4,9,15,16)
foreach ($id in $machineEvidenceIds) {
    $scenario = @($scenarios | Where-Object { [int]$_.id -eq $id })[0]
    if ($scenario.status -eq 'Passed') {
        if ($null -eq $scenario.evidence -or $scenario.evidence.automaticVerdict -ne 'Passed' -or $scenario.userVerdict -ne 'Passed' -or $scenario.finalVerdict -ne 'Passed') { throw "Machine-verifiable scenario $id lacks accepted evidence." }
        if ([int]$scenario.evidence.eventCount -le 0 -or [datetimeoffset]$scenario.evidence.completedAt -lt [datetimeoffset]$scenario.evidence.startedAt) { throw "Machine-verifiable scenario $id has malformed evidence." }
    }
}

$monitors = @($report.monitors)
if ($monitors.Count -eq 0 -or [int]$report.monitorCount -ne $monitors.Count) { throw 'Monitor metadata is missing or inconsistent.' }
$hasHighDpi = @($monitors | Where-Object { [int]$_.scalePercent -ge 125 }).Count -gt 0
$hasMultiple = $monitors.Count -ge 2
$hasNegative = @($monitors | Where-Object { [int]$_.x -lt 0 -or [int]$_.y -lt 0 }).Count -gt 0
$dpiPairs = @($monitors | ForEach-Object { "{0:F0}x{1:F0}" -f [double]$_.dpiX,[double]$_.dpiY } | Sort-Object -Unique)
$hasMixedDpi = $dpiPairs.Count -gt 1
$availability = @{ 44 = -not $hasHighDpi; 45 = -not $hasMultiple; 46 = -not $hasNegative; 47 = -not $hasMixedDpi }
foreach ($scenario in @($scenarios | Where-Object { $_.status -eq 'NotAvailable' })) {
    $id = [int]$scenario.id
    if (-not $availability.ContainsKey($id) -or -not $availability[$id]) { throw 'Scenario cannot be NotAvailable in the recorded environment.' }
}

$hasUnavailable = @($scenarios | Where-Object { $_.status -eq 'NotAvailable' }).Count -gt 0
$expectedVerdict = if ($hasUnavailable) { 'PassedWithUnverifiedEnvironments' } else { 'Passed' }
if ($report.verdict -ne $expectedVerdict) { throw 'Validation verdict is inconsistent.' }
Write-Host "Manual validation accepted: $expectedVerdict"
