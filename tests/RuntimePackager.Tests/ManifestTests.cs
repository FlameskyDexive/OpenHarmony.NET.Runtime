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
            Assert.Equal(new[] { 15, 18, 20, 23, 26 }, first.VerifiedApis);
            Assert.Equal(2, first.Packages.Count);
            Assert.Equal(firstText, secondText);
            Assert.Contains(first.Packages.SelectMany(package => package.Files), file => file.Path == "sdk/libaot.a");
            Assert.Equal("CycloneDX 1.5 JSON", first.Sbom.Format);
            Assert.All(first.Packages, package => Assert.Equal($"{package.Abi}/runtime-pack", package.Root));
            Assert.Equal(second.Packages.Count, first.Packages.Count);
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
            var options = Options(root, Path.Combine(root, "out")) with { ApiLevels = new[] { 14 } };
            Assert.Throws<ArgumentException>(() => RuntimePackagerService.Package(options));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RuntimePackagerOptions Options(string source, string output) => new(
        source, output, "10.0.10-ohos.test", "runtime-sha", "bindings-sha", new[] { 15, 18, 20, 23, 26 }, "Release", new[] { "arm64", "x64" });

    private static string CreateFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "openharmony-runtime-packager-" + Guid.NewGuid().ToString("N"));
        foreach (var architecture in new[] { "arm64", "x64" })
        {
            var coreclr = Path.Combine(root, "artifacts", "bin", "coreclr", $"openharmony.{architecture}.Release");
            var runtime = Path.Combine(root, "artifacts", "bin", $"microsoft.netcore.app.runtime.linux-musl-{architecture}", "Release", "runtimes", $"linux-musl-{architecture}");
            Directory.CreateDirectory(Path.Combine(coreclr, "aotsdk"));
            Directory.CreateDirectory(Path.Combine(runtime, "lib", "net10.0"));
            Directory.CreateDirectory(Path.Combine(runtime, "native"));
            File.WriteAllText(Path.Combine(coreclr, "aotsdk", "libaot.a"), architecture);
            File.WriteAllText(Path.Combine(runtime, "lib", "net10.0", "System.Native.a"), architecture);
            File.WriteAllText(Path.Combine(runtime, "native", "libc++_shared.so"), architecture);
            File.WriteAllText(Path.Combine(coreclr, "runtime-build-provenance.json"), "{}");
        }
        return root;
    }
}
