# OpenHarmony .NET 10 Runtime Compatibility Preview

Version: `10.0.10-ohos.2-preview.1`

This preview packages one provenance-verified API13 NativeAOT runtime baseline
for `arm64-v8a` and `x86_64`. The manifest exposes compatibility aliases for
build APIs 13-24 and 26. API25 is explicitly unsupported and absent from the
manifest, package targets, and release evidence.

## SDK And Compatibility State

- Installed Native SDK APIs 13, 14, 15, 18, 20, 23, and 26 were built and
  verified for both ABIs.
- Native SDK APIs 16, 17, 19, 21, 22, and 24 were unavailable from SDK Manager;
  their logical compatibility rows are retained as explicit `SKIPPED` results.
- Installed x86_64 emulator images for APIs 13-24 and 26 were tested even when
  the corresponding Native SDK was unavailable. The 91-case matrix contains 54
  executed passes and 37 explicit unavailable-HAP skips.
- API25 has neither an installable Native SDK nor an emulator image in this
  environment and is not a supported target.

## Release Policy

The API26 Native SDK is marked Beta, so this version may be published only to a
preview or RC channel. Stable publication is rejected by the release verifier.
The tracked manifest, SPDX 2.3 SBOM, `SHA256SUMS`, both runtime packages, and all
26 API/ABI compatibility rows must match before publication.

The current aggregate state is `READY_WITH_DEVICE_DEFERRED`, not `PASS`. No
API24 arm64 HDC target was connected for the final local acceptance run. The
publication workflow requires the SHA-256 of a canonical device-complete
evidence aggregate; API13 and API14 must pass cold start, smoke, and Counter
interaction on API24 first, followed by every remaining buildable API through
API24. API26 is not installed on that lower-version device.
