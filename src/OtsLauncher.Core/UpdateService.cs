namespace OtsLauncher.Core;

public sealed class UpdateService
{
    private readonly FileIndexer _indexer;
    private readonly IDownloadService _downloader;
    private readonly IAppLog _log;

    public UpdateService(IDownloadService downloader, IAppLog log, IFileSystem? fs = null)
    {
        _downloader = downloader;
        _log = log;
        _indexer = new FileIndexer(fs);
    }

    public async Task RepairAsync(
        ResolvedProfile profile,
        IPlatformPaths paths,
        bool includeAppFiles,
        CancellationToken ct,
        IDownloadProgress? progress = null)
    {
        var root = paths.ProfileDirectory(profile.Id);
        Directory.CreateDirectory(root);
        // Inspect hashes every file in the package and can take over a minute for
        // a full OTClient tree. Keep that CPU/disk work off the UI thread so the
        // launcher remains responsive while CHECKING is displayed.
        var status = await Task.Run(
            () => _indexer.Inspect(profile, root, includeAppFiles),
            ct).ConfigureAwait(false);
        _log.Info($"repair {profile.Id} platform={profile.Platform.Key} need={status.MissingOrChanged}");

        foreach (var check in status.Files)
        {
            if (check.Status == FileStatus.Ok)
                continue;
            var dest = PathGuard.ResolveUnder(root, check.File.Path);
            var file = WithUrl(profile, check.File);
            await _downloader.DownloadAsync(file, dest, paths.Cache, ct, progress);
        }

        _indexer.WriteLocalVersion(root, profile);
    }

    public async Task<bool> UpdateIfNeededAsync(
        ResolvedProfile profile,
        IPlatformPaths paths,
        bool includeAppFiles,
        CancellationToken ct,
        IDownloadProgress? progress = null)
    {
        var root = paths.ProfileDirectory(profile.Id);
        if (_indexer.IsVersionCurrent(profile, root, includeAppFiles))
        {
            _log.Info($"quick version match {profile.Id} {profile.Package.PackageVersion}:{profile.Package.AssetsVersion}");
            return false;
        }

        await RepairAsync(profile, paths, includeAppFiles, ct, progress);
        return true;
    }

    public static ManifestFile WithUrl(ResolvedProfile profile, ManifestFile file)
    {
        if (!string.IsNullOrWhiteSpace(file.Url))
            return file;
        throw new InvalidOperationException($"No URL for {profile.Id}/{file.Path}");
    }
}
