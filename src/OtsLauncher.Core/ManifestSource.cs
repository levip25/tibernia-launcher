namespace OtsLauncher.Core;

public sealed class ManifestSource : IManifestProvider
{
    private readonly ManifestStore _store = new();
    private readonly HttpClient _http;
    private readonly IAppLog _log;
    private readonly string? _cachePath;

    public ManifestSource(HttpClient http, IAppLog log, string? cachePath = null)
    {
        _http = http;
        _log = log;
        _cachePath = cachePath;
    }

    public async Task<NetworkManifest> LoadAsync(string pathOrUrl, CancellationToken ct)
    {
        _log.Info($"manifest source {pathOrUrl}");
        string json;
        if (pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || pathOrUrl.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || pathOrUrl.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new UriBuilder(pathOrUrl);
            var refresh = $"launcher_refresh={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            uri.Query = string.IsNullOrWhiteSpace(uri.Query)
                ? refresh
                : $"{uri.Query.TrimStart('?')}&{refresh}";

            using var request = new HttpRequestMessage(HttpMethod.Get, uri.Uri);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true,
                MaxAge = TimeSpan.Zero
            };
            request.Headers.Pragma.ParseAdd("no-cache");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            json = await response.Content.ReadAsStringAsync(ct);
            var manifest = _store.Parse(json);
            if (!string.IsNullOrWhiteSpace(_cachePath))
            {
                var directory = Path.GetDirectoryName(_cachePath)!;
                Directory.CreateDirectory(directory);
                var temporary = Path.Combine(directory, $".{Path.GetFileName(_cachePath)}.{Guid.NewGuid():N}.tmp");
                try
                {
                    await File.WriteAllTextAsync(temporary, json, ct);
                    File.Move(temporary, _cachePath, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log.Warn($"manifest cache unavailable: {ex.Message}");
                }
                finally
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
            }

            return manifest;
        }

        json = await File.ReadAllTextAsync(pathOrUrl, ct);
        return _store.Parse(json);
    }
}
