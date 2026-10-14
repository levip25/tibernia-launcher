using System.Text.Json.Serialization;

namespace OtsLauncher.Core;

public sealed class NetworkManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "2";

    [JsonPropertyName("generatedAt")]
    public string? GeneratedAt { get; set; }

    [JsonPropertyName("servers")]
    public List<ServerProfile> Servers { get; set; } = new();
}

public sealed class ServerProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }

    [JsonPropertyName("host")]
    public string? Host { get; set; }

    [JsonPropertyName("loginPort")]
    public int? LoginPort { get; set; }

    [JsonPropertyName("gamePort")]
    public int? GamePort { get; set; }

    [JsonPropertyName("manifestUrl")]
    public string? ManifestUrl { get; set; }

    [JsonPropertyName("packages")]
    public Dictionary<string, ClientPackage> Packages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ClientPackage
{
    [JsonPropertyName("packageVersion")]
    public string PackageVersion { get; set; } = "0";

    [JsonPropertyName("assetsVersion")]
    public string AssetsVersion { get; set; } = "0";

    [JsonPropertyName("launch")]
    public LaunchSpec Launch { get; set; } = new();

    [JsonPropertyName("appFiles")]
    public List<ManifestFile> AppFiles { get; set; } = new();

    [JsonPropertyName("assetFiles")]
    public List<ManifestFile> AssetFiles { get; set; } = new();
}

public sealed class LaunchSpec
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "executable";

    [JsonPropertyName("entry")]
    public string Entry { get; set; } = "otclient.exe";
}

public sealed class ManifestFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class ResolvedProfile
{
    public required ServerProfile Server { get; init; }
    public required PlatformId Platform { get; init; }
    public required ClientPackage Package { get; init; }

    public string Id => Server.Id;
    public string Name => Server.Name;
    public string Protocol => Server.Protocol ?? "";
    public string Version => Package.AssetsVersion;
    public LaunchKind LaunchKind => Package.Launch.Kind.ToLowerInvariant() switch
    {
        "executable" => LaunchKind.Executable,
        "package" => LaunchKind.Package,
        _ => LaunchKind.Unsupported
    };
}

public enum FileStatus { Missing, Mismatch, Ok }

public sealed class FileCheck
{
    public required ManifestFile File { get; init; }
    public required FileStatus Status { get; init; }
    public required string Bucket { get; init; }
}

public sealed class ProfileStatus
{
    public required ResolvedProfile Profile { get; init; }
    public required string LocalAssetsVersion { get; init; }
    public required IReadOnlyList<FileCheck> Files { get; init; }
    public bool IsReady => Files.All(f => f.Status == FileStatus.Ok);
    public int MissingOrChanged => Files.Count(f => f.Status != FileStatus.Ok);
}
