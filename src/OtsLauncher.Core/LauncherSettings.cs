using System.Text.Json;
using System.Text.Json.Serialization;

namespace OtsLauncher.Core;

public sealed class LauncherSettings
{
    [JsonPropertyName("manifestUrl")]
    public string? ManifestUrl { get; set; }

    public static LauncherSettings LoadIfPresent(string path)
    {
        if (!File.Exists(path))
            return new LauncherSettings();

        var settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? new LauncherSettings();

        if (!string.IsNullOrWhiteSpace(settings.ManifestUrl) && !IsAllowedManifestUrl(settings.ManifestUrl))
            throw new InvalidOperationException("Manifest URL must use HTTPS (localhost HTTP is allowed for development).");

        return settings;
    }

    public static bool IsAllowedManifestUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttps
            || parsed.Scheme == Uri.UriSchemeHttp && (parsed.IsLoopback || parsed.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)));
}
