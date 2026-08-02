# OpenHarmony .NET 10 Runtime

这是 OpenHarmony-NET/runtime 的 .NET 10 NativeAOT runtime 包仓库，目标架构为
`arm64-v8a` 真机和 `x86_64` 模拟器。

## 支持范围

- 运行时基线 API：13
- 支持 API：13-24、26（API25 明确不支持）
- RID：`linux-musl-arm64`、`linux-musl-x64`
- 两个 ABI 都使用经过 provenance 校验的 API13 基线 payload；manifest v2 为每个 API/ABI 显式记录 alias。

## 生成包

先在 runtime 仓库按目标 API 编译并验证产物，再运行打包器。`--api-levels` 是清单中的已验证矩阵，实际二进制默认按 API15 基线打包：

```powershell
dotnet run --project tools/RuntimePackager/RuntimePackager.csproj -- `
  --source-root D:\Engine\OHOS\runtime `
  --output-root . `
  --version 10.0.10-ohos.2-preview.1 `
  --runtime-commit <runtime-commit> `
  --bindings-commit <bindings-commit> `
  --api-levels 13,14,15,16,17,18,19,20,21,22,23,24,26 `
  --architectures arm64,x64
```

输出位于 `releases/<version>/`，包括 `manifest.json`、`sbom.json`、每个架构的
`{arm64-v8a,x86_64}/runtime-pack/{framework,sdk,native}` 以及源构建 provenance。

## 在项目中使用

项目需要 .NET 10 和 PublishAotCross：

```xml
<PropertyGroup>
  <RuntimeIdentifier>linux-musl-arm64</RuntimeIdentifier>
  <OpenHarmonyTarget>true</OpenHarmonyTarget>
  <OpenHarmonyApiLevel>13</OpenHarmonyApiLevel>
</PropertyGroup>
<Import Project="path/to/OpenHarmony.NET.Runtime/runtime.targets" />
```

模拟器将 RID 改为 `linux-musl-x64`。`OpenHarmonyApiLevel` 必须存在于 v2
manifest 的显式映射中；OpenHarmony 构建不会回退到旧的 `9.0.0` 目录。

## 校验

```powershell
dotnet test OpenHarmony.NET.Runtime.slnx
```

`schema/runtime-manifest.schema.json` 定义了 manifest 格式；每个文件的 SHA-256 都记录在 manifest 中，便于发布前校验。

可审计的发布 manifest、SPDX 2.3 SBOM、校验和及 preview/stable 策略位于
`release/`。API26 SDK 仍为 Beta 时只允许 preview/rc；发布还必须提供 arm64
真机和 x86_64 模拟器验收日志的 SHA-256，详见 `release/README.md`。
