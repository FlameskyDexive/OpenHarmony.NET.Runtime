# OpenHarmony .NET 10 Runtime

这是 OpenHarmony-NET/runtime 的 .NET 10 NativeAOT runtime 包仓库，目标架构为
`arm64-v8a` 真机和 `x86_64` 模拟器。

## 支持范围

- 最低 API：15（HarmonyOS 5.0）
- 已验证 API：15、18、20、23、26
- RID：`linux-musl-arm64`、`linux-musl-x64`
- API15 是兼容基线；如果没有单独的 API 目录，API18/20/23/26 会使用 API15 包。

## 生成包

先在 runtime 仓库按目标 API 编译并验证产物，再运行打包器。`--api-levels` 是清单中的已验证矩阵，实际二进制默认按 API15 基线打包：

```powershell
dotnet run --project tools/RuntimePackager/RuntimePackager.csproj -- `
  --source-root D:\Engine\OHOS\runtime `
  --output-root . `
  --version 10.0.10-ohos.1 `
  --runtime-commit <runtime-commit> `
  --bindings-commit <bindings-commit> `
  --api-levels 15,18,20,23,26 `
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
  <OpenHarmonyApiLevel>15</OpenHarmonyApiLevel>
</PropertyGroup>
<Import Project="path/to/OpenHarmony.NET.Runtime/runtime.targets" />
```

模拟器将 RID 改为 `linux-musl-x64`，API18/20/23/26 只需调整
`OpenHarmonyApiLevel`。targets 会优先使用对应 API 的独立包，缺失时回退到 API15 基线，并在包不存在时给出明确错误。

## 校验

```powershell
dotnet test OpenHarmony.NET.Runtime.slnx
```

`schema/runtime-manifest.schema.json` 定义了 manifest 格式；每个文件的 SHA-256 都记录在 manifest 中，便于发布前校验。
