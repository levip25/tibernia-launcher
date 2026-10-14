using System.Text.Json;

namespace OtsLauncher.Core;

public sealed class ManifestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public NetworkManifest LoadFromFile(string path) => Parse(File.ReadAllText(path));

    public NetworkManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<NetworkManifest>(json, JsonOptions)
                       ?? throw new InvalidOperationException("Empty manifest");
        Validate(manifest);
        return manifest;
    }

    public void Validate(NetworkManifest manifest)
    {
        if (manifest.Servers.Count == 0)
            throw new InvalidOperationException("Manifest has no servers");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var server in manifest.Servers)
        {
            if (string.IsNullOrWhiteSpace(server.Id) || !ids.Add(server.Id))
                throw new InvalidOperationException($"Bad or duplicate server id: {server.Id}");
            if (server.Packages.Count == 0)
                throw new InvalidOperationException($"Server {server.Id} has no packages");

            foreach (var (key, package) in server.Packages)
            {
                foreach (var file in package.AppFiles.Concat(package.AssetFiles))
                {
                    if (!PathGuard.IsSafeRelativePath(file.Path))
                        throw new InvalidOperationException($"Unsafe path in {server.Id}/{key}: {file.Path}");
                    if (file.Sha256 is not { Length: 64 })
                        throw new InvalidOperationException($"Bad SHA-256 for {server.Id}/{file.Path}");
                }
            }
        }
    }
}
