namespace OtsLauncher.Core;

public sealed class FileIndexer
{
    private const string AssetsVersionFile = ".ots-assets-version";
    private const string PackageVersionFile = ".ots-package-version";
    private readonly IFileSystem _fs;

    public FileIndexer(IFileSystem? fs = null) => _fs = fs ?? new SystemFileSystem();

    public ProfileStatus Inspect(ResolvedProfile profile, string profileRoot, bool includeAppFiles)
    {
        var checks = new List<FileCheck>();
        void add(IEnumerable<ManifestFile> files, string bucket)
        {
            foreach (var file in files)
            {
                var full = PathGuard.ResolveUnder(profileRoot, file.Path);
                FileStatus status;
                if (!_fs.FileExists(full))
                    status = FileStatus.Missing;
                else
                {
                    using var stream = _fs.OpenRead(full);
                    var hash = HashUtil.Sha256Stream(stream);
                    status = HashUtil.EqualsHex(hash, file.Sha256) ? FileStatus.Ok : FileStatus.Mismatch;
                }
                checks.Add(new FileCheck { File = file, Status = status, Bucket = bucket });
            }
        }

        if (includeAppFiles)
            add(profile.Package.AppFiles, "app");
        add(profile.Package.AssetFiles, "assets");

        var versionFile = Path.Combine(profileRoot, AssetsVersionFile);
        var local = _fs.FileExists(versionFile) ? _fs.ReadAllText(versionFile).Trim() : "none";
        return new ProfileStatus { Profile = profile, LocalAssetsVersion = local, Files = checks };
    }

    public bool IsVersionCurrent(ResolvedProfile profile, string profileRoot, bool includeAppFiles)
    {
        if (!_fs.DirectoryExists(profileRoot))
            return false;

        if (includeAppFiles && profile.LaunchKind == LaunchKind.Executable)
        {
            var entry = PathGuard.ResolveUnder(profileRoot, profile.Package.Launch.Entry);
            if (!_fs.FileExists(entry))
                return false;
        }

        var expected = VersionStamp(profile);
        var packageFile = Path.Combine(profileRoot, PackageVersionFile);
        if (_fs.FileExists(packageFile))
            return string.Equals(_fs.ReadAllText(packageFile).Trim(), expected, StringComparison.Ordinal);

        // One-time migration for installations made before package stamps existed.
        var legacyFile = Path.Combine(profileRoot, AssetsVersionFile);
        if (_fs.FileExists(legacyFile))
        {
            if (!string.Equals(_fs.ReadAllText(legacyFile).Trim(), profile.Package.AssetsVersion, StringComparison.Ordinal))
                return false;
        }
        else
        {
            // Existing pre-stamp installations get a quick, one-time size check.
            // Full SHA-256 verification remains available through Repair files.
            var files = includeAppFiles
                ? profile.Package.AppFiles.Concat(profile.Package.AssetFiles)
                : profile.Package.AssetFiles;
            foreach (var file in files)
            {
                var full = PathGuard.ResolveUnder(profileRoot, file.Path);
                if (!_fs.FileExists(full) || (file.Size > 0 && _fs.FileSize(full) != file.Size))
                    return false;
            }
            _fs.WriteAllText(legacyFile, profile.Package.AssetsVersion);
        }
        _fs.WriteAllText(packageFile, expected);
        return true;
    }

    public void WriteLocalVersion(string profileRoot, string version)
    {
        _fs.CreateDirectory(profileRoot);
        _fs.WriteAllText(Path.Combine(profileRoot, ".ots-assets-version"), version);
    }

    public void WriteLocalVersion(string profileRoot, ResolvedProfile profile)
    {
        WriteLocalVersion(profileRoot, profile.Package.AssetsVersion);
        _fs.WriteAllText(Path.Combine(profileRoot, PackageVersionFile), VersionStamp(profile));
    }

    private static string VersionStamp(ResolvedProfile profile) =>
        $"{profile.Package.PackageVersion}:{profile.Package.AssetsVersion}";
}
