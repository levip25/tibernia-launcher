namespace OtsLauncher.Core;

public sealed class ProfileResolver
{
    public ServerProfile RequireServer(NetworkManifest manifest, string serverId)
    {
        var server = manifest.Servers.FirstOrDefault(s =>
            string.Equals(s.Id, serverId, StringComparison.OrdinalIgnoreCase));
        if (server is null)
            throw new InvalidOperationException($"Unknown server profile: {serverId}");
        return server;
    }

    public ResolvedProfile Resolve(NetworkManifest manifest, string serverId, PlatformId platform)
    {
        var server = RequireServer(manifest, serverId);
        if (!server.Packages.TryGetValue(platform.Key, out var package))
            throw new PlatformNotSupportedException(
                $"Server '{serverId}' has no package for {platform.Key}");
        return new ResolvedProfile
        {
            Server = server,
            Platform = platform,
            Package = package
        };
    }
}
