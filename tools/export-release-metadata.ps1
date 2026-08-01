[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $GeneratedReleaseRoot,

    [Parameter(Mandatory = $true)]
    [string] $MetadataRoot,

    [ValidateSet('Beta', 'Release')]
    [string] $Api26ReleaseType = 'Beta',

    [ValidateSet('preview', 'rc', 'stable')]
    [string] $Channel = 'preview',

    [string] $CreatedUtc = '2026-08-01T00:00:00Z'
)

$ErrorActionPreference = 'Stop'

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-TextSha256 {
    param([Parameter(Mandatory = $true)][string] $Text)
    $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
    $hash = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($hash.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $hash.Dispose()
    }
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Content
    )
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

if ($Channel -eq 'stable' -and $Api26ReleaseType -ne 'Release') {
    throw 'Stable release is blocked while the HarmonyOS API26 SDK release type is Beta.'
}

$sourceManifestPath = Join-Path $GeneratedReleaseRoot 'manifest.json'
if (-not (Test-Path -LiteralPath $sourceManifestPath -PathType Leaf)) {
    throw "Generated runtime manifest was not found: $sourceManifestPath"
}

$manifest = Get-Content -LiteralPath $sourceManifestPath -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $MetadataRoot -Force | Out-Null
$manifestPath = Join-Path $MetadataRoot 'manifest.json'
Copy-Item -LiteralPath $sourceManifestPath -Destination $manifestPath -Force

$spdxPackages = foreach ($package in $manifest.packages) {
    $contentIndex = ($package.files | Sort-Object path | ForEach-Object { "$($_.sha256)  $($_.path)" }) -join "`n"
    $packageDigest = Get-TextSha256 -Text ($contentIndex + "`n")
    [ordered]@{
        name = "OpenHarmony.NET.Runtime.$($package.rid)"
        SPDXID = "SPDXRef-Package-$($package.abi.Replace('_', '-'))"
        versionInfo = [string]$manifest.version
        downloadLocation = 'NOASSERTION'
        filesAnalyzed = $false
        checksums = @([ordered]@{ algorithm = 'SHA256'; checksumValue = $packageDigest })
        licenseConcluded = 'NOASSERTION'
        licenseDeclared = 'NOASSERTION'
        copyrightText = 'NOASSERTION'
        comment = "API$($package.apiLevel); ABI=$($package.abi); RID=$($package.rid); runtimeCommit=$($manifest.runtimeCommit)"
        externalRefs = @([ordered]@{
            referenceCategory = 'PACKAGE-MANAGER'
            referenceType = 'purl'
            referenceLocator = "pkg:generic/OpenHarmony.NET.Runtime.$($package.rid)@$($manifest.version)"
        })
    }
}

$spdx = [ordered]@{
    spdxVersion = 'SPDX-2.3'
    dataLicense = 'CC0-1.0'
    SPDXID = 'SPDXRef-DOCUMENT'
    name = "OpenHarmony.NET.Runtime-$($manifest.version)"
    documentNamespace = "https://github.com/FlameskyDexive/OpenHarmony.NET.Runtime/releases/$($manifest.version)/spdx"
    creationInfo = [ordered]@{
        created = $CreatedUtc
        creators = @('Organization: OpenHarmony.NET', 'Tool: OpenHarmony.NET.Runtime RuntimePackager')
    }
    documentDescribes = @($spdxPackages | ForEach-Object { $_.SPDXID })
    packages = @($spdxPackages)
    annotations = @([ordered]@{
        annotationDate = $CreatedUtc
        annotationType = 'OTHER'
        annotator = 'Organization: OpenHarmony.NET'
        comment = "releaseChannel=$Channel; api26SdkReleaseType=$Api26ReleaseType; bindingsCommit=$($manifest.bindingsCommit)"
    })
}

$spdxPath = Join-Path $MetadataRoot 'sbom.spdx.json'
Write-Utf8NoBom -Path $spdxPath -Content (($spdx | ConvertTo-Json -Depth 12) + "`n")

$checksumLines = @(
    "$(Get-Sha256 -Path $manifestPath)  manifest.json",
    "$(Get-Sha256 -Path $spdxPath)  sbom.spdx.json"
)
Write-Utf8NoBom -Path (Join-Path $MetadataRoot 'SHA256SUMS') -Content (($checksumLines -join "`n") + "`n")

Write-Output "Exported $($manifest.version) $Channel metadata to '$MetadataRoot'."
