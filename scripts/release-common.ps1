Set-StrictMode -Version Latest

function Get-ReleaseBinaryMetadata {
    param([Parameter(Mandatory)][string]$Directory,[Parameter(Mandatory)][string]$BaseName)
    $exe = Join-Path $Directory "$BaseName.exe"
    $dll = Join-Path $Directory "$BaseName.dll"
    if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath $dll)) { throw "Missing published binary for $BaseName." }
    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString()
    [ordered]@{
        name = $BaseName
        executable = [IO.Path]::GetFileName($exe)
        assembly = [IO.Path]::GetFileName($dll)
        assemblyVersion = $assemblyVersion
        fileVersion = $versionInfo.FileVersion
        informationalVersion = $versionInfo.ProductVersion
        executableSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
        assemblySha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Assert-ReleaseBinaryMetadata {
    param(
        [Parameter(Mandatory)]$Metadata,
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string]$ExpectedVersion,
        [Parameter(Mandatory)][string]$ExpectedCommit)
    $actual = Get-ReleaseBinaryMetadata -Directory $Directory -BaseName ([string]$Metadata.name)
    foreach ($field in @('assemblyVersion','fileVersion','informationalVersion','executableSha256','assemblySha256')) {
        if ([string]$actual[$field] -cne [string]$Metadata.$field) { throw "Binary metadata mismatch for $($Metadata.name): $field." }
    }
    $expectedInformational = "$ExpectedVersion+$ExpectedCommit"
    if ([string]$actual.informationalVersion -cne $expectedInformational) { throw "Wrong informational version for $($Metadata.name)." }
    if ([string]$actual.informationalVersion -notmatch [regex]::Escape($ExpectedCommit) + '$') { throw "Wrong informational commit for $($Metadata.name)." }
}

function Expand-ReleaseArchive {
    param([Parameter(Mandatory)][string]$Archive,[Parameter(Mandatory)][string]$Destination)
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force }
    Expand-Archive -LiteralPath $Archive -DestinationPath $Destination -Force
}
