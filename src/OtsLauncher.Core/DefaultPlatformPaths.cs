namespace OtsLauncher.Core;

public sealed class DefaultPlatformPaths : IPlatformPaths
{
    public string Root { get; }
    public string Clients { get; }
    public string Cache { get; }
    public string Logs { get; }

    public DefaultPlatformPaths(string root)
    {
        Root = Path.GetFullPath(root);
        Clients = Path.Combine(Root, "clients");
        Cache = Path.Combine(Root, "cache");
        Logs = Path.Combine(Root, "logs");
    }

    public string ProfileDirectory(string serverId)
    {
        if (!PathGuard.IsSafeRelativePath(serverId))
            throw new InvalidOperationException($"Bad server id: {serverId}");
        return Path.Combine(Clients, serverId);
    }

    public void EnsureLayout(IFileSystem fs)
    {
        fs.CreateDirectory(Clients);
        fs.CreateDirectory(Cache);
        fs.CreateDirectory(Logs);
        foreach (var id in new[] { "latest", "custom", "classic", "war" })
            fs.CreateDirectory(ProfileDirectory(id));
    }
}
