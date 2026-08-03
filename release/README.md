# .NET 10 OpenHarmony release metadata

`release/10.0.10-ohos.2-preview.1` contains the tracked, reviewable metadata for
the generated runtime payload. Runtime files and NuGet packages remain workflow
artifacts under `releases/10.0.10-ohos.2-preview.1` and are not committed to Git.

The package baseline is OpenHarmony API13, covering arm64-v8a devices and x86_64
emulators. Manifest v2 has explicit aliases for API13 through API24 and API26;
API25 is excluded. `manifest.json` records every payload file and SHA-256 digest;
`sbom.spdx.json` is the SPDX 2.3 release SBOM; `SHA256SUMS` protects the tracked
metadata files.

API26 SDK `26.0.0.25` is a Beta package. Therefore this release may only be
published as preview or rc. A stable release is blocked until the API26 SDK is
Release quality and the manifest is regenerated. Publishing also requires
fresh arm64 physical-device and x86_64 emulator evidence from the sample HAP
workflow; a host-only build is not release evidence.

Before publishing, an independent XEngine-style consumer must restore only the
immutable package IDs below from a temporary feed (plus their public NuGet
dependencies), with an isolated package cache and no project/source reference:

- `OpenHarmony.NET.Runtime.NativeAot.x86_64/10.0.10-ohos.2-preview.1`
- `OpenHarmony.NET.Runtime.NativeAot.arm64-v8a/10.0.10-ohos.2-preview.1`
- `OpenHarmony.NET.PublishAotCross/42.42.42-dev`

The clean consumer gate publishes API13 and API26 for x86_64 and arm64-v8a,
verifies the `x86_64-linux-ohos` and `aarch64-linux-ohos` ELF machines, and scans
the generated assets, NuGet props/targets, and binlogs for runtime source or local
`releases/` paths. Identical API13/API26 ELF hashes are expected when both APIs
resolve the manifest's API13 baseline alias.

Run the gate against the generated release directory and an immutable
PublishAotCross package. The runtime directory must directly contain these two
package files; the third input must be exactly
`OpenHarmony.NET.PublishAotCross.42.42.42-dev.nupkg`:

```powershell
../OpenHarmony.Blazor.Hybrid/tests/XEngine.ConsumerFixture/run-clean-consumer.ps1 `
  -RuntimePackageDirectory ./releases/10.0.10-ohos.2-preview.1 `
  -RuntimePackageChecksumsPath ./release/10.0.10-ohos.2-preview.1/SHA256SUMS `
  -PublishAotCrossPackagePath <path-to>/OpenHarmony.NET.PublishAotCross.42.42.42-dev.nupkg `
  -PublishAotCrossPackageSha256 <published-sha256> `
  -SdkRoot $env:LOCALAPPDATA\OpenHarmony\Sdk
```

The runner verifies the cross-compiler package against its published SHA-256 and
each runtime package against the release `SHA256SUMS`, then creates a throwaway
feed containing only those three `.nupkg` files, uses isolated `NUGET_PACKAGES`
and `DOTNET_CLI_HOME`, and allows nuget.org only for transitive dependencies. It
rejects `ProjectReference` and local runtime imports before restore, records the
resolved `OpenHarmonyTargetTriple` from the restored package targets, verifies
the corresponding `--target=` linker argument in the publish binlog, scans
generated assets/props/targets/binlogs for
`D:\Engine\OHOS\runtime` and any local `OpenHarmony.NET.Runtime\releases` path,
and writes normalized `clean-consumer-evidence.json`. Successful runs remove the
temporary work directory; failed runs retain it for investigation. This is a
host-side package-consumer gate, not substitute evidence for required device
acceptance.

Regenerate and verify metadata:

```powershell
./tools/export-release-metadata.ps1 `
  -GeneratedReleaseRoot ./releases/10.0.10-ohos.2-preview.1 `
  -MetadataRoot ./release/10.0.10-ohos.2-preview.1 `
  -Channel preview -Api26ReleaseType Beta

./tools/verify-release.ps1 `
  -MetadataRoot ./release/10.0.10-ohos.2-preview.1 `
  -GeneratedReleaseRoot ./releases/10.0.10-ohos.2-preview.1 `
  -Channel preview -Api26ReleaseType Beta
```
