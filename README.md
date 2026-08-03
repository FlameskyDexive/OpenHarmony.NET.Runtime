# OpenHarmony .NET 10 Runtime

这是 OpenHarmony-NET/runtime 的 .NET 10 NativeAOT runtime 包仓库，目标架构为
`arm64-v8a` 真机和 `x86_64` 模拟器。

## 支持范围

- 运行时基线 API：13
- 支持 API：13-24、26（API25 明确不支持）
- RID：`linux-musl-arm64`、`linux-musl-x64`
- 两个 ABI 都使用经过 provenance 校验的 API13 基线 payload；manifest v2 为每个 API/ABI 显式记录 alias。

## 生成包

先在 runtime 仓库按 API13 编译并验证基线产物，再运行打包器。`--api-levels` 是清单中的已验证兼容矩阵，实际二进制按 API13 基线打包：

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

第三方项目（包括 XEngine）只需要 .NET 10、已发布的 ABI runtime 包和
`OpenHarmony.NET.PublishAotCross`，不得导入本仓库的 `runtime.targets` 或引用本地
`releases/` 目录。x86_64 模拟器项目使用：

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <RuntimeIdentifier>linux-musl-x64</RuntimeIdentifier>
  <PublishAot>true</PublishAot>
  <SelfContained>true</SelfContained>
  <OpenHarmonyTarget>true</OpenHarmonyTarget>
  <OpenHarmonyApiLevel>13</OpenHarmonyApiLevel>
  <OpenHarmonyAbi>x86_64</OpenHarmonyAbi>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="OpenHarmony.NET.Runtime.NativeAot.x86_64" Version="10.0.10-ohos.2-preview.1" />
  <PackageReference Include="OpenHarmony.NET.PublishAotCross" Version="42.42.42-dev" />
</ItemGroup>
```

arm64-v8a 真机项目将 runtime 包改为
`OpenHarmony.NET.Runtime.NativeAot.arm64-v8a`，RID 改为
`linux-musl-arm64`，并设置 `OpenHarmonyAbi=arm64-v8a`。
`OpenHarmonyApiLevel` 是消费方的 build API，必须是 13-24 或 26；API25
会被拒绝。包内 manifest v2 将 build API 显式映射到经过 provenance 和
SHA-256 校验的 API13 baseline alias，不依赖本地源码或目录命名推断。

XEngine 的包消费合同是固定的：项目只能包含下面三个 `PackageReference`，且版本
必须完全一致。不得有 `ProjectReference`、本地 `<Import>`、`<Reference HintPath>`，
也不得引用 `runtime/`、`OpenHarmony.NET.Runtime/releases/` 或未发布的 runtime
产物。两个 runtime 包使用相同的 API13 baseline；消费方通过
`OpenHarmonyApiLevel` 选择 API13 或 API26、通过 `OpenHarmonyAbi` 选择 ABI。

```xml
<PackageReference Include="OpenHarmony.NET.Runtime.NativeAot.x86_64" Version="10.0.10-ohos.2-preview.1" />
<PackageReference Include="OpenHarmony.NET.Runtime.NativeAot.arm64-v8a" Version="10.0.10-ohos.2-preview.1" />
<PackageReference Include="OpenHarmony.NET.PublishAotCross" Version="42.42.42-dev" />
```

发布时为目标 API 指定已安装的 OpenHarmony Native SDK 根目录：

```powershell
dotnet publish -c Release `
  -p:OpenHarmonyApiLevel=26 `
  -p:OpenHarmonyAbi=x86_64 `
  -p:OpenHarmonySdkRoot=$env:LOCALAPPDATA\OpenHarmony\Sdk
```

XEngine 仓库中的 `../OpenHarmony.Blazor.Hybrid/tests/XEngine.ConsumerFixture` 是可复现的
clean-room 验收。将运行时发布目录、其发布 `SHA256SUMS`、PublishAotCross 的已发布包及其
发布渠道记录的 SHA-256 作为输入，其中 runtime 目录必须直接包含两个 runtime `.nupkg`：

```powershell
../OpenHarmony.Blazor.Hybrid/tests/XEngine.ConsumerFixture/run-clean-consumer.ps1 `
  -RuntimePackageDirectory ./releases/10.0.10-ohos.2-preview.1 `
  -RuntimePackageChecksumsPath ./release/10.0.10-ohos.2-preview.1/SHA256SUMS `
  -PublishAotCrossPackagePath <path-to>/OpenHarmony.NET.PublishAotCross.42.42.42-dev.nupkg `
  -PublishAotCrossPackageSha256 <published-sha256> `
  -SdkRoot $env:LOCALAPPDATA\OpenHarmony\Sdk
```

它只将这三个不可变 `.nupkg` 复制到临时 feed，隔离 NuGet cache 和 CLI home，然后
发布 API13/API26 x `x86_64`/`arm64-v8a` 四个 NativeAOT case。验收会验证交叉编译包
与发布 SHA-256 一致、两个 runtime 包与发布 `SHA256SUMS` 一致、每个 ABI 解析到对应 runtime
包、从还原的 package targets 读取实际 `OpenHarmonyTargetTriple`，并在 publish binlog 中验证
对应 `--target=` 链接参数、通过 SDK `llvm-readelf` 验证 ELF machine，并扫描 assets、
生成的 props/targets 和 restore/publish binlog，拒绝 `D:\Engine\OHOS\runtime` 或任何
本地 `OpenHarmony.NET.Runtime\releases` 路径。成功后会写出
`clean-consumer-evidence.json`（UTF-8、LF newline、无本地绝对路径）并删除临时目录；
失败时保留临时目录以便诊断。该 host clean-room gate 不替代后续的 arm64 真机验收。

## 校验

```powershell
dotnet test OpenHarmony.NET.Runtime.slnx
```

`schema/runtime-manifest.schema.json` 定义了 manifest 格式；每个文件的 SHA-256 都记录在 manifest 中，便于发布前校验。

可审计的发布 manifest、SPDX 2.3 SBOM、校验和及 preview/stable 策略位于
`release/`。API26 SDK 仍为 Beta 时只允许 preview/rc；发布还必须提供 arm64
真机和 x86_64 模拟器验收日志的 SHA-256，详见 `release/README.md`。
