using OtsLauncher.Core;
using System.Net;
using System.Net.Http;
using Xunit;

namespace OtsLauncher.Core.Tests;

public class CriticalTests
{
    private static NetworkManifest Load() => new ManifestStore().LoadFromFile(FindManifest());

    [Theory]
    [InlineData("classic", "772")]
    [InlineData("latest", "1525")]
    [InlineData("custom", "1525")]
    [InlineData("war", "800")]
    public void Launcher_preserves_bootstrap_and_selects_one_isolated_world(string world, string protocol)
    {
        var root = Path.Combine(Path.GetTempPath(), "ots-bootstrap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "init.lua");
        const string upstream = "Servers_init = {}\ng_app.setCompactName('otclient')\n-- setup logger\ng_modules.autoLoadModules(999)\n";
        File.WriteAllText(path, upstream);
        var profile = new ProfileResolver().Resolve(Load(), world, PlatformId.WindowsX64);
        profile.Server.Protocol = protocol;
        var writer = new OtClientConfigWriter();
        writer.Write(profile, root);
        var first = File.ReadAllText(path);
        writer.Write(profile, root);
        Assert.Equal(first, File.ReadAllText(path));
        Assert.Equal(upstream, first.Replace("dofile('/config/ots-network.lua') -- OTS launcher bootstrap\n\n", ""));
        var config = File.ReadAllText(Path.Combine(root, "config", "ots-network.lua"));
        Assert.Contains($"OTS_SERVER_ID = '{world}'", config);
        Assert.Contains($"OTS_PROTOCOL = '{protocol}'", config);
        var expectedLoginProtocol = int.Parse(protocol) >= 1281 ? 1100 : int.Parse(protocol);
        Assert.Contains($"OTS_LOGIN_PROTOCOL = {expectedLoginProtocol}", config);
        Assert.Contains("OTS_RSA = 'otserv'", config);
        Assert.Contains("httpLogin = false", config);
        Assert.Contains("g_app.setCompactName('ots-' .. OTS_SERVER_ID)", config);
    }

    [Fact]
    public void Missing_bootstrap_is_not_replaced_with_a_nonfunctional_stub()
    {
        var root = Path.Combine(Path.GetTempPath(), "ots-missing-init-" + Guid.NewGuid().ToString("N"));
        var profile = new ProfileResolver().Resolve(Load(), "war", PlatformId.WindowsX64);
        Assert.Throws<InvalidOperationException>(() => new OtClientConfigWriter().Write(profile, root));
        Assert.False(File.Exists(Path.Combine(root, "init.lua")));
    }

    [Fact]
    public void Sha256_matches_known_placeholder()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("WAR-CLIENT-PLACEHOLDER\n");
        Assert.Equal("d9c485f486f73b5022a809897689b7749c65dd8ac589ad34c145793864135b55", HashUtil.Sha256Bytes(bytes));
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("..\\secret")]
    [InlineData("/etc/passwd")]
    [InlineData("modules/../../war/otclient.exe")]
    public void PathGuard_rejects_traversal(string path)
    {
        Assert.False(PathGuard.IsSafeRelativePath(path));
        Assert.Throws<InvalidOperationException>(() => PathGuard.ResolveUnder(Path.GetTempPath(), path));
    }

    [Fact]
    public void Resolves_windows_package_for_war()
    {
        var resolved = new ProfileResolver().Resolve(Load(), "war", PlatformId.WindowsX64);
        Assert.Equal("windows-x64", resolved.Platform.Key);
        Assert.Equal(LaunchKind.Executable, resolved.LaunchKind);
        Assert.Equal("ots-war.exe", resolved.Package.Launch.Entry);
        Assert.NotEmpty(resolved.Package.AppFiles);
    }

    [Fact]
    public void Resolves_android_assets_without_exe()
    {
        var resolved = new ProfileResolver().Resolve(Load(), "classic", PlatformId.AndroidArm64);
        Assert.Equal(LaunchKind.Package, resolved.LaunchKind);
        Assert.Empty(resolved.Package.AppFiles);
        Assert.NotEmpty(resolved.Package.AssetFiles);
    }

