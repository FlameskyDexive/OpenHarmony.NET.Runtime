[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $MetadataRoot,

    [string] $GeneratedReleaseRoot,

    [ValidateSet('Beta', 'Release')]
    [string] $Api26ReleaseType = 'Beta',

    [ValidateSet('preview', 'rc', 'stable')]
    [string] $Channel = 'preview'
)

$ErrorActionPreference = 'Stop'
$expectedApis = @(13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 26)
$expectedArchitectures = @('arm64', 'x64')
$expectedAbis = @('arm64-v8a', 'x86_64')

function Assert-EqualSequence {
    param(
        [Parameter(Mandatory = $true)][object[]] $Actual,
        [Parameter(Mandatory = $true)][object[]] $Expected,
        [Parameter(Mandatory = $true)][string] $Name
    )
    if (($Actual -join ',') -ne ($Expected -join ',')) {
        throw "$Name mismatch: expected '$($Expected -join ',')', found '$($Actual -join ',')'."
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

if ($Channel -eq 'stable' -and $Api26ReleaseType -ne 'Release') {
    throw 'Stable release is blocked while the HarmonyOS API26 SDK release type is Beta.'
}

$manifestPath = Join-Path $MetadataRoot 'manifest.json'
$spdxPath = Join-Path $MetadataRoot 'sbom.spdx.json'
$sumsPath = Join-Path $MetadataRoot 'SHA256SUMS'
foreach ($path in @($manifestPath, $spdxPath, $sumsPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Release metadata file was not found: $path" }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 2) { throw 'Runtime manifest schemaVersion must be 2.' }
if ($manifest.version -ne '10.0.10-ohos.2-preview.1') { throw "Unexpected runtime version: $($manifest.version)" }
if ($manifest.runtimeBaselineApi -ne 13) { throw "Runtime baseline API must be 13, found $($manifest.runtimeBaselineApi)." }
if ($manifest.runtimeCommit -notmatch '^[0-9a-f]{40}$' -or $manifest.bindingsCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'Runtime and bindings commits must be full lowercase Git SHA-1 values.'
}
Assert-EqualSequence -Actual @($manifest.supportedApis) -Expected $expectedApis -Name 'supportedApis'
Assert-EqualSequence -Actual @($manifest.architectures) -Expected $expectedArchitectures -Name 'architectures'
Assert-EqualSequence -Actual @($manifest.packages | ForEach-Object { $_.abi } | Sort-Object) -Expected @($expectedAbis | Sort-Object) -Name 'package ABIs'

foreach ($package in $manifest.packages) {
    if ($package.apiLevel -ne 13) { throw "Package $($package.abi) is not built at the API13 baseline." }
    if ($package.sdk.sha256 -notmatch '^[0-9a-f]{64}$') { throw "Invalid SDK hash for $($package.abi)." }
    $dependencies = @($package.dependencies)
    if (($dependencies | Select-Object -Unique).Count -ne $dependencies.Count) {
        throw "Duplicate dependency names are present for $($package.abi)."
    }
    $paths = @($package.files | ForEach-Object { $_.path })
    if (($paths | Select-Object -Unique).Count -ne $paths.Count) { throw "Duplicate file paths are present for $($package.abi)." }
    foreach ($file in $package.files) {
        if ($file.sha256 -notmatch '^[0-9a-f]{64}$') { throw "Invalid file hash for $($package.root)/$($file.path)." }
    }
}

if (@($manifest.compatibilityEntries).Count -ne 26) { throw 'Runtime manifest must contain 26 API/ABI compatibility entries.' }
foreach ($entry in $manifest.compatibilityEntries) {
    if ($entry.runtimeApi -ne 13 -or $entry.compatibilityKind -ne 'alias' -or $entry.sourceDirty) {
        throw "Invalid API13 compatibility entry for API$($entry.buildApi)/$($entry.abi)."
    }
    if ($entry.provenanceSha256 -notmatch '^[0-9a-f]{64}$') { throw "Invalid provenance hash for API$($entry.buildApi)/$($entry.abi)." }
}

$spdx = Get-Content -LiteralPath $spdxPath -Raw | ConvertFrom-Json
if ($spdx.spdxVersion -ne 'SPDX-2.3' -or $spdx.dataLicense -ne 'CC0-1.0') { throw 'SPDX document must use SPDX-2.3 and CC0-1.0.' }
if ($spdx.name -ne "OpenHarmony.NET.Runtime-$($manifest.version)") { throw 'SPDX document version does not match the runtime manifest.' }
if (@($spdx.packages).Count -ne @($manifest.packages).Count) { throw 'SPDX package count does not match the runtime manifest.' }

$checksumEntries = @{}
foreach ($line in Get-Content -LiteralPath $sumsPath) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw "Invalid SHA256SUMS line: $line" }
    $checksumEntries[$Matches[2]] = $Matches[1]
}
foreach ($name in @('manifest.json', 'sbom.spdx.json')) {
    $path = Join-Path $MetadataRoot $name
    if ($checksumEntries[$name] -ne (Get-Sha256 -Path $path)) { throw "SHA256SUMS mismatch for $name." }
}

if (-not [string]::IsNullOrWhiteSpace($GeneratedReleaseRoot)) {
    $generatedManifestPath = Join-Path $GeneratedReleaseRoot 'manifest.json'
    if ((Get-Sha256 -Path $generatedManifestPath) -ne (Get-Sha256 -Path $manifestPath)) {
        throw 'Tracked release manifest differs from the generated runtime package manifest.'
    }
    $generatedSbomPath = Join-Path $GeneratedReleaseRoot $manifest.sbom.path
    if ((Get-Sha256 -Path $generatedSbomPath) -ne $manifest.sbom.sha256) { throw 'Generated CycloneDX SBOM hash does not match the manifest.' }
    foreach ($package in $manifest.packages) {
        $packageRoot = Join-Path $GeneratedReleaseRoot ($package.root -replace '/', [IO.Path]::DirectorySeparatorChar)
        foreach ($file in $package.files) {
            $path = Join-Path $packageRoot ($file.path -replace '/', [IO.Path]::DirectorySeparatorChar)
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Package file is missing: $path" }
            $item = Get-Item -LiteralPath $path
            if ($item.Length -ne $file.size -or (Get-Sha256 -Path $path) -ne $file.sha256) {
                throw "Package file verification failed: $path"
            }
        }
    }
    foreach ($nupkg in Get-ChildItem -LiteralPath $GeneratedReleaseRoot -Filter '*.nupkg' -File) {
        if ($checksumEntries[$nupkg.Name] -ne (Get-Sha256 -Path $nupkg.FullName)) {
            throw "SHA256SUMS mismatch for $($nupkg.Name)."
        }
    }
}

Write-Output "Verified $($manifest.version) $Channel release metadata for API13-24/26 and both ABIs."
