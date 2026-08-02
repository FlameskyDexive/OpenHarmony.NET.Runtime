using System.Security.Cryptography;
using System.Text.Json;
using OpenHarmony.RuntimePackager;
using Xunit;

namespace RuntimePackager.Tests;

public sealed class ManifestTests
{
    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "runtime.linux-musl-arm64.microsoft.dotnet.ilcompiler", "runtime.arm64.targets")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the OpenHarmony.NET.Runtime repository root.");
        }
    }

    [Fact]
    public void NativeAotTargetsConsumeNativeFrameworkLibrariesFromNativeDirectory()
    {
        foreach (var target in new[]
        {
            Path.Combine(RepositoryRoot, "runtime.linux-musl-arm64.microsoft.dotnet.ilcompiler", "runtime.arm64.targets"),
            Path.Combine(RepositoryRoot, "runtime.linux-musl-x64.microsoft.dotnet.ilcompiler", "runtime.x64.targets")
        })
        {
            var text = File.ReadAllText(target);
            Assert.Contains("<IlcFrameworkNativePath>$(OpenHarmonyRuntimePackPath)\\native\\</IlcFrameworkNativePath>", text);
            Assert.Contains("Condition=\"'$(OpenHarmonyTarget)' != 'true'\"", text);
        }
    }

    [Fact]
    public void NugetRecipeCarriesManifestRuntimePackAndBuildTransitiveValidation()
    {
        var recipe = File.ReadAllText(Path.Combine(RepositoryRoot, "pack", "OpenHarmony.NET.Runtime.NativeAot.csproj"));
        Assert.Contains("PackagePath=\"runtime-pack\"", recipe);
        Assert.Contains("manifest.json", recipe);
        Assert.Contains("PackagePath=\"buildTransitive\"", recipe);

        foreach (var abi in new[] { "arm64-v8a", "x86_64" })
        {
            var targets = File.ReadAllText(Path.Combine(RepositoryRoot, "buildTransitive", $"OpenHarmony.NET.Runtime.NativeAot.{abi}.targets"));
            Assert.Contains("BeforeTargets=\"PrepareForBuild\"", targets);
            Assert.Contains("schemaVersion&quot;: 2", targets);
            Assert.Contains("sourceDirty&quot;: true", targets);
            Assert.DoesNotContain("9.0.0", targets);
        }
    }

    [Fact]
    public void PackagesBothArchitecturesWithDeterministicManifest()
    {
        var root = CreateFixture();
        try
        {
            var output = Path.Combine(root, "out");
            var options = Options(root, output);
            var first = RuntimePackagerService.Package(options);
            var firstText = File.ReadAllText(Path.Combine(output, "releases", options.Version, "manifest.json"));
            var second = RuntimePackagerService.Package(options);
            var secondText = File.ReadAllText(Path.Combine(output, "releases", options.Version, "manifest.json"));

            Assert.Equal(new[] { "arm64", "x64" }, first.Architectures);
            Assert.Equal(new[] { 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 26 }, first.VerifiedApis);
            Assert.Equal(2, first.Packages.Count);
            Assert.Equal(firstText, secondText);
            Assert.Contains(first.Packages.SelectMany(package => package.Files), file => file.Path == "sdk/libaot.a");
            Assert.Equal("CycloneDX 1.5 JSON", first.Sbom.Format);
            Assert.All(first.Packages, package => Assert.Equal($"{package.Abi}/runtime-pack", package.Root));
            Assert.All(first.Packages, package =>
                Assert.Equal(package.Dependencies.Distinct(StringComparer.Ordinal), package.Dependencies));
            Assert.Equal(second.Packages.Count, first.Packages.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ManifestV2MapsEverySupportedApiToTheApi13Baseline()
    {
        var root = CreateFixture();
        try
        {
            var options = Options(root, Path.Combine(root, "out")) with
            {
                ApiLevels = new[] { 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 26 }
            };

            var manifest = RuntimePackagerService.Package(options);

            Assert.Equal(2, manifest.SchemaVersion);
            Assert.Equal(13, manifest.RuntimeBaselineApi);
            Assert.Equal(options.ApiLevels, manifest.SupportedApis);
            Assert.Equal(26, manifest.CompatibilityEntries.Count);
            Assert.Equal("alias", manifest.Resolve(14, "x86_64").CompatibilityKind);
            Assert.Equal(13, manifest.Resolve(14, "x86_64").RuntimeApi);
            Assert.Throws<ArgumentOutOfRangeException>(() => manifest.Resolve(25, "x86_64"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsUnsupportedApi()
    {
        var root = CreateFixture();
        try
        {
            var options = Options(root, Path.Combine(root, "out")) with { ApiLevels = new[] { 25 } };
            Assert.Throws<ArgumentException>(() => RuntimePackagerService.Package(options));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsDirtyRuntimeProvenance()
    {
        var root = CreateFixture();
        try
        {
            var provenance = Directory.GetFiles(root, "runtime-build-provenance.json", SearchOption.AllDirectories)[0];
            File.WriteAllText(provenance, "{\"buildApi\":13,\"sourceDirty\":true,\"sdkManifestSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}");

            Assert.Throws<InvalidDataException>(() =>
                RuntimePackagerService.Package(Options(root, Path.Combine(root, "out"))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsSdkManifestHashThatDoesNotMatchCatalog()
    {
        var root = CreateFixture();
        try
        {
            var options = Options(root, Path.Combine(root, "out"));
            File.WriteAllText(options.SdkCatalogPath!, "{\"packages\":[{\"apiLevel\":13,\"manifestSha256\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"}]}");
            Assert.Throws<InvalidDataException>(() => RuntimePackagerService.Package(options));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TrackedReleaseMetadataCoversBothAbisAndHasValidChecksums()
    {
        var releaseRoot = Path.Combine(RepositoryRoot, "release", "10.0.10-ohos.2-preview.1");
        var manifestPath = Path.Combine(releaseRoot, "manifest.json");
        var spdxPath = Path.Combine(releaseRoot, "sbom.spdx.json");
        var sumsPath = Path.Combine(releaseRoot, "SHA256SUMS");

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;
        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("10.0.10-ohos.2-preview.1", root.GetProperty("version").GetString());
        Assert.Equal(13, root.GetProperty("runtimeBaselineApi").GetInt32());
        Assert.Equal(new[] { 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 26 }, root.GetProperty("supportedApis").EnumerateArray().Select(value => value.GetInt32()));
        Assert.Equal(26, root.GetProperty("compatibilityEntries").GetArrayLength());
        Assert.Equal(new[] { "arm64-v8a", "x86_64" }, root.GetProperty("packages").EnumerateArray().Select(value => value.GetProperty("abi").GetString()));
        Assert.All(root.GetProperty("packages").EnumerateArray(), package =>
        {
            var dependencies = package.GetProperty("dependencies").EnumerateArray().Select(value => value.GetString()).ToArray();
            Assert.Equal(dependencies.Distinct(StringComparer.Ordinal), dependencies);
        });

        using var spdx = JsonDocument.Parse(File.ReadAllText(spdxPath));
        Assert.Equal("SPDX-2.3", spdx.RootElement.GetProperty("spdxVersion").GetString());
        Assert.Equal(2, spdx.RootElement.GetProperty("packages").GetArrayLength());

        var sums = File.ReadAllLines(sumsPath)
            .Select(line => line.Split("  ", 2, StringSplitOptions.None))
            .ToDictionary(parts => parts[1], parts => parts[0], StringComparer.Ordinal);
        Assert.Equal(Hash(manifestPath), sums["manifest.json"]);
        Assert.Equal(Hash(spdxPath), sums["sbom.spdx.json"]);
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static RuntimePackagerOptions Options(string source, string output) => new(
        source, output, "10.0.10-ohos.test", "runtime-sha", "bindings-sha", new[] { 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 26 }, "Release", new[] { "arm64", "x64" })
    {
        SdkCatalogPath = Path.Combine(source, "sdk-catalog.json")
    };

    private static string CreateFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "openharmony-runtime-packager-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(root + Path.DirectorySeparatorChar + "sdk-catalog.json", "{\"packages\":[{\"apiLevel\":13,\"manifestSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}]}");
        foreach (var architecture in new[] { "arm64", "x64" })
        {
            var coreclr = Path.Combine(root, "artifacts", "bin", "coreclr", $"openharmony.{architecture}.Release");
            var runtime = Path.Combine(root, "artifacts", "bin", $"microsoft.netcore.app.runtime.linux-musl-{architecture}", "Release", "runtimes", $"linux-musl-{architecture}");
            Directory.CreateDirectory(Path.Combine(coreclr, "aotsdk"));
            Directory.CreateDirectory(Path.Combine(runtime, "lib", "net10.0"));
            Directory.CreateDirectory(Path.Combine(runtime, "native"));
            Directory.CreateDirectory(Path.Combine(runtime, "native", "first"));
            Directory.CreateDirectory(Path.Combine(runtime, "native", "second"));
            File.WriteAllText(Path.Combine(coreclr, "aotsdk", "libaot.a"), architecture);
            File.WriteAllText(Path.Combine(runtime, "lib", "net10.0", "System.Native.a"), architecture);
            File.WriteAllText(Path.Combine(runtime, "native", "libc++_shared.so"), architecture);
            File.WriteAllText(Path.Combine(runtime, "native", "first", "libduplicate.so"), architecture);
            File.WriteAllText(Path.Combine(runtime, "native", "second", "libduplicate.so"), architecture);
            File.WriteAllText(
                Path.Combine(coreclr, "runtime-build-provenance.json"),
                "{\"buildApi\":13,\"sourceDirty\":false,\"sdkManifestSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}");
        }
        return root;
    }
}
