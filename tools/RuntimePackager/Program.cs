using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenHarmony.RuntimePackager;

public sealed record RuntimePackagerOptions(
    string SourceRoot,
    string OutputRoot,
    string Version,
    string RuntimeCommit,
    string BindingsCommit,
    IReadOnlyList<int> ApiLevels,
    string Configuration,
    IReadOnlyList<string> Architectures);

public sealed class RuntimeManifest
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; } = 1;
    [JsonPropertyName("version")] public required string Version { get; init; }
    [JsonPropertyName("runtimeCommit")] public required string RuntimeCommit { get; init; }
    [JsonPropertyName("bindingsCommit")] public required string BindingsCommit { get; init; }
    [JsonPropertyName("minimumApi")] public required int MinimumApi { get; init; }
    [JsonPropertyName("verifiedApis")] public required IReadOnlyList<int> VerifiedApis { get; init; }
    [JsonPropertyName("architectures")] public required IReadOnlyList<string> Architectures { get; init; }
    [JsonPropertyName("toolVersions")] public required IReadOnlyDictionary<string, string> ToolVersions { get; init; }
    [JsonPropertyName("packages")] public required IReadOnlyList<RuntimePackageManifest> Packages { get; init; }
    [JsonPropertyName("sbom")] public required RuntimeSbomManifest Sbom { get; init; }
}

public sealed class RuntimePackageManifest
{
    [JsonPropertyName("apiLevel")] public required int ApiLevel { get; init; }
    [JsonPropertyName("architecture")] public required string Architecture { get; init; }
    [JsonPropertyName("abi")] public required string Abi { get; init; }
    [JsonPropertyName("rid")] public required string Rid { get; init; }
    [JsonPropertyName("root")] public required string Root { get; init; }
    [JsonPropertyName("sdk")] public required RuntimeSdkManifest Sdk { get; init; }
    [JsonPropertyName("dependencies")] public required IReadOnlyList<string> Dependencies { get; init; }
    [JsonPropertyName("licenses")] public required IReadOnlyList<string> Licenses { get; init; }
    [JsonPropertyName("files")] public required IReadOnlyList<RuntimeFileManifest> Files { get; init; }
}

public sealed class RuntimeSdkManifest
{
    [JsonPropertyName("folder")] public required string Folder { get; init; }
    [JsonPropertyName("packageVersion")] public required string PackageVersion { get; init; }
    [JsonPropertyName("sha256")] public required string Sha256 { get; init; }
}

public sealed class RuntimeSbomManifest
{
    [JsonPropertyName("format")] public required string Format { get; init; }
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("sha256")] public required string Sha256 { get; init; }
}

public sealed class RuntimeFileManifest
{
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("size")] public required long Size { get; init; }
    [JsonPropertyName("sha256")] public required string Sha256 { get; init; }
}