    [Fact]
    public void Missing_platform_throws() => Assert.Throws<PlatformNotSupportedException>(() => new ProfileResolver().Resolve(Load(), "war", new PlatformId(ClientOs.Unknown, "mips")));

    [Fact]
    public void Profiles_cannot_touch_sibling()
    {
        var root = Path.Combine(Path.GetTempPath(), "ots-iso-" + Guid.NewGuid().ToString("N"));
        var paths = new DefaultPlatformPaths(root);
        paths.EnsureLayout(new SystemFileSystem());
        File.WriteAllText(Path.Combine(paths.ProfileDirectory("war"), "secret.bin"), "nope");
        Assert.Throws<InvalidOperationException>(() => PathGuard.ResolveUnder(paths.ProfileDirectory("latest"), "../war/secret.bin"));
    }

    [Fact]
    public async Task Interrupted_download_leaves_existing_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ots-dl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, "data", "file.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.WriteAllText(dest, "GOOD");
        var file = new ManifestFile { Path = "data/file.dat", Size = 4, Sha256 = HashUtil.Sha256Bytes(System.Text.Encoding.UTF8.GetBytes("GOOD")), Url = "https://127.0.0.1:1/missing" };
        var log = new AppLog(Path.Combine(dir, "logs"));
        var dl = new SafeDownloader(new HttpClient(), log, new DownloadOptions { Timeout = TimeSpan.FromSeconds(2), MaxRetries = 2, RetryDelay = TimeSpan.FromMilliseconds(10) });
        await Assert.ThrowsAnyAsync<Exception>(() => dl.DownloadFileAsync(file, dest, Path.Combine(dir, "cache"), CancellationToken.None));
        Assert.Equal("GOOD", File.ReadAllText(dest));
    }

    [Fact]
    public async Task Transient_server_error_is_retried_and_then_succeeds()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ots-retry-" + Guid.NewGuid().ToString("N"));
        var bytes = System.Text.Encoding.UTF8.GetBytes("GOOD");
        var handler = new SequenceResponseHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        using var http = new HttpClient(handler);
        var downloader = new SafeDownloader(http, new AppLog(Path.Combine(dir, "logs")), new DownloadOptions
        {
            MaxRetries = 3,
            RetryDelay = TimeSpan.Zero
        });
        var file = new ManifestFile
        {
            Path = "data/file.dat",
            Size = bytes.Length,
            Sha256 = HashUtil.Sha256Bytes(bytes),
            Url = "https://updates.example/file.dat"
        };

        await downloader.DownloadFileAsync(file, Path.Combine(dir, "data", "file.dat"), Path.Combine(dir, "cache"), CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal("GOOD", File.ReadAllText(Path.Combine(dir, "data", "file.dat")));
    }

    [Fact]
    public async Task Permanent_http_error_is_not_retried()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ots-no-retry-" + Guid.NewGuid().ToString("N"));
        var handler = new SequenceResponseHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        var downloader = new SafeDownloader(http, new AppLog(Path.Combine(dir, "logs")), new DownloadOptions
        {
            MaxRetries = 3,
            RetryDelay = TimeSpan.Zero
        });
        var file = new ManifestFile { Path = "missing.dat", Sha256 = new string('0', 64), Url = "https://updates.example/missing.dat" };

        var error = await Assert.ThrowsAsync<DownloadFailedException>(() =>
            downloader.DownloadFileAsync(file, Path.Combine(dir, "missing.dat"), Path.Combine(dir, "cache"), CancellationToken.None));

        Assert.Equal(DownloadFailureKind.Server, error.Kind);
        Assert.Equal(1, error.Attempts);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Integrity_error_is_not_retried_or_installed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ots-integrity-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(dir, "data.dat");
        Directory.CreateDirectory(dir);
        File.WriteAllText(destination, "OLD");
        var handler = new SequenceResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("CORRUPT")
        });
        using var http = new HttpClient(handler);
        var downloader = new SafeDownloader(http, new AppLog(Path.Combine(dir, "logs")), new DownloadOptions
        {
            MaxRetries = 3,
            RetryDelay = TimeSpan.Zero
        });
        var file = new ManifestFile
        {
            Path = "data.dat",
            Sha256 = HashUtil.Sha256Bytes(System.Text.Encoding.UTF8.GetBytes("EXPECTED")),
            Url = "https://updates.example/data.dat"
        };

        var error = await Assert.ThrowsAsync<DownloadFailedException>(() =>
            downloader.DownloadFileAsync(file, destination, Path.Combine(dir, "cache"), CancellationToken.None));

        Assert.Equal(DownloadFailureKind.Integrity, error.Kind);
        Assert.Equal(1, error.Attempts);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("OLD", File.ReadAllText(destination));
    }

    [Fact]
    public void Unknown_server_is_rejected() => Assert.Throws<InvalidOperationException>(() => new ProfileResolver().RequireServer(Load(), "pvp-extra"));

    [Fact]
    public async Task Invalid_remote_manifest_does_not_replace_last_good_cache()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ots-manifest-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cache = Path.Combine(dir, "network.manifest.json");
        const string previous = "last-known-good";
        File.WriteAllText(cache, previous);
        using var http = new HttpClient(new StaticResponseHandler("{not valid json"));
        var source = new ManifestSource(http, new AppLog(Path.Combine(dir, "logs")), cache);

        await Assert.ThrowsAnyAsync<Exception>(() => source.LoadAsync("https://updates.example/network.manifest.json", CancellationToken.None));

        Assert.Equal(previous, File.ReadAllText(cache));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    [Fact]
    public async Task Valid_remote_manifest_is_validated_then_cached()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ots-manifest-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cache = Path.Combine(dir, "network.manifest.json");
        var json = File.ReadAllText(FindManifest());
        using var http = new HttpClient(new StaticResponseHandler(json));
        var source = new ManifestSource(http, new AppLog(Path.Combine(dir, "logs")), cache);

        var manifest = await source.LoadAsync("https://updates.example/network.manifest.json", CancellationToken.None);

        Assert.Equal(4, manifest.Servers.Count);
        Assert.Equal(json, File.ReadAllText(cache));
    }

    [Fact]
    public async Task Remote_manifest_refresh_bypasses_http_caches()
    {
        var json = File.ReadAllText(FindManifest());
        var handler = new StaticResponseHandler(json);
        using var http = new HttpClient(handler);
        var source = new ManifestSource(http, new AppLog(Path.Combine(Path.GetTempPath(), "ots-manifest-refresh-logs")));

        await source.LoadAsync("https://updates.example/network.manifest.json?channel=dev", CancellationToken.None);

        Assert.NotNull(handler.RequestUri);
        Assert.Contains("channel=dev", handler.RequestUri!.Query);
        Assert.Contains("launcher_refresh=", handler.RequestUri.Query);
        Assert.True(handler.CacheControlNoCache);
        Assert.True(handler.CacheControlNoStore);
        Assert.True(handler.PragmaNoCache);
    }

    [Fact]
    public async Task Valid_remote_manifest_is_used_when_local_cache_cannot_be_replaced()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ots-manifest-cache-blocked-" + Guid.NewGuid().ToString("N"));
        var blockedCachePath = Path.Combine(dir, "network.manifest.json");
        Directory.CreateDirectory(blockedCachePath);
        var json = File.ReadAllText(FindManifest());
        using var http = new HttpClient(new StaticResponseHandler(json));
        var source = new ManifestSource(http, new AppLog(Path.Combine(dir, "logs")), blockedCachePath);

        var manifest = await source.LoadAsync("https://updates.example/network.manifest.json", CancellationToken.None);

        Assert.Equal(4, manifest.Servers.Count);
        Assert.True(Directory.Exists(blockedCachePath));
    }

    [Theory]
    [InlineData("https://updates.example/network.manifest.json", true)]
    [InlineData("http://localhost:8080/network.manifest.json", true)]
    [InlineData("http://127.0.0.1:8080/network.manifest.json", true)]
    [InlineData("http://updates.example/network.manifest.json", false)]
    [InlineData("file:///c:/temp/network.manifest.json", false)]
    public void Launcher_settings_only_allow_secure_remote_manifest_sources(string url, bool allowed) =>
        Assert.Equal(allowed, LauncherSettings.IsAllowedManifestUrl(url));

    [Fact]
    public void Launcher_language_defaults_to_english_and_is_remembered()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ots-language-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "launcher.preferences.json");
        Assert.Equal(LauncherLanguage.English, LauncherPreferences.Load(path).Language);
        new LauncherPreferences { Language = LauncherLanguage.Polish }.Save(path);
        Assert.Equal(LauncherLanguage.Polish, LauncherPreferences.Load(path).Language);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("pl")]
    [InlineData("pt")]
    public void Launcher_language_is_applied_to_all_four_clients(string language)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ots-client-language-" + Guid.NewGuid().ToString("N"));
        var changed = new ClientLanguageService(dir).ApplyToAll(language);
        Assert.Equal(4, changed.Count);
        foreach (var path in changed)
            Assert.Contains($"locale: {language}", File.ReadAllText(path));
    }

    [Fact]
    public async Task Update_service_downloads_only_changed_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ots-delta-" + Guid.NewGuid().ToString("N"));
        var paths = new DefaultPlatformPaths(dir);
        paths.EnsureLayout(new SystemFileSystem());
        var profileRoot = paths.ProfileDirectory("classic");
        var goodBytes = System.Text.Encoding.UTF8.GetBytes("GOOD");
        var changedBytes = System.Text.Encoding.UTF8.GetBytes("NEW");
        File.WriteAllBytes(Path.Combine(profileRoot, "good.dat"), goodBytes);
        File.WriteAllText(Path.Combine(profileRoot, "changed.spr"), "OLD");
        var profile = new ResolvedProfile
        {
            Server = new ServerProfile { Id = "classic", Name = "Classic" },
            Platform = PlatformId.WindowsX64,
            Package = new ClientPackage
            {
                AssetsVersion = "2",
                AssetFiles =
                [
                    new ManifestFile { Path = "good.dat", Size = goodBytes.Length, Sha256 = HashUtil.Sha256Bytes(goodBytes), Url = "https://updates.example/good.dat" },
                    new ManifestFile { Path = "changed.spr", Size = changedBytes.Length, Sha256 = HashUtil.Sha256Bytes(changedBytes), Url = "https://updates.example/changed.spr" }
                ]
            }
        };
        var downloader = new RecordingDownloader();
        var service = new UpdateService(downloader, new AppLog(paths.Logs));

        await service.RepairAsync(profile, paths, includeAppFiles: true, CancellationToken.None);

        Assert.Equal(["changed.spr"], downloader.Paths);
        Assert.Equal("2", File.ReadAllText(Path.Combine(profileRoot, ".ots-assets-version")));
    }

    [Fact]
    public async Task Matching_package_stamp_skips_file_hashing_and_downloads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ots-fast-version-" + Guid.NewGuid().ToString("N"));
        var paths = new DefaultPlatformPaths(dir);
        paths.EnsureLayout(new SystemFileSystem());
        var root = paths.ProfileDirectory("war");
        File.WriteAllText(Path.Combine(root, "ots-war.exe"), "client");
        File.WriteAllText(Path.Combine(root, ".ots-package-version"), "app-8:assets-8");
        var profile = new ResolvedProfile
        {
            Server = new ServerProfile { Id = "war", Name = "War" },
            Platform = PlatformId.WindowsX64,
            Package = new ClientPackage
            {
                PackageVersion = "app-8",
                AssetsVersion = "assets-8",
                Launch = new LaunchSpec { Entry = "ots-war.exe" },
                AssetFiles = [new ManifestFile { Path = "huge.spr", Sha256 = new string('0', 64), Url = "https://updates.example/huge.spr" }]
            }
        };
        var downloader = new RecordingDownloader();
        var changed = await new UpdateService(downloader, new AppLog(paths.Logs))
            .UpdateIfNeededAsync(profile, paths, true, CancellationToken.None);

        Assert.False(changed);
        Assert.Empty(downloader.Paths);
    }

    [Fact]
    public void Pre_stamp_installation_is_migrated_with_fast_size_check()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ots-fast-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ots-war.exe"), "client");
        File.WriteAllText(Path.Combine(dir, "data.dat"), "assets");
        var profile = new ResolvedProfile
        {
            Server = new ServerProfile { Id = "war", Name = "War" },
            Platform = PlatformId.WindowsX64,
            Package = new ClientPackage
            {
                PackageVersion = "app-8", AssetsVersion = "assets-8",
                Launch = new LaunchSpec { Entry = "ots-war.exe" },
                AppFiles = [new ManifestFile { Path = "ots-war.exe", Size = 6, Sha256 = new string('0', 64) }],
                AssetFiles = [new ManifestFile { Path = "data.dat", Size = 6, Sha256 = new string('0', 64) }]
            }
        };

        Assert.True(new FileIndexer().IsVersionCurrent(profile, dir, true));
        Assert.Equal("app-8:assets-8", File.ReadAllText(Path.Combine(dir, ".ots-package-version")));
    }

    [Fact]
    public async Task Downloader_reports_progress_while_copying_a_large_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ots-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var source = Path.Combine(dir, "source.bin");
        var destination = Path.Combine(dir, "client", "source.bin");
        var cache = Path.Combine(dir, "cache");
        var bytes = Enumerable.Range(0, 400_000).Select(index => (byte)(index % 251)).ToArray();
        File.WriteAllBytes(source, bytes);
        var file = new ManifestFile
        {
            Path = "source.bin",
            Size = bytes.Length,
            Sha256 = HashUtil.Sha256Bytes(bytes),
            Url = new Uri(source).AbsoluteUri
        };
        var progress = new RecordingProgress();

        await new SafeDownloader(new HttpClient(), new AppLog(Path.Combine(dir, "logs")))
            .DownloadFileAsync(file, destination, cache, CancellationToken.None, progress);

        Assert.True(progress.Samples.Count >= 4);
        Assert.Contains(progress.Samples, sample => sample.Received > 0 && sample.Received < bytes.Length);
        Assert.Equal(bytes.Length, progress.Samples[^1].Received);
        Assert.Equal(bytes.Length, progress.Samples[^1].Total);
    }

    private static string FindManifest()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "manifests", "samples", "network.manifest.json");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        throw new FileNotFoundException("manifest");
    }

    private sealed class RecordingProgress : IDownloadProgress
    {
        public List<(long Received, long? Total)> Samples { get; } = [];

        public void Report(string filePath, long received, long? total) => Samples.Add((received, total));
    }

    private sealed class StaticResponseHandler(string content) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public bool CacheControlNoCache { get; private set; }
        public bool CacheControlNoStore { get; private set; }
        public bool PragmaNoCache { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            CacheControlNoCache = request.Headers.CacheControl?.NoCache == true;
            CacheControlNoStore = request.Headers.CacheControl?.NoStore == true;
            PragmaNoCache = request.Headers.Pragma.Any(value => value.Name.Equals("no-cache", StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }

    private sealed class SequenceResponseHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (_responses.Count == 0)
                throw new InvalidOperationException("No response configured for request.");
            return Task.FromResult(_responses.Dequeue());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                while (_responses.TryDequeue(out var response))
                    response.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class RecordingDownloader : IDownloadService
    {
        public List<string> Paths { get; } = [];

        public Task DownloadAsync(ManifestFile file, string destinationPath, string cacheDir, CancellationToken ct, IDownloadProgress? progress = null)
        {
            Paths.Add(file.Path);
            return Task.CompletedTask;
        }
    }
}
