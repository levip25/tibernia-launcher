namespace OtsLauncher.Core;

public sealed class DownloadOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; init; } = 3;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(8);
}

public sealed class SafeDownloader : IDownloadService
{
    private readonly HttpClient _http;
    private readonly IAppLog _log;
    private readonly IFileSystem _fs;
    private readonly DownloadOptions _options;

    public SafeDownloader(HttpClient http, IAppLog log, DownloadOptions? options = null, IFileSystem? fs = null)
    {
        _http = http;
        _log = log;
        _fs = fs ?? new SystemFileSystem();
        _options = options ?? new DownloadOptions();
        _http.Timeout = _options.Timeout;
    }

    public Task DownloadAsync(ManifestFile file, string destinationPath, string cacheDir, CancellationToken ct, IDownloadProgress? progress = null)
        => DownloadFileAsync(file, destinationPath, cacheDir, ct, progress);

    public async Task DownloadFileAsync(ManifestFile file, string destinationPath, string cacheDir, CancellationToken ct, IDownloadProgress? progress = null)
    {
        var url = file.Url ?? throw new InvalidOperationException($"No URL for {file.Path}");
        if (!IsAllowedUrl(url))
            throw new InvalidOperationException($"Refusing URL for {file.Path}");

        _fs.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
        _fs.CreateDirectory(cacheDir);

        var temp = Path.Combine(cacheDir, Guid.NewGuid().ToString("N") + ".part");
        Exception? last = null;
        var attempts = Math.Max(1, _options.MaxRetries);
        var completedAttempts = 0;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            completedAttempts = attempt;
            ct.ThrowIfCancellationRequested();
            try
            {
                _log.Info($"download attempt {attempt} {file.Path}");
                await CopyToTemporaryFileAsync(url, temp, file, progress, ct);

                await using (var verify = _fs.OpenRead(temp))
                {
                    var hash = HashUtil.Sha256Stream(verify);
                    if (!HashUtil.EqualsHex(hash, file.Sha256))
                    {
                        _log.Error($"hash mismatch {file.Path} got={hash}");
                        throw new InvalidOperationException($"SHA-256 mismatch for {file.Path}");
                    }
                }

                if (file.Size > 0 && _fs.FileSize(temp) != file.Size)
                {
                    throw new InvalidOperationException($"Size mismatch for {file.Path}");
                }

                progress?.Report(file.Path, file.Size, file.Size);
                _fs.MoveFile(temp, destinationPath, overwrite: true);
                _log.Info($"stored {file.Path}");
                return;
            }
            catch (Exception ex) when (ex is OperationCanceledException && !ct.IsCancellationRequested)
            {
                last = ex;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
            }

            _fs.DeleteFile(temp);
            if (last is null || attempt >= attempts || !ShouldRetry(last, url))
                break;

            var delay = RetryDelayFor(attempt);
            _log.Warn($"retry {file.Path} in {delay.TotalMilliseconds:0}ms: {last.Message}");
            await Task.Delay(delay, ct);
        }

        _fs.DeleteFile(temp);
        last ??= new InvalidOperationException($"Failed to download {file.Path}");
        throw new DownloadFailedException(
            file.Path,
            Classify(last),
            completedAttempts,
            last,
            (last as HttpRequestException)?.StatusCode);
    }

    private static bool IsAllowedUrl(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

    private async Task CopyToTemporaryFileAsync(
        string url,
        string temp,
        ManifestFile file,
        IDownloadProgress? progress,
        CancellationToken ct)
    {
        await using var output = _fs.CreateFile(temp);
        var buffer = new byte[128 * 1024];
        long received = 0;
        var expected = file.Size > 0 ? file.Size : (long?)null;
        progress?.Report(file.Path, 0, expected);

        if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            await using var local = File.OpenRead(new Uri(url).LocalPath);
            await CopyWithProgressAsync(local, output, buffer, file.Path, expected, progress, ct);
            return;
        }

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        expected ??= response.Content.Headers.ContentLength;
        await CopyWithProgressAsync(input, output, buffer, file.Path, expected, progress, ct);

        async Task CopyWithProgressAsync(
            Stream source,
            Stream destination,
            byte[] copyBuffer,
            string filePath,
            long? total,
            IDownloadProgress? downloadProgress,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                var count = await source.ReadAsync(copyBuffer.AsMemory(), cancellationToken);
                if (count == 0)
                    break;
                await destination.WriteAsync(copyBuffer.AsMemory(0, count), cancellationToken);
                received += count;
                downloadProgress?.Report(filePath, received, total);
            }
        }
    }

    private TimeSpan RetryDelayFor(int failedAttempt)
    {
        var multiplier = Math.Pow(2, Math.Max(0, failedAttempt - 1));
        var milliseconds = Math.Min(
            _options.RetryDelay.TotalMilliseconds * multiplier,
            _options.MaxRetryDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
    }

    private static bool ShouldRetry(Exception ex, string url)
    {
        if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return false;

        return ex switch
        {
            HttpRequestException { StatusCode: null } => true,
            HttpRequestException { StatusCode: System.Net.HttpStatusCode.RequestTimeout } => true,
            HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } => true,
            HttpRequestException http when IsServerError(http.StatusCode) => true,
            TaskCanceledException => true,
            IOException => true,
            _ => false
        };
    }

    private static DownloadFailureKind Classify(Exception ex) => ex switch
    {
        TaskCanceledException => DownloadFailureKind.Timeout,
        HttpRequestException http when IsServerError(http.StatusCode) => DownloadFailureKind.Server,
        HttpRequestException { StatusCode: not null } => DownloadFailureKind.Server,
        HttpRequestException => DownloadFailureKind.Network,
        UnauthorizedAccessException => DownloadFailureKind.Storage,
        IOException => DownloadFailureKind.Storage,
        InvalidOperationException invalid when invalid.Message.Contains("SHA-256", StringComparison.OrdinalIgnoreCase)
            || invalid.Message.Contains("Size mismatch", StringComparison.OrdinalIgnoreCase) => DownloadFailureKind.Integrity,
        InvalidOperationException => DownloadFailureKind.Configuration,
        _ => DownloadFailureKind.Unknown
    };

    private static bool IsServerError(System.Net.HttpStatusCode? statusCode) =>
        statusCode is not null && (int)statusCode.Value >= 500;
}
