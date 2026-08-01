# .NET 10 OpenHarmony release metadata

`release/10.0.10-ohos.1` contains the tracked, reviewable metadata for the
generated runtime payload. The 350 MB runtime files remain workflow artifacts
under `releases/10.0.10-ohos.1` and are intentionally not committed to Git.

The package baseline is HarmonyOS API15, covering arm64-v8a devices and x86_64
emulators. Compile compatibility is verified for API15, API18, API20, API23,
and API26. `manifest.json` records every payload file and SHA-256 digest;
`sbom.spdx.json` is the SPDX 2.3 release SBOM; `SHA256SUMS` protects the tracked
metadata files.

API26 SDK `26.0.0.25` is a Beta package. Therefore `10.0.10-ohos.1` may only be
published as preview or rc. A stable release is blocked until the API26 SDK is
Release quality and the manifest is regenerated. Publishing also requires
fresh arm64 physical-device and x86_64 emulator evidence from the sample HAP
workflow; a host-only build is not release evidence.

Regenerate and verify metadata:

```powershell
./tools/export-release-metadata.ps1 `
  -GeneratedReleaseRoot ./releases/10.0.10-ohos.1 `
  -MetadataRoot ./release/10.0.10-ohos.1 `
  -Channel preview -Api26ReleaseType Beta

./tools/verify-release.ps1 `
  -MetadataRoot ./release/10.0.10-ohos.1 `
  -GeneratedReleaseRoot ./releases/10.0.10-ohos.1 `
  -Channel preview -Api26ReleaseType Beta
```
