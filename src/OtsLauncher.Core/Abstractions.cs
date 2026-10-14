namespace OtsLauncher.Core;

public enum ClientOs
{
    Windows,
    Android,
    Ios,
    Unknown
}

public sealed record PlatformId(ClientOs Os, string Architecture)
{
    public string Key => Os switch
    {
        ClientOs.Windows => $"windows-{Architecture}",
        ClientOs.Android => $"android-{Architecture}",
        ClientOs.Ios => $"ios-{Architecture}",
        _ => $"unknown-{Architecture}"
    };

    public static PlatformId WindowsX64 => new(ClientOs.Windows, "x64");
    public static PlatformId AndroidArm64 => new(ClientOs.Android, "arm64");
    public static PlatformId IosArm64 => new(ClientOs.Ios, "arm64");
}

public interface IPlatformInfo
{
    PlatformId Current { get; }
}

public interface IPlatformPaths
{
    string Root { get; }
    string Clients { get; }
    string Cache { get; }
    string Logs { get; }
    string ProfileDirectory(string serverId);
}

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    Stream OpenRead(string path);
    Stream CreateFile(string path);
    void DeleteFile(string path);
    void MoveFile(string source, string destination, bool overwrite);
    void CopyFile(string source, string destination, bool overwrite);
    string ReadAllText(string path);
    void WriteAllText(string path, string contents);
    long FileSize(string path);
}

public interface IAppLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

public interface IDownloadProgress
{
    void Report(string filePath, long received, long? total);
}

public interface IDownloadService
{
    Task DownloadAsync(ManifestFile file, string destinationPath, string cacheDir, CancellationToken ct, IDownloadProgress? progress = null);
}

public interface IManifestProvider
{
    Task<NetworkManifest> LoadAsync(string pathOrUrl, CancellationToken ct);
}

public interface IClientLauncher
{
    /// <summary>
    /// Starts the profile on this OS. Windows: OTClient exe.
    /// Mobile implementations will open a package/activity — not an .exe path.
    /// </summary>
    Task LaunchAsync(ResolvedProfile profile, string profileDirectory, CancellationToken ct);
}

public enum LaunchKind
{
    Executable,
    Package,
    Unsupported
}
