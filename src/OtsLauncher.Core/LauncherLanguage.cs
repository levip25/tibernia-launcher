using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OtsLauncher.Core;

public static class LauncherLanguage
{
    public const string English = "en";
    public const string Polish = "pl";
    public const string BrazilianPortuguese = "pt";

    public static string Normalize(string? value) => value?.ToLowerInvariant() switch
    {
        Polish => Polish,
        BrazilianPortuguese or "pt-br" or "pt_br" => BrazilianPortuguese,
        _ => English
    };
}

public sealed class LauncherPreferences
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = LauncherLanguage.English;

    public static LauncherPreferences Load(string path)
    {
        if (!File.Exists(path))
            return new LauncherPreferences();

        try
        {
            var value = JsonSerializer.Deserialize<LauncherPreferences>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? new LauncherPreferences();
            value.Language = LauncherLanguage.Normalize(value.Language);
            return value;
        }
        catch (JsonException)
        {
            return new LauncherPreferences();
        }
    }

    public void Save(string path)
    {
        Language = LauncherLanguage.Normalize(Language);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}

public sealed class ClientLanguageService
{
    private static readonly string[] WorldIds = ["classic", "war", "latest", "custom"];
    private static readonly Regex LocaleLine = new(@"(?m)^\s*locale\s*:\s*[^\r\n]*$", RegexOptions.CultureInvariant);
    private readonly string _applicationData;

    public ClientLanguageService(string applicationData) => _applicationData = Path.GetFullPath(applicationData);

    public IReadOnlyList<string> ApplyToAll(string language)
    {
        language = LauncherLanguage.Normalize(language);
        var updated = new List<string>();
        foreach (var worldId in WorldIds)
        {
            var compactName = "ots-" + worldId;
            var config = Path.Combine(_applicationData, "otcr", compactName, compactName, "config.otml");
            Directory.CreateDirectory(Path.GetDirectoryName(config)!);
            var contents = File.Exists(config) ? File.ReadAllText(config) : string.Empty;
            var locale = $"locale: {language}";
            contents = LocaleLine.IsMatch(contents)
                ? LocaleLine.Replace(contents, locale, 1)
                : locale + Environment.NewLine + contents;
            var temporary = config + ".tmp";
            File.WriteAllText(temporary, contents);
            File.Move(temporary, config, true);
            updated.Add(config);
        }
        return updated;
    }
}