public static class RuntimePackagerService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    private static readonly IReadOnlyDictionary<string, (string Abi, string Rid)> ArchitectureMap =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["arm64"] = ("arm64-v8a", "linux-musl-arm64"),
            ["x64"] = ("x86_64", "linux-musl-x64")
        };

    public static RuntimeManifest Package(RuntimePackagerOptions options)
    {
        ValidateOptions(options);
        var baselineApi = options.ApiLevels.Min();
        var packages = new List<RuntimePackageManifest>();
        var releaseRoot = Path.Combine(options.OutputRoot, "releases", options.Version);

        foreach (var architecture in options.Architectures.OrderBy(value => value, StringComparer.Ordinal))
        {
            var (abi, rid) = ArchitectureMap[architecture];
            var source = ResolveSource(options.SourceRoot, architecture, rid, options.Configuration);
            var packageRoot = Path.Combine(releaseRoot, abi, "runtime-pack");
            if (Directory.Exists(packageRoot))
            {
                Directory.Delete(packageRoot, recursive: true);
            }

            CopyDirectory(source.Sdk, Path.Combine(packageRoot, "sdk"));
            CopyDirectory(source.Framework, Path.Combine(packageRoot, "framework"));
            CopyDirectory(source.Native, Path.Combine(packageRoot, "native"));

            var provenanceTarget = Path.Combine(packageRoot, "provenance", "runtime-build-provenance.json");
            if (File.Exists(source.Provenance))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(provenanceTarget)!);
                File.Copy(source.Provenance, provenanceTarget, overwrite: true);
            }

            var provenance = ReadProvenance(source.Provenance);
            packages.Add(new RuntimePackageManifest
            {
                ApiLevel = baselineApi,
                Architecture = architecture,
                Abi = abi,
                Rid = rid,
                Root = $"{abi}/runtime-pack",
                Sdk = new RuntimeSdkManifest
                {
                    Folder = provenance.GetValueOrDefault("sdkFolder", baselineApi.ToString()),
                    PackageVersion = provenance.GetValueOrDefault("sdkPackageVersion", "unknown"),
                    Sha256 = ComputeDirectoryHash(Path.Combine(packageRoot, "sdk"))
                },
                Dependencies = Directory.EnumerateFiles(Path.Combine(packageRoot, "native"), "*", SearchOption.AllDirectories)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .Cast<string>()
                    .ToArray(),
                Licenses = EnumerateFiles(packageRoot)
                    .Where(file => file.Path.Contains("license", StringComparison.OrdinalIgnoreCase))
                    .Select(file => file.Path)
                    .ToArray(),
                Files = EnumerateFiles(packageRoot).ToArray()
            });
        }

        var toolVersions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dotnetRuntime"] = Environment.Version.ToString(),
            ["packager"] = typeof(RuntimePackagerService).Assembly.GetName().Version?.ToString() ?? "unknown"
        };
        var sbomPath = Path.Combine(releaseRoot, "sbom.json");
        var sbom = new
        {
            bomFormat = "CycloneDX",
            specVersion = "1.5",
            serialNumber = "urn:uuid:00000000-0000-0000-0000-000000000001",
            version = 1,
            components = packages.Select(package => new
            {
                type = "application",
                name = $"OpenHarmony.NET.Runtime.{package.Rid}",
                version = options.Version,
                scope = "required",
                hashes = new[] { new { alg = "SHA-256", content = package.Sdk.Sha256 } }
            }).ToArray()
        };
        Directory.CreateDirectory(releaseRoot);
        File.WriteAllText(sbomPath, JsonSerializer.Serialize(sbom, JsonOptions) + Environment.NewLine);
        var manifest = new RuntimeManifest
        {
            Version = options.Version,
            RuntimeCommit = options.RuntimeCommit,
            BindingsCommit = options.BindingsCommit,
            MinimumApi = baselineApi,
            VerifiedApis = options.ApiLevels.OrderBy(value => value).ToArray(),
            Architectures = options.Architectures.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            ToolVersions = toolVersions,
            Packages = packages,
            Sbom = new RuntimeSbomManifest
            {
                Format = "CycloneDX 1.5 JSON",
                Path = "sbom.json",
                Sha256 = ComputeFileHash(sbomPath)
            }
        };

        var manifestPath = Path.Combine(releaseRoot, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine);
        return manifest;
    }

    private static void ValidateOptions(RuntimePackagerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SourceRoot)) throw new ArgumentException("SourceRoot is required.");
        if (string.IsNullOrWhiteSpace(options.OutputRoot)) throw new ArgumentException("OutputRoot is required.");
        if (string.IsNullOrWhiteSpace(options.Version)) throw new ArgumentException("Version is required.");
        if (options.ApiLevels.Count == 0 || options.ApiLevels.Any(api => api is not (15 or 18 or 20 or 23 or 26)))
            throw new ArgumentException("API levels must be selected from 15, 18, 20, 23, and 26.");
        if (options.Architectures.Count == 0 || options.Architectures.Any(api => !ArchitectureMap.ContainsKey(api)))
            throw new ArgumentException("Architectures must be selected from arm64 and x64.");
    }

    private static SourceLayout ResolveSource(string sourceRoot, string architecture, string rid, string configuration)
    {
        var root = Path.GetFullPath(sourceRoot);
        var candidates = new[]
        {
            root,
            Path.Combine(root, "artifacts")
        };
        foreach (var artifacts in candidates)
        {
            var coreclr = Path.Combine(artifacts, "bin", "coreclr", $"openharmony.{architecture}.{configuration}");
            var runtime = Path.Combine(artifacts, "bin", $"microsoft.netcore.app.runtime.{rid}", configuration, "runtimes", rid);
            var sdk = Path.Combine(coreclr, "aotsdk");
            var framework = Path.Combine(runtime, "lib", "net10.0");
            var native = Path.Combine(runtime, "native");
            if (Directory.Exists(sdk) && Directory.Exists(framework) && Directory.Exists(native))
            {
                return new SourceLayout(sdk, framework, native, Path.Combine(coreclr, "runtime-build-provenance.json"));
            }
        }

        throw new DirectoryNotFoundException($"Could not find OpenHarmony {architecture} runtime artifacts under '{root}'.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static IEnumerable<RuntimeFileManifest> EnumerateFiles(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(value => value, StringComparer.Ordinal))
        {
            using var stream = File.OpenRead(file);
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            yield return new RuntimeFileManifest
            {
                Path = relative,
                Size = stream.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()
            };
        }
    }

    private static Dictionary<string, string> ReadProvenance(string path)
    {
        if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateObject()
            .Where(property => property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            .ToDictionary(property => property.Name, property => property.Value.ToString(), StringComparer.Ordinal);
    }

    private static string ComputeDirectoryHash(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in EnumerateFiles(root))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes($"{file.Path}\n{file.Sha256}\n{file.Size}\n"));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed record SourceLayout(string Sdk, string Framework, string Native, string Provenance);
}

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = Parse(args);
            var manifest = RuntimePackagerService.Package(options);
            Console.WriteLine($"Packaged {manifest.Version}: {manifest.Packages.Count} architecture package(s), baseline API {manifest.MinimumApi}.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static RuntimePackagerOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException("Arguments must use --name value syntax.");
            values[args[index][2..]] = args[++index];
        }

        string Required(string name) => values.TryGetValue(name, out var value) ? value : throw new ArgumentException($"--{name} is required.");
        var apiLevels = (values.TryGetValue("api-levels", out var apiValue) ? apiValue : "15")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse).Distinct().OrderBy(value => value).ToArray();
        var architectures = (values.TryGetValue("architectures", out var archValue) ? archValue : "arm64,x64")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new RuntimePackagerOptions(
            Required("source-root"),
            Required("output-root"),
            Required("version"),
            values.GetValueOrDefault("runtime-commit", "unknown"),
            values.GetValueOrDefault("bindings-commit", "unknown"),
            apiLevels,
            values.GetValueOrDefault("configuration", "Release"),
            architectures);
    }
}
